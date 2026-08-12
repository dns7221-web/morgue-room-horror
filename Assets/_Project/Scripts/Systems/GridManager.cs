using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 강사님이 알려준 뱀서식 3x3 무한 맵 재활용을 그대로 구현한 격자 매니저.
/// <see cref="GridCell"/> 프리팹 하나를 9번 복제해 3x3으로 깔고, 플레이어가
/// 중앙 칸에서 인접 칸으로 넘어갈 때마다 <b>반대편에 뒤처진 행/열 전체를
/// 진행 방향 앞쪽으로 재배치</b>한다.
///
/// ── 핵심 개념 전환 (구 5안과 다른 점) ─────────────────────
/// 구 5안(RoomModulePool)은 "플레이어를 매번 시작점으로 순간이동"시켰지만,
/// 이 격자는 <b>플레이어가 순간이동 없이 계속 걸어가고, 대신 칸(방)들이
/// 재배치</b>된다. 이게 뱀서 타일 재활용의 본질이다 — 카메라(플레이어)는
/// 그대로 두고 타일이 재활용된다.
///
/// ── 은폐 ──────────────────────────────────────────────
/// 재활용 대상 행/열은 항상 새 중앙에서 최소 2칸 떨어져 있다. "모든 문은
/// 기본적으로 닫혀 있고, 방금 지나온 문만 열려 있다"는 규칙만 지키면 그
/// 사이에 닫힌 문이 최소 하나 끼어, 재배치 순간이 시야에 안 걸린다
/// (Stalker의 SightBlocker 레이어와 같은 발상 — 닫힌 문은 이미 이 게임에서
/// 불투명하게 다뤄진다). 그래서 <see cref="OnPlayerEnteredCell"/>이 방금
/// 들어온 문을 곧장 닫는다.
///
/// ── 좌표→월드 변환은 UnityEngine.Grid에 맡긴다 ────────────
/// 직접 벡터 수식을 계산하는 대신 유니티 내장 Grid 컴포넌트의
/// CellToWorld()를 쓴다. 계산을 대신해주는 것도 이득이지만,
/// 더 큰 이득은 <b>씬 뷰에 격자 눈금이 실제로 그려진다는 것</b> — 칸이
/// 제자리에 놓였는지 스크린샷으로 눈대중할 필요 없이 그 자리에서 바로 보인다.
/// </summary>
[RequireComponent(typeof(Grid))]
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    private static readonly Vector2Int North = new(0, 1);
    private static readonly Vector2Int East = new(1, 0);
    private static readonly Vector2Int South = new(0, -1);
    private static readonly Vector2Int West = new(-1, 0);

    [Header("Setup")]
    [Tooltip("복제해서 3x3으로 깔 격자 칸 프리팹. 사방 소켓·문이 전부 있어야 한다.")]
    [SerializeField] private GridCell cellPrefab;

    private Grid grid;
    private readonly Dictionary<Vector2Int, GridCell> cells = new();

    /// <summary>플레이어가 지금 서 있는 칸의 격자 좌표.</summary>
    public Vector2Int CenterCoord { get; private set; }

    /// <summary>플레이어가 지금 서 있는 칸. 아직 초기화 전이면 null.</summary>
    public GridCell CenterCell => cells.TryGetValue(CenterCoord, out var c) ? c : null;

    /// <summary>지금 격자에 놓인 칸들 (좌표 → 칸). 표시판이 읽어간다 — 쓰기는 격자만 한다.</summary>
    public IReadOnlyDictionary<Vector2Int, GridCell> Cells => cells;

    /// <summary>
    /// <see cref="MeasureSpacing"/>이 실측한 칸 간격 (x, z). 곧 칸 하나가 차지해도 되는
    /// 크기이기도 해서, 표시판이 "지오메트리가 이 몫을 넘지 않는가"를 검사하는 데 쓴다.
    /// </summary>
    public Vector2 Spacing => grid != null ? new Vector2(grid.cellSize.x, grid.cellSize.z) : Vector2.zero;

    /// <summary>
    /// 마지막 재활용 기록 — 표시판이 "방금 무슨 일이 있었나"를 보여주기 위한 것.
    ///
    /// 재활용은 <b>보이면 안 되는</b> 사건이라, 정상 동작일수록 화면에 아무 일도 일어나지
    /// 않는다. 그래서 일어났는지 여부 자체를 눈으로 확인할 방법이 따로 있어야 한다.
    /// </summary>
    public struct RecycleRecord
    {
        public Vector2Int direction;   // 진행 방향
        public Vector2Int from;        // 옮기기 전 행/열의 대표 좌표
        public Vector2Int to;          // 옮긴 뒤 대표 좌표
        public float time;             // Time.time
    }

    /// <summary>마지막 재활용 기록. 아직 한 번도 없었으면 direction이 zero.</summary>
    public RecycleRecord LastRecycle { get; private set; }

    // ── GameManager가 판정 로직을 여기에 접붙이기 위한 신호 ────────────────
    //
    // GridManager는 "칸이 어디 있는가"만 알고 "그 칸에 뭐가 있어야 하는가"는 모른다
    // (관심사 분리 — GameManager와 같은 철학). 그래서 직접 판정 로직을 부르는 대신
    // 이벤트만 낸다. 구독자가 없어도(GameManager가 gridMode를 안 켰거나 아직 없어도)
    // 격자 자체는 그대로 돌아간다.

    /// <summary>3x3 칸이 처음 다 서고 플레이어가 중앙에 놓인 직후, 그 중앙 칸과 함께 딱 한 번 울린다.</summary>
    public event System.Action<GridCell> Initialized;

    /// <summary>플레이어가 선 칸(중앙)이 바뀔 때마다 새 CenterCell과 함께 울린다.</summary>
    public event System.Action<GridCell> CenterChanged;

    /// <summary>재활용으로 자리를 옮긴 칸을 한 개씩, 새 자리에 놓인 <b>뒤에</b> 알린다 — "이 칸을 새로 꾸며라"는 신호.</summary>
    public event System.Action<GridCell> CellRecycled;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        grid = GetComponent<Grid>();
    }

    private void Start()
    {
        Initialize();
    }

    /// <summary>3x3 칸 생성 + 간격 실측 + 배치. 1회만 호출.</summary>
    public void Initialize()
    {
        MeasureSpacing();

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                var coord = new Vector2Int(x, y);
                var cell = Instantiate(cellPrefab, transform);
                cell.name = $"{cellPrefab.name}_{x}_{y}";
                cell.Coord = coord;
                cells[coord] = cell;
                PlaceCell(cell, coord);

                // 방 프리팹의 복도 문은 3안 시절 설정을 물려받아 잠긴 채로 저장돼 있다.
                // 격자엔 판정 관문이 없으니 여기서 풀어준다 (GridCell 주석 참고).
                cell.UnlockAllDoors();
            }
        }

        CenterCoord = Vector2Int.zero;

        SpawnPlayerInCenter();

        Initialized?.Invoke(CenterCell);
    }

    /// <summary>
    /// 플레이어를 가운데 칸의 스폰 지점에 세운다. <b>게임을 켤 때 딱 한 번</b>이다 —
    /// 이 격자는 플레이어가 제 발로 걸어다니는 구조라, 그 뒤로는 순간이동이 없다
    /// (구 3안이 매 루프 플레이어를 옮기던 것과 정반대). 씬에 찍혀 있던 자리는
    /// 옛 건물 기준이라, 여기서 한 번 데려오지 않으면 격자 밖에 남는다.
    /// </summary>
    private void SpawnPlayerInCenter()
    {
        var spawn = CenterCell != null ? CenterCell.SpawnPoint : null;
        if (spawn == null)
        {
            Debug.LogError("[GridManager] 가운데 칸에 Spawn Point가 없어 플레이어를 데려오지 못했습니다 — 격자 밖에 남습니다.", this);
            return;
        }

        var teleporter = FindAnyObjectByType<PlayerTeleporter>();
        if (teleporter == null)
        {
            Debug.LogError("[GridManager] 씬에서 PlayerTeleporter를 찾지 못했습니다.", this);
            return;
        }

        teleporter.TeleportTo(spawn.position, spawn.rotation);
    }

    /// <summary>
    /// 문 안쪽 트리거(CellBoundaryTrigger)에서 호출: 플레이어가 어느 칸 안으로
    /// 완전히 들어섰다. 그 칸의 좌표와 지금 중앙 좌표를 비교해 이동 방향을
    /// 스스로 계산하므로, 트리거 쪽에 "이 문은 북쪽" 같은 수동 설정이 필요 없다.
    /// </summary>
    public void OnPlayerEnteredCell(GridCell enteredCell)
    {
        Vector2Int dir = enteredCell.Coord - CenterCoord;
        if (dir == Vector2Int.zero) return; // 이미 중앙인 칸 — 문간을 서성이다 되돌아온 경우 등

        if (Mathf.Abs(dir.x) + Mathf.Abs(dir.y) != 1)
        {
            // 대각선/원거리 이동은 있을 수 없다(문은 인접 칸끼리만 연결). 발생하면
            // 좌표 갱신 로직에 사고가 있다는 뜻이라, 조용히 넘어가지 않고 알린다.
            Debug.LogError($"[GridManager] 인접하지 않은 칸으로 진입 감지: {CenterCoord} → {enteredCell.Coord}", this);
            CenterCoord = enteredCell.Coord;
            return;
        }

        CenterCoord += dir;
        RecycleTrailingLine(dir);
        CenterChanged?.Invoke(CenterCell);

        // 은폐 규칙 — 방금 지나온 경계의 문을 전부 곧장 닫는다(이중문이면 양쪽 다).
        // 한쪽만 닫으면 나머지 한쪽 틈으로 재활용 순간이 새어 보인다.
        //
        // 경계 하나에 문이 <b>어느 칸 소속인지 정해져 있지 않다.</b> 문짝을 북·동에만
        // 달면 지나온 경계의 문은 <b>떠나온 칸</b>이 들고 있어서, 들어온 칸만 보면
        // 닫을 것을 못 찾고 문이 열린 채 남는다. 양쪽 칸에 다 걸어 둔다.
        foreach (var door in enteredCell.GetDoors(-dir))
            if (door != null) door.Close();

        if (cells.TryGetValue(CenterCoord - dir, out var departed) && departed != null)
            foreach (var door in departed.GetDoors(dir))
                if (door != null) door.Close();
    }

    /// <summary>
    /// 새 중앙 기준으로 <b>반대편에 2칸 뒤처진 행/열</b>을 진행 방향 3칸
    /// 앞으로 옮긴다 — "배열이 한 칸 굴러가는" 뱀서 재활용의 실제 계산.
    ///
    /// dir을 90도 돌린 벡터(perp)로 그 행/열의 나머지 두 칸을 훑는다 —
    /// 가로 이동이든 세로 이동이든 같은 코드로 처리된다(축을 나눠 분기하지 않음).
    /// </summary>
    private void RecycleTrailingLine(Vector2Int dir)
    {
        Vector2Int perp = new(-dir.y, dir.x);
        Vector2Int edgeBase = CenterCoord - dir * 2; // 새 중앙 기준 뒤처진 행/열의 대표 좌표

        LastRecycle = new RecycleRecord
        {
            direction = dir,
            from = edgeBase,
            to = edgeBase + dir * 3,
            time = Time.time,
        };

        for (int i = -1; i <= 1; i++)
        {
            Vector2Int oldCoord = edgeBase + perp * i;
            if (!cells.TryGetValue(oldCoord, out var cell))
            {
                Debug.LogError($"[GridManager] 재활용 대상 칸이 없습니다: {oldCoord}", this);
                continue;
            }

            Vector2Int newCoord = oldCoord + dir * 3;
            cells.Remove(oldCoord);
            cells[newCoord] = cell;
            cell.Coord = newCoord;
            PlaceCell(cell, newCoord);
            CellRecycled?.Invoke(cell);
        }
    }

    /// <summary>
    /// 지정 칸의 사방 출입문을 전부 잠그거나 푼다 — 판정 전엔 어느 방향으로도
    /// 못 나가게 GameManager가 쓴다.
    ///
    /// 문짝은 <b>북·동에만</b> 있다(겪은 버그 ⑤ — 경계마다 문이 두 짝이면 두 번 열게
    /// 된다). 그래서 이 칸의 남·서쪽 경계는 <b>이웃 칸이 실제 문을 들고 있다</b> —
    /// 칸 좌표를 기준으로 소유자를 다시 찾아야 하고, 그게 GridCell 하나만으로는 안 되고
    /// 격자 전체를 아는 GridManager에 있어야 하는 이유다.
    /// </summary>
    public void SetCellExitsLocked(GridCell cell, bool locked)
    {
        if (cell == null) return;
        Vector2Int coord = cell.Coord;

        LockDoors(cell.GetDoors(North), locked);
        LockDoors(cell.GetDoors(East), locked);

        if (cells.TryGetValue(coord + South, out var southNeighbor) && southNeighbor != null)
            LockDoors(southNeighbor.GetDoors(North), locked);
        if (cells.TryGetValue(coord + West, out var westNeighbor) && westNeighbor != null)
            LockDoors(westNeighbor.GetDoors(East), locked);
    }

    private static void LockDoors(Door[] doors, bool locked)
    {
        foreach (var d in doors)
            if (d != null) d.SetLocked(locked);
    }

    /// <summary>
    /// 월드 좌표가 속한 칸 좌표. 격자 밖이어도 좌표 자체는 항상 나온다 —
    /// 존재 여부는 호출부가 <see cref="Cells"/>로 확인한다.
    ///
    /// ⚠️ <see cref="Grid.WorldToCell"/>을 그대로 쓰면 안 된다 — 그건 칸의 <b>모서리</b>가
    /// 경계라고 가정하는데, <see cref="PlaceCell"/>이 놓는 방 프리팹은 원점이 <b>방
    /// 정중앙</b>이다(PlaceCell 주석 참고 — 프리팹 원점을 CellToWorld 결과에 그대로 맞춘다).
    /// 그래서 floor 대신 <b>제일 가까운 칸 중심</b>으로 반올림해야 방의 절반이 옆 칸으로
    /// 오판되지 않는다.
    /// </summary>
    public Vector2Int GetCoordAt(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        return new Vector2Int(
            Mathf.RoundToInt(local.x / grid.cellSize.x),
            Mathf.RoundToInt(local.z / grid.cellSize.z));
    }

    /// <summary>
    /// 인접한 두 칸 사이 경계의 문이 전부 닫혀 있는지 — 추격자 피신 규칙(GameManager.
    /// PlayerIsSheltered)이 쓴다. 인접하지 않으면 막을 문이 없다는 뜻이라 true(안전)로 본다.
    /// </summary>
    public bool AreBoundaryDoorsClosed(Vector2Int a, Vector2Int b)
    {
        Vector2Int dir = b - a;
        if (Mathf.Abs(dir.x) + Mathf.Abs(dir.y) != 1) return true;

        foreach (var door in GetBoundaryDoors(a, dir))
            if (door != null && door.IsOpen) return false;
        return true;
    }

    /// <summary>
    /// a에서 dir 방향 경계의 문을 찾는다. 문짝은 북·동에만 있어(SetCellExitsLocked 참고)
    /// 남·서쪽 경계는 <b>이웃 칸이 반대쪽(북·동)으로 들고 있는 문</b>을 대신 찾는다.
    /// </summary>
    private Door[] GetBoundaryDoors(Vector2Int a, Vector2Int dir)
    {
        if (dir == North || dir == East)
            return cells.TryGetValue(a, out var cell) && cell != null
                ? cell.GetDoors(dir) : System.Array.Empty<Door>();

        return cells.TryGetValue(a + dir, out var neighbor) && neighbor != null
            ? neighbor.GetDoors(-dir) : System.Array.Empty<Door>();
    }

    private void PlaceCell(GridCell cell, Vector2Int coord)
    {
        // GetCellCenterWorld가 아니라 CellToWorld를 쓴다 — 전자는 '칸 중심'을 주느라
        // 칸 크기의 절반(Y로도 0.5m)을 더해서, 격자가 이 오브젝트 자리에서 통째로
        // 어긋난다. 칸 (0,0)이 GridManager 자리에 정확히 놓여야 위치를 눈으로 잡을 수 있다.
        Vector3 pos = grid.CellToWorld(new Vector3Int(coord.x, 0, coord.y));
        cell.PlaceAt(pos, transform.rotation);
    }

    /// <summary>
    /// 대향 소켓 쌍(N↔S, E↔W) 간격을 프로브 하나로 실측해 <see cref="Grid.cellSize"/>에
    /// 써넣는다 — 좌표 하드코딩 없이 칸 하나만 자로 삼는 RoomModulePool.MeasureSlots()와
    /// 같은 패턴. y값은 이 격자에서 안 쓰이므로 1로 채워만 둔다(0이면 Grid가 나눗셈에서
    /// 오류를 낸다).
    /// </summary>
    /// <summary>
    /// 씬 뷰에 칸 경계와 좌표를 그린다.
    ///
    /// 재활용은 <b>보이면 안 되는 사건</b>이라 게임 화면에서는 아무 일도 일어나지 않는
    /// 것이 정상이다. 그런데 그러면 <b>제대로 도는지도 볼 수가 없다.</b> 표시판의 숫자만
    /// 으로는 "배열이 한 칸 굴러갔다"는 것이 잘 안 읽혀서, 씬 뷰에 칸을 직접 그린다 —
    /// 게임뷰는 착시를, 씬뷰는 원리를 보여준다는 이 포폴의 시연 방식 그대로다.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (cells.Count == 0) return;   // 에디트 모드에는 칸이 없다

        Vector2 s = Spacing;
        var size = new Vector3(s.x, 0.2f, s.y);

        foreach (var kv in cells)
        {
            if (kv.Value == null) continue;

            bool isCenter = kv.Key == CenterCoord;
            bool justMoved = LastRecycle.direction != Vector2Int.zero
                          && Time.time - LastRecycle.time < 2f
                          && kv.Key.x >= LastRecycle.to.x - 1 && kv.Key.x <= LastRecycle.to.x + 1
                          && kv.Key.y >= LastRecycle.to.y - 1 && kv.Key.y <= LastRecycle.to.y + 1;

            Gizmos.color = justMoved ? Color.red
                         : isCenter ? Color.green
                         : new Color(1f, 1f, 1f, 0.25f);

            Vector3 center = kv.Value.transform.position;
            Gizmos.DrawWireCube(center, size);

            // 방금 옮겨온 줄은 바닥을 칠해 눈에 확 띄게 한다.
            if (justMoved)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.12f);
                Gizmos.DrawCube(center, size);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = isCenter ? Color.green : Color.white;
            UnityEditor.Handles.Label(center + Vector3.up * 2f,
                                      isCenter ? $"[{kv.Key.x}, {kv.Key.y}] ◀ 지금 여기" : $"{kv.Key.x}, {kv.Key.y}");
#endif
        }
    }

    private void MeasureSpacing()
    {
        var probe = Instantiate(cellPrefab, transform);
        probe.PlaceAt(transform.position, transform.rotation);

        var east = probe.GetSocket(East);
        var west = probe.GetSocket(West);
        var north = probe.GetSocket(North);
        var south = probe.GetSocket(South);

        if (east == null || west == null || north == null || south == null)
        {
            Debug.LogError(
                $"[GridManager] {cellPrefab.name}에 사방 소켓이 전부 있어야 간격을 잴 수 있습니다. " +
                "칸이 전부 같은 자리에 겹쳐 나타납니다.", this);
        }
        else
        {
            float spacingX = Vector3.Dot(east.transform.position - west.transform.position, Vector3.right);
            float spacingZ = Vector3.Dot(north.transform.position - south.transform.position, Vector3.forward);
            grid.cellSize = new Vector3(spacingX, 1f, spacingZ);
        }

        Destroy(probe.gameObject);
    }
}
