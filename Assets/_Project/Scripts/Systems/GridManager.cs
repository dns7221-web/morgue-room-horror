using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 강사님이 알려준 뱀서식 무한 맵 재활용을 그대로 구현한 격자 매니저.
/// <see cref="GridCell"/> 프리팹 하나를 한 변(<see cref="Radius"/> 참고, 기본 3)만큼
/// 복제해 정사각 격자로 깔고, 플레이어가 중앙 칸에서 인접 칸으로 넘어갈 때마다
/// <b>반대편에 뒤처진 행/열 전체를 진행 방향 앞쪽으로 재배치</b>한다.
///
/// ── 핵심 개념 전환 (구 5안과 다른 점) ─────────────────────
/// 구 5안(RoomModulePool)은 "플레이어를 매번 시작점으로 순간이동"시켰지만,
/// 이 격자는 <b>플레이어가 순간이동 없이 계속 걸어가고, 대신 칸(방)들이
/// 재배치</b>된다. 이게 뱀서 타일 재활용의 본질이다 — 카메라(플레이어)는
/// 그대로 두고 타일이 재활용된다.
///
/// ── 은폐 ──────────────────────────────────────────────
/// 재활용 대상 행/열은 항상 새 중앙에서 최소 <c>Radius + 1</c>칸 떨어져 있다
/// (기본 3x3이면 2칸). "모든 문은 기본적으로 닫혀 있고, 방금 지나온 문만 열려
/// 있다"는 규칙만 지키면 그 사이에 닫힌 문이 최소 하나 끼어, 재배치 순간이
/// 시야에 안 걸린다 (Stalker의 SightBlocker 레이어와 같은 발상 — 닫힌 문은 이미
/// 이 게임에서 불투명하게 다뤄진다). 그래서 <see cref="OnPlayerEnteredCell"/>이
/// 방금 들어온 문을 곧장 닫는다.
///
/// ⚠️ 이건 오래도록 <b>검사되지 않는 가정</b>이었다 — 사람이 눈으로 보고 "안 보이네"
/// 한 것이 전부였다. 지금은 <see cref="CoverageAudit"/>가 재활용이 일어나는 프레임마다
/// 실제로 가려져 있었는지 검사해서, 주장이 아니라 결과(위반 횟수)로 남긴다.
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
    [Tooltip("복제해서 깔 격자 칸 프리팹. 사방 소켓·문이 전부 있어야 한다.")]
    [SerializeField] private GridCell cellPrefab;

    // 추가 생성 — 한 변을 필드로 뺐다. 전에는 3이 네 군데(생성 범위 ±1, 뒤처짐 2,
    // 이동량 3, 줄 길이 ±1)에 매직 넘버로 흩어져 있어서, 읽는 사람이 그 1·2·3의
    // 관계를 좌표를 그려가며 복원해야 했다. 아래 Radius 하나에서 전부 유도하면
    // 그 관계가 코드에 그대로 적힌다.
    [Tooltip("한 변의 칸 수. 중앙 칸이 생기려면 홀수여야 한다. 3이 최소.")]
    [SerializeField, Min(3)] private int gridSize = 3;

    // 추가 생성 — 은폐 검사기. 비워도 격자는 그대로 돈다(GridDebugPanel과 같은 철학:
    // 검증 장치가 없어도 본체는 혼자 굴러가야 한다).
    [Tooltip("은폐 검사기. 비우면 같은 오브젝트에서 자동으로 찾는다. 아예 없으면 검사만 안 돌고 격자는 그대로 돈다.")]
    [SerializeField] private CoverageAudit coverageAudit;

    private Grid grid;
    private readonly Dictionary<Vector2Int, GridCell> cells = new();

    // 추가 생성 — 이번에 옮길 칸들. 검사기가 '사라지는 자리'를 먼저 봐야 하므로
    // 옮기기 전에 한 줄을 통째로 모아 둬야 한다. 한 칸 넘어갈 때 한 번만 쓰는
    // 목록이라 매번 new하지 않고 재사용한다(GC 부담 0).
    private readonly List<GridCell> movingCells = new();

    /// <summary>
    /// 중앙에서 격자 끝까지의 칸 수. 한 변 N에서 <c>(N-1)/2</c>.
    ///
    /// ── 왜 3이 최소인가 ───────────────────────────────────
    /// 재활용되는 줄은 <b>옮기기 전</b> 자리가 새 중앙에서 <c>Radius + 1</c>칸 뒤에
    /// 있다. N=3이면 2칸 — 그 사이에 경계가 둘, 닫힌 문이 두 짝 끼어 있어 재배치
    /// 순간이 시야에 안 걸린다. N=1은 이웃 칸이 아예 없어 격자가 성립하지 않으므로
    /// <b>3이 최소</b>다. N=5면 여유가 3칸으로 늘지만 칸이 9개에서 25개가 된다
    /// (상시 로드 2.8배) — 여유를 더 살 이유가 없어서 3으로 둔다.
    /// </summary>
    public int Radius => (gridSize - 1) / 2;

    /// <summary>
    /// 생성 시점의 좌표를 숫자 패드 번호로 바꾼다 — 3x3이면 7 8 9 / 4 5 6 / 1 2 3, 가운데가 5.
    ///
    /// 가운데 = (칸 수 + 1) / 2, 오른쪽으로 +1, 위로 +한 변. 한 변이 5면 13이 가운데가 된다.
    /// 번호는 <b>생성 때 한 번만</b> 매긴다 — 재활용으로 좌표가 바뀌어도 번호는 칸을 따라간다.
    /// </summary>
    private int NumpadId(int x, int y) => (gridSize * gridSize + 1) / 2 + x + y * gridSize;

    /// <summary>
    /// 짝수를 넣으면 중앙 칸이 조각과 조각 사이 이음매에 와서 격자가 성립하지 않는다.
    /// 인스펙터에서 바꾸는 즉시 홀수로 보정하고 한 줄 알린다 — 플레이해 보고 나서야
    /// "9칸이 아니라 16칸이 깔렸다"를 깨닫는 것보다 낫다.
    /// </summary>
    private void OnValidate()
    {
        if (gridSize % 2 != 0) return;

        gridSize++;
        Debug.LogWarning($"[GridManager] 한 변은 홀수여야 중앙 칸이 생깁니다 — {gridSize}로 보정했습니다.", this);
    }

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
    // 이벤트만 낸다. 구독자가 없어도(씬에 GameManager가 아직/아예 없어도)
    // 격자 자체는 그대로 돌아간다 — 격자를 단독으로 검증할 수 있는 이유다.

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

        // 추가 생성 — 검사기는 보통 이 오브젝트에 함께 붙는다. 슬롯 연결을 잊으면
        // <b>격자는 검사를 안 부르는데 표시판은 검사기를 찾아내</b> "검사 없음"만
        // 뜨는 어긋난 상태가 된다 — 붙여놨는데 안 도는 종류라 원인을 찾기 어렵다.
        // AnomalyManager가 자식에서 Anomaly를 모으고 GridCell이 StalkerPoint를
        // 자동으로 찾는 것과 같은 이유로, 여기서도 찾아 둔다.
        if (coverageAudit == null) coverageAudit = GetComponent<CoverageAudit>();
    }

    private void Start()
    {
        Initialize();
    }

    /// <summary>칸 생성(한 변 gridSize) + 간격 실측 + 배치. 1회만 호출.</summary>
    public void Initialize()
    {
        MeasureSpacing();

        int r = Radius;   // 추가 생성 — 루프 안에서 매번 나눗셈하지 않게 한 번만 받는다
        for (int x = -r; x <= r; x++)
        {
            for (int y = -r; y <= r; y++)
            {
                var coord = new Vector2Int(x, y);
                var cell = Instantiate(cellPrefab, transform);
                cell.name = $"{cellPrefab.name}_{x}_{y}";
                cell.Coord = coord;
                // 추가 생성 (2026-10-26) — 숫자 패드 모양 번호(7 8 9 / 4 5 6 / 1 2 3).
                // 가운데가 5, 오른쪽이 +1, 위가 +한 변. 한 번 매기면 평생 안 바뀐다(GridCell.Id 참고).
                cell.Id = NumpadId(x, y);
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

        // 추가 생성 (2026-10-27) — 지나온 경계 말고도, 격자에 열려 있는 문은 전부 닫는다.
        //
        // ── 왜 필요한가 (자동 플레이테스트에서 겪은 버그) ──────────
        // 문을 닫는 곳이 위의 '지나온 경계' 한 곳뿐이었다. 그래서 문을 열어보고 <b>다른 쪽으로</b>
        // 나가면, 쓰지 않은 그 문은 영영 열린 채 남았다 — 재활용으로 칸이 옮겨가도 그대로.
        //  • 열린 문이 일직선으로 늘어서면 40~50m 밖 칸까지 보여 재활용 순간이 노출된다
        //    (은폐 규칙의 전제 "방금 지나온 문만 열려 있다"가 깨진다)
        //  • 나중에 그 칸에 다시 들어가면 판정 없이 그 문으로 걸어 나갈 수 있다
        //    (잠금은 E 상호작용만 막고, 이미 열린 문은 그냥 통과된다)
        // 새 칸에 들어선 순간엔 그 칸의 사방이 다시 잠기므로(GameManager.OnGridCenterChanged)
        // 다른 문이 열려 있을 이유가 없다 — 한꺼번에 닫아 규칙을 원래대로 되돌린다.
        CloseAllOpenDoors();
    }

    /// <summary>
    /// 추가 생성 (2026-10-27) — 격자의 열린 문을 전부 닫는다.
    /// 문짝은 북·동에만 달려 있으므로(SetCellExitsLocked 참고) 칸마다 그 두 방향만 훑으면
    /// 격자 안의 모든 경계를 한 번씩 본다. Door.Close()는 이미 닫힌 문이면 아무것도 안 한다.
    /// </summary>
    private void CloseAllOpenDoors()
    {
        foreach (var cell in cells.Values)
        {
            if (cell == null) continue;
            foreach (var door in cell.GetDoors(North)) if (door != null) door.Close();
            foreach (var door in cell.GetDoors(East)) if (door != null) door.Close();
        }
    }

    /// <summary>
    /// 새 중앙 기준으로 <b>반대편에 (Radius+1)칸 뒤처진 행/열</b>을 진행 방향으로
    /// <b>한 변(gridSize)만큼</b> 옮긴다 — "배열이 한 칸 굴러가는" 뱀서 재활용의 실제 계산.
    ///
    /// ── 숫자가 아니라 관계로 적는다 (추가 생성) ───────────
    /// 전에는 2와 3이 그대로 박혀 있었다. 둘은 독립된 값이 아니라 한 변 N에서
    /// 나오는 관계다 — <b>뒤처짐 = R+1</b>, <b>이동량 = 2R+1 = N</b>. 그래서 숫자를
    /// 지우고 관계를 적었다. 한 변을 5로 바꿔도 이 함수는 그대로 맞는다.
    ///
    /// <b>이동량이 왜 N인가 —</b> 옮기기 전 그 줄은 새 중앙 기준 상대좌표 −(R+1)에
    /// 있고, 가야 할 자리는 격자의 맨 앞 +R이다. 둘의 차가 곧 N이다.
    ///
    /// dir을 90도 돌린 벡터(perp)로 그 행/열의 나머지 칸을 훑는다 —
    /// 가로 이동이든 세로 이동이든 같은 코드로 처리된다(축을 나눠 분기하지 않음).
    /// </summary>
    private void RecycleTrailingLine(Vector2Int dir)
    {
        // 추가 생성 — 재활용 한 번이 몇 ms인지 렉 미터가 읽어간다(LagMeterRows 참고).
        // 이 포폴의 주장이 "끊김 없이 걸어서 넘어간다"인데, 여기가 무거우면 그 프레임이
        // 튀어 주장 자체가 깨진다. 지금까지는 안 튀는 것을 눈으로만 확인했다.
        using var _ = s_recycleMarker.Auto();

        int r = Radius;
        Vector2Int perp = new(-dir.y, dir.x);
        Vector2Int edgeBase = CenterCoord - dir * (r + 1); // 새 중앙 기준 뒤처진 행/열의 대표 좌표

        LastRecycle = new RecycleRecord
        {
            direction = dir,
            from = edgeBase,
            to = edgeBase + dir * gridSize,
            time = Time.time,
        };

        // ── 1) 옮길 칸을 먼저 모은다 (추가 생성) ───────────────────────────
        // 검사기가 '사라지는 자리'를 봐야 하는데, 옮기기 시작하면 그 자리가 이미
        // 없다. 이 시점의 cell.Coord는 아직 <b>옛 좌표</b>라 검사기가 그대로 읽는다.
        movingCells.Clear();
        for (int i = -r; i <= r; i++)
        {
            Vector2Int oldCoord = edgeBase + perp * i;
            if (!cells.TryGetValue(oldCoord, out var cell))
            {
                Debug.LogError($"[GridManager] 재활용 대상 칸이 없습니다: {oldCoord}", this);
                continue;
            }
            movingCells.Add(cell);
        }

        // ── 2) 사라지는 자리가 가려져 있었는지 (추가 생성) ─────────────────
        coverageAudit?.Check(movingCells, CenterCoord, CoverageAudit.Phase.Leaving);

        // ── 3) 실제로 옮긴다 ──────────────────────────────────────────────
        foreach (var cell in movingCells)
        {
            Vector2Int oldCoord = cell.Coord;
            Vector2Int newCoord = oldCoord + dir * gridSize;

            cells.Remove(oldCoord);
            cells[newCoord] = cell;
            cell.Coord = newCoord;
            PlaceCell(cell, newCoord);
            CellRecycled?.Invoke(cell);
        }

        // ── 4) 나타난 자리도 가려져 있는지 (추가 생성) ─────────────────────
        // 직후만 봐도 될 것 같지만 아니다 — 둘은 다른 사건이다. 2번은 "사라지는
        // 걸 봤는가", 4번은 "나타나는 걸 봤는가"를 검사한다. 하나만 두면 반쪽이다.
        coverageAudit?.Check(movingCells, CenterCoord, CoverageAudit.Phase.Arriving);
    }

    // 추가 생성 — 렉 미터용 계측 구간. 이름을 LagMeterRows가 그대로 등록한다.
    // 프로파일러가 꺼진 빌드에서는 유니티가 이 구간을 사실상 비용 없이 넘긴다.
    private static readonly ProfilerMarker s_recycleMarker = new ProfilerMarker("GridManager.Recycle");

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
    /// <paramref name="from"/> 칸에서 <paramref name="target"/> 칸 쪽으로 나가는
    /// <b>첫 경계</b>의 문이 전부 닫혀 있는지 — 은폐 검사(<see cref="CoverageAudit"/>)가 쓴다.
    ///
    /// ── 왜 '첫 경계'만 보나 ───────────────────────────────
    /// "그 칸이 안 보인다"를 엄밀히 따지면 두 칸 사이 모든 경로를 훑어야 하지만,
    /// 그럴 필요가 없다. 플레이어가 선 칸에서 <b>그 방향으로 나가는 문이 닫혀 있으면</b>
    /// 그 방향으로는 아무것도 볼 수 없다 — 더 멀리 있는 것도 당연히 못 본다.
    ///
    /// 대각선 칸은 첫 걸음이 둘(가로·세로)이라 <b>둘 다 닫혀 있어야</b> 막힌 것으로 본다.
    /// 한쪽이라도 열려 있으면 "볼 수 있을지도 모른다"로 넘기고, 실제로 보였는지는
    /// 검사기가 시야 판정으로 확인한다. 틀리는 쪽이 <b>더 엄격한 쪽</b>이라 안전하다.
    /// </summary>
    public bool IsSealedToward(Vector2Int from, Vector2Int target)
    {
        Vector2Int delta = target - from;
        if (delta == Vector2Int.zero) return false;   // 자기 칸은 사이에 끼울 문이 없다

        if (delta.x != 0)
        {
            var step = new Vector2Int(delta.x > 0 ? 1 : -1, 0);
            if (!AreBoundaryDoorsClosed(from, from + step)) return false;
        }

        if (delta.y != 0)
        {
            var step = new Vector2Int(0, delta.y > 0 ? 1 : -1);
            if (!AreBoundaryDoorsClosed(from, from + step)) return false;
        }

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

            // 방금 옮겨온 줄: to를 중심으로 한 변만큼 퍼져 있다 (±Radius).
            int r = Radius;
            bool isCenter = kv.Key == CenterCoord;
            bool justMoved = LastRecycle.direction != Vector2Int.zero
                          && Time.time - LastRecycle.time < 2f
                          && kv.Key.x >= LastRecycle.to.x - r && kv.Key.x <= LastRecycle.to.x + r
                          && kv.Key.y >= LastRecycle.to.y - r && kv.Key.y <= LastRecycle.to.y + r;

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
            // 수정 (2026-10-26): 좌표가 아니라 칸 번호(숫자 패드)를 쓴다 — 표시판 미니맵과 같은 이유다.
            // 좌표는 한 칸 움직일 때마다 아홉 개가 전부 바뀌어서 "어느 줄이 건너뛰었나"가 안 읽힌다.
            // 번호는 칸에 붙어 있으니, 씬 뷰에서 번호가 통째로 옮겨 가는 모습이 그대로 보인다.
            UnityEditor.Handles.color = isCenter ? Color.green : Color.white;
            UnityEditor.Handles.Label(center + Vector3.up * 2f,
                                      isCenter ? $"[{kv.Value.Id}] ◀ 지금 여기" : kv.Value.Id.ToString());
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
