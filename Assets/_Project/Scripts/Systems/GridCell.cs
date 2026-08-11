using UnityEngine;

/// <summary>
/// 3x3 격자 한 칸을 대표하는 방. 사방(N/E/S/W)에 문+소켓을 하나씩 가진다.
///
/// ── RoomModule과의 차이 ────────────────────────────────
/// RoomModule(구 5안)은 소켓 하나를 축으로 180도 뒤집어 반대편에 재배치했지만,
/// 이 칸은 <b>전부 같은 방향을 보는 순수 격자</b>라 뒤집을 필요가 없다. 재활용은
/// 평행이동뿐이라 오히려 계산이 더 단순하다(GridManager 참고).
///
/// 소켓/문 참조는 <see cref="GridManager"/>가 대향 소켓 쌍(N↔S, E↔W) 간격을
/// 재는 자로도 쓰고, 재활용 후 "방금 들어온 문을 닫는" 은폐 규칙에도 쓴다.
/// </summary>
public class GridCell : MonoBehaviour, IAnomalyHost
{
    /// <summary>4방향 중 하나. Inspector에서 실수로 (2,1) 같은 값을 넣는 사고를 막기 위해 enum으로 받는다.</summary>
    public enum CardinalDirection { North, East, South, West }

    [System.Serializable]
    public struct DoorSide
    {
        public CardinalDirection direction;
        [Tooltip("이 방향 접합부. forward는 모듈 바깥을 향해야 한다.")]
        public RoomSocket socket;
        [Tooltip("이 방향 문(들). 이중문이면 2개 다 등록 — 재활용 시 이 방향 문은 전부 닫힌다.")]
        public Door[] doors;
    }

    [Tooltip("사방 문/소켓. N·E·S·W 네 개를 전부 등록해야 GridManager가 간격을 잴 수 있다.")]
    [SerializeField] private DoorSide[] sides;

    [Tooltip("게임을 켰을 때 플레이어가 설 자리. 가운데 칸에서만 쓰인다(GridManager가 시작 시 1회 호출).")]
    [SerializeField] private Transform spawnPoint;

    /// <summary>게임 시작 시 플레이어가 설 자리. 비어 있으면 null.</summary>
    public Transform SpawnPoint => spawnPoint;

    /// <summary>지금 이 칸이 격자에서 차지한 좌표. GridManager가 배치·재활용 때마다 갱신한다.</summary>
    public Vector2Int Coord { get; set; }

    /// <summary>enum 방향을 격자 좌표 단위 벡터로 변환.</summary>
    public static Vector2Int ToVector(CardinalDirection dir) => dir switch
    {
        CardinalDirection.North => new Vector2Int(0, 1),
        CardinalDirection.East => new Vector2Int(1, 0),
        CardinalDirection.South => new Vector2Int(0, -1),
        CardinalDirection.West => new Vector2Int(-1, 0),
        _ => Vector2Int.zero,
    };

    /// <summary>지정 방향의 소켓. 없으면 null(연결 누락 — 호출부가 경고를 남긴다).</summary>
    public RoomSocket GetSocket(Vector2Int direction)
    {
        foreach (var s in sides)
            if (ToVector(s.direction) == direction) return s.socket;
        return null;
    }

    /// <summary>지정 방향의 문(들). 등록이 없으면 빈 배열(연결 누락이어도 순회는 안전하게).</summary>
    public Door[] GetDoors(Vector2Int direction)
    {
        foreach (var s in sides)
            if (ToVector(s.direction) == direction) return s.doors ?? System.Array.Empty<Door>();
        return System.Array.Empty<Door>();
    }

    /// <summary>
    /// 등록된 문을 전부 <b>잠금 해제</b>한다.
    ///
    /// 방 프리팹의 복도 문은 구 3안 시절의 설정을 그대로 물려받아 <b>잠긴 채로</b>
    /// 저장돼 있다 — 그때는 GameManager가 판정을 마친 뒤 풀어줬기 때문이다.
    /// 격자에는 그런 관문이 없으므로(플레이어가 자유롭게 돌아다니는 것이 전부다)
    /// 칸을 만들 때 한 번 풀어준다.
    ///
    /// 프리팹 값을 고치지 않고 코드로 푸는 이유: 이 설정은 <b>3안과 공유하는</b>
    /// 프리팹에 들어 있어, 파일에서 바꾸면 판정 전에 못 나가야 하는 3안 규칙이 깨진다.
    /// </summary>
    public void UnlockAllDoors()
    {
        foreach (var s in sides)
        {
            if (s.doors == null) continue;
            foreach (var d in s.doors)
                if (d != null) d.SetLocked(false);
        }
    }

    /// <summary>지정 위치·회전으로 이동시킨다 (재활용 시 GridManager가 호출 — 값 기반 배치, RoomModule.PlaceAt과 같은 패턴).</summary>
    public void PlaceAt(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
    }

    // ── IAnomalyHost — RoomModule과 같은 계약을 구현한다 ────────────────────
    //
    // AnomalyManager·StalkerPoint는 인스펙터에 손으로 연결하지 않고 자식에서 찾는다.
    // GridCellBuilder가 조립할 때 이미 "Anomalies"(AnomalyManager 포함)와
    // "StalkerPoint" 오브젝트를 이름 그대로 만들어 두므로, 기존 GridCell_v3 프리팹을
    // 다시 조립하지 않아도 그대로 동작한다.

    private AnomalyManager anomalyManager;
    private Transform stalkerSpawnPoint;
    private bool refsResolved;

    /// <summary>로그 식별용 이름 (IAnomalyHost).</summary>
    public string DisplayName => name;

    /// <summary>이번 세팅에서 이 칸에 실제로 이상현상이 있는지 (판정 비교용).</summary>
    public bool HasAnomaly { get; private set; }

    /// <summary>이번 칸의 이상현상이 '그것'(추격자)인지.</summary>
    public bool StalkerIsAnomaly { get; private set; }

    /// <summary>
    /// 이번 칸 이상현상 이름 (디버그 표시용). RoomModule.AnomalyLabel과 같은 패턴 —
    /// 판정 자체는 HasAnomaly(있음/없음)만 보므로, 정확히 <b>무엇</b>이 걸렸는지는
    /// 게임 로직에 필요 없다. 그래도 개발 중엔 "이 방에 지금 뭐가 있어야 정답인가"를
    /// 눈으로 확인할 방법이 있어야 진짜 이상현상이 안 보이는 것인지(연출 버그)
    /// 안 걸린 것인지(정상)를 가를 수 있다.
    /// </summary>
    public string AnomalyLabel
    {
        get
        {
            ResolveRefs();
            if (!HasAnomaly) return "없음";
            if (StalkerIsAnomaly) return "그것";
            return anomalyManager != null ? anomalyManager.CurrentName : "있음";
        }
    }

    /// <summary>이 칸에서 '그것'이 서는 자리. 자식의 "StalkerPoint"를 찾는다.</summary>
    public Transform StalkerSpawnPoint
    {
        get { ResolveRefs(); return stalkerSpawnPoint; }
    }

    /// <summary>이상현상 유무를 세팅한다. 실제 발동/복원은 자식 AnomalyManager가 담당.</summary>
    public void SetAnomaly(bool has)
    {
        ResolveRefs();
        HasAnomaly = has;
        StalkerIsAnomaly = false;
        if (anomalyManager != null) anomalyManager.SetAnomaly(has);
    }

    /// <summary>
    /// 이번 칸의 이상현상을 '그것'으로 고정한다 (칸 데이터만 — 물리적 등장은
    /// GameManager가 <see cref="StalkerSpawnPoint"/>를 받아 씬 단일 Stalker에 지시한다).
    /// RoomModule.SetStalkerAsAnomaly()와 같은 규칙: 다른 변형은 정상으로 복원한다.
    /// </summary>
    public void SetStalkerAsAnomaly()
    {
        ResolveRefs();
        HasAnomaly = true;
        StalkerIsAnomaly = true;
        if (anomalyManager != null) anomalyManager.SetAnomaly(false);
    }

    /// <summary>
    /// 이 칸이 좌표를 옮겨다니며 여러 번 재활용되는 동안 자식 구성은 바뀌지 않으므로,
    /// 한 번만 찾아 캐시한다.
    /// </summary>
    private void ResolveRefs()
    {
        if (refsResolved) return;
        refsResolved = true;

        anomalyManager = GetComponentInChildren<AnomalyManager>(true);
        var t = transform.Find("StalkerPoint");
        stalkerSpawnPoint = t;

        if (anomalyManager == null)
            Debug.LogWarning($"[GridCell] {name} 아래에서 AnomalyManager를 찾지 못했습니다 — 이상현상이 하나도 안 나옵니다.", this);
        if (stalkerSpawnPoint == null)
            Debug.LogWarning($"[GridCell] {name} 아래에서 'StalkerPoint'를 찾지 못했습니다 — 추격자가 등장할 자리가 없습니다.", this);
    }
}
