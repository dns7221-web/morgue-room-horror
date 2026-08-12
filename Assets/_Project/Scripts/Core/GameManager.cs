using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 전체 진행을 지휘하는 두뇌(중앙 매니저).
///
/// ── 담당 ──────────────────────────────────────────────
///  • 진행도 관리 (0 ~ clearGoal, 정답 +1 / 오답 0)
///  • 판정 처리   (플레이어 O/X vs 실제 이상현상 유무)
///  • 루프 진행   (판정 → 문 열림 → 복도를 걷다 암전 → 다시 영안실 문 앞)
///  • 목표 도달 시 클리어
///
/// ── 한 루프의 흐름 ────────────────────────────────────
///  1. 영안실에서 이상현상 판정 → 복도 문이 열린다
///  2. 복도를 잠시 걷는다 (여기까진 아무 일도 안 일어남)
///  3. 복도 중간 트리거 → 화면이 검게 페이드아웃
///  4. <b>암전 상태에서</b> 건물을 반대쪽 슬롯으로 옮기고 플레이어를 스냅
///  5. 밝아지면 다시 영안실 문 앞 — 플레이어는 계속 걸어온 줄 안다
///
/// 화면에 존재하는 건물은 언제나 하나뿐이라, 이음새를 들킬 여지가 없다.
///
/// 다른 스크립트(판정 UI, 복도 트리거)가 쉽게 접근하도록 싱글톤으로 둔다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("방 모듈 풀. 재활용으로 공간 반복을 만든다 (활성 모듈이 곧 현재 방).")]
    [SerializeField] private RoomModulePool pool;
    [Tooltip("플레이어 순간이동 담당. 암전 순간 새 모듈 시작점으로 스냅.")]
    [SerializeField] private PlayerTeleporter teleporter;
    [Tooltip("복도 통과 시 화면을 검게 페이드해 건물 이동을 가리는 연출. 비우면 즉시 스왑(디버그용).")]
    [SerializeField] private ScreenFader screenFader;
    [Tooltip("오답 순간 잠깐 번쩍이는 그림. 비워도 동작에는 지장 없다(연출만 빠짐).")]
    [SerializeField] private ScareFlash wrongAnswerFlash;
    [Tooltip("클리어 화면. 목표 달성 후 복도로 나서면 나타난다. 씬에서는 꺼둘 것.")]
    [SerializeField] private GameObject clearScreen;
    [Tooltip("씬 단일 추격자. 모듈이 몇 개 켜지든 이 한 마리만 존재한다 — 어느 방에 세울지는 그때그때 RoomModule.StalkerSpawnPoint를 넘겨 지시한다.")]
    [SerializeField] private Stalker stalker;

    [Header("3x3 격자 모드")]
    [Tooltip("켜면 RoomModulePool 대신 GridManager.CenterCell을 판정 대상으로 쓴다. " +
             "3안 경로(pool 참조)는 건드리지 않고 완전히 갈라진 경로를 새로 탄다 — " +
             "이 토글이 꺼져 있으면 아래 격자 코드는 전부 무시된다.")]
    [SerializeField] private bool gridMode;

    [Header("Rules")]
    [Tooltip("이 횟수만큼 연속 성공하면 클리어.")]
    [SerializeField] private int clearGoal = 8;
    [Tooltip("방에 이상현상이 나타날 확률 (0~1).")]
    [SerializeField, Range(0f, 1f)] private float anomalyChance = 0.5f;
    [Tooltip("이 횟수만큼 누적으로 틀리면 추격자가 나타난다. 0이면 등장하지 않는다.")]
    [SerializeField, Min(0)] private int mistakesBeforeStalker = 3;

    [Header("Debug")]
    [Tooltip("체크 시 O(있음)/X(없음) 키로 판정 테스트. 판정 UI 만들면 끄면 됨.")]
    [SerializeField] private bool enableTestKeys = true;

    // ── 상태 ──
    private int progress;
    private bool judged;            // 이번 방을 이미 판정했는지 (중복 방지)
    private bool doorwayOccupied;   // 플레이어 몸이 문간(문짝 궤적)에 걸쳐 있는지

    // 누적 오답 수. progress는 틀릴 때마다 0으로 리셋되므로 '몇 번 틀렸나'를
    // 따로 세야 한다. 추격자 등장 조건에 쓰고, 등장시키면 다시 0으로 돌린다.
    private int mistakes;
    // 플레이어가 안전구역(영안실) 안에 있는지.
    private bool inSafeZone;

    /// <summary>지금 RoomModulePool(3안) 대신 GridManager(격자)를 판정 대상으로 쓰는지.</summary>
    private bool GridMode => gridMode && GridManager.Instance != null;

    /// <summary>
    /// 플레이어가 지금 안전한지.
    ///
    /// ── 3안 ── <b>안전구역(키패드 방) 안 + 영안실 쪽 이중문이 닫힘</b>.
    /// 뛰어들어오는 것만으로는 부족하고, 돌아서서 문을 닫아야 성립한다.
    ///
    /// ── 격자 ── "칸"이 아니라 <b>경계 문</b>이 기준이다. 격자는 방 유형이 없어
    /// 3안의 '안전한 방' 개념이 그대로 안 옮겨지지만, 3안 규칙의 본질은 사실
    /// "나와 추격자 사이 문이 닫혀 있다"였다. 그걸 그대로 일반화한다 —
    /// <b>내 칸 ≠ 추격자가 있는 칸이고, 그 사이 경계 문이 닫혀 있으면 안전.</b>
    /// 같은 칸에 있으면 문을 다 닫아도 안전할 수 없다(자기 칸은 경계가 아니므로
    /// 첫 조건에서 이미 걸러진다). 인접하지 않으면 애초에 위협이 안 닿으므로 안전으로 본다
    /// (AreBoundaryDoorsClosed가 처리).
    /// </summary>
    public bool PlayerIsSheltered
    {
        get
        {
            if (GridMode)
            {
                var gm = GridManager.Instance;
                if (stalker == null || gm == null) return false;

                Vector2Int playerCoord = gm.CenterCoord;
                Vector2Int stalkerCoord = gm.GetCoordAt(stalker.transform.position);
                if (playerCoord == stalkerCoord) return false;

                return gm.AreBoundaryDoorsClosed(playerCoord, stalkerCoord);
            }

            if (!inSafeZone || pool == null) return false;
            var active = pool.Active;
            return active != null && active.AreShelterDoorsClosed();
        }
    }

    /// <summary>안전구역(SafeZone) 출입 상태 갱신.</summary>
    public void SetInSafeZone(bool inside) => inSafeZone = inside;

    public int Progress => progress;
    /// <summary>클리어에 필요한 연속 성공 횟수 (진행도 UI가 "n / 목표" 표시에 사용).</summary>
    public int ClearGoal => clearGoal;
    public bool IsCleared => progress >= clearGoal;
    /// <summary>이번 방을 이미 판정했는지 (키패드가 중복 판정을 막는 데 사용).</summary>
    public bool HasJudged => judged;

    /// <summary>누적 오답 수 (디버그 표시용).</summary>
    public int Mistakes => mistakes;
    /// <summary>추격자 등장까지 필요한 누적 오답 수. 0이면 등장하지 않음.</summary>
    public int MistakesBeforeStalker => mistakesBeforeStalker;
    /// <summary>현재 방(3안) 또는 현재 칸(격자)에 이상현상이 있는지 (디버그·강제 판정용).</summary>
    public bool CurrentRoomHasAnomaly => CurrentAnomalyHost != null && CurrentAnomalyHost.HasAnomaly;

    /// <summary>지금 판정 대상인 방/칸. GridMode에 따라 갈린다 — Judge()·Dress()가 여기 하나만 본다.</summary>
    private IAnomalyHost CurrentAnomalyHost
    {
        get
        {
            if (GridMode) return GridManager.Instance.CenterCell;
            return pool != null ? pool.Active : null;
        }
    }
    /// <summary>현재 활성 모듈 (디버그 표시용, 3안 전용).</summary>
    public RoomModule CurrentRoom => pool != null ? pool.Active : null;

    /// <summary>지금 화면에 켜져 있는 모듈 개수 (디버그 검증용). 3안 구조에서는 언제나 1이 정상.</summary>
    public int ActiveModuleCount => pool != null ? pool.ActiveModuleCount : 0;

    /// <summary>씬 단일 추격자 (디버그 표시용).</summary>
    public Stalker Stalker => stalker;

    /// <summary>[디버그] 이번 방/칸의 이상현상을 즉시 '그것'으로 바꾼다.</summary>
    public void DebugMakeStalkerAnomaly()
    {
        var room = CurrentAnomalyHost;
        if (room == null) return;
        SetStalkerAsAnomaly(room);
        judged = false;
        Debug.Log("[Debug] 이번 방 이상현상을 '그것'으로 교체");
    }

    /// <summary>
    /// 이 방/칸을 '그것' 방으로 세팅하고, 씬 단일 추격자를 그 스폰 지점에 등장시킨다.
    /// 방 데이터 세팅과 물리적 등장을 한 곳에서 묶어, 한쪽만 호출해 생기는
    /// "정답은 있음인데 화면엔 아무것도 없는" 사고를 막는다.
    /// </summary>
    private void SetStalkerAsAnomaly(IAnomalyHost room)
    {
        room.SetStalkerAsAnomaly();

        if (stalker == null)
        {
            Debug.LogWarning("[GameManager] Stalker가 연결돼 있지 않아 '있음'인데 아무것도 나타나지 않습니다.", this);
            return;
        }
        stalker.Appear(room.StalkerSpawnPoint);
    }

    /// <summary>씬 단일 추격자를 물린다.</summary>
    private void DismissStalker()
    {
        if (stalker != null) stalker.Vanish();
    }

    /// <summary>
    /// 현재 활성 모듈의 루트 Transform. 풀 초기화 전에는 null.
    /// 씬에 고정으로 놓인 월드 UI가 방을 따라다니는 데 사용 (FollowActiveRoom).
    /// </summary>
    public Transform ActiveRoom
    {
        get
        {
            if (pool == null) return null;
            var active = pool.Active;
            return active != null ? active.transform : null;
        }
    }

    private InputAction judgeYesAction; // O = 있음
    private InputAction judgeNoAction;  // X = 없음

    private void Awake()
    {
        // 싱글톤 세팅
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (enableTestKeys)
        {
            judgeYesAction = new InputAction("JudgeYes", InputActionType.Button, "<Keyboard>/o");
            judgeNoAction = new InputAction("JudgeNo", InputActionType.Button, "<Keyboard>/x");
        }
    }

    private void OnEnable()
    {
        judgeYesAction?.Enable();
        judgeNoAction?.Enable();
    }

    private void OnDisable()
    {
        judgeYesAction?.Disable();
        judgeNoAction?.Disable();

        if (gridMode && GridManager.Instance != null)
        {
            GridManager.Instance.CenterChanged -= OnGridCenterChanged;
            GridManager.Instance.CellRecycled -= OnGridCellRecycled;
        }
    }

    private void Start()
    {
        if (GridMode)
        {
            var grid = GridManager.Instance;

            // 판정 리셋(칸 전환)·재꾸미기(재활용)를 GridManager 이벤트에 접붙인다.
            // GridManager가 직접 GameManager를 부르지 않는 이유는 관심사 분리 —
            // "칸이 어디 있나"와 "그 칸에 뭐가 있어야 하나"를 갈라 둔다.
            grid.CenterChanged += OnGridCenterChanged;
            grid.CellRecycled += OnGridCellRecycled;

            // GridManager.Start()가 이 스크립트보다 먼저 돌았을 수도, 나중에 돌 수도
            // 있다(스크립트 실행 순서는 보장되지 않는다). 이미 칸이 서 있으면 곧장
            // 시작하고, 아니면 Initialized를 기다린다 — 둘 다 안전하다.
            if (grid.CenterCell != null) BeginGridFirstRoom(grid.CenterCell);
            else grid.Initialized += BeginGridFirstRoom;

            return;
        }

        pool.Initialize();   // 모듈 풀 생성 + 배치 슬롯 실측 + 첫 모듈 배치
        EnterFirstRoom();    // 게임 최초 진입은 루프 재진입과 다르게 취급 (문 밖이 아니라 영안실 안쪽에서 시작)
    }

    private void Update()
    {
        if (!enableTestKeys) return;
        if (judgeYesAction.WasPressedThisFrame()) Judge(true);
        if (judgeNoAction.WasPressedThisFrame()) Judge(false);
    }

    /// <summary>
    /// 계기판(판정 UI)에서 호출. 플레이어의 답과 실제 이상현상 유무를 비교한다.
    /// </summary>
    /// <param name="playerSaysAnomaly">플레이어가 O(있음)를 골랐으면 true.</param>
    public void Judge(bool playerSaysAnomaly)
    {
        if (judged) return;   // 이미 판정한 방이면 무시

        var room = CurrentAnomalyHost;
        if (room == null)
        {
            Debug.LogWarning("[GameManager] 판정할 방/칸을 찾지 못해 무시합니다.", this);
            return;
        }

        judged = true;

        bool correct = (playerSaysAnomaly == room.HasAnomaly);
        if (correct)
        {
            progress++;
            Debug.Log($"[GameManager] 정답! 진행도 {progress}/{clearGoal}");

            // ⚠ 여기서 return하면 안 된다. 아래 문 잠금 해제를 건너뛰어
            // <b>클리어한 플레이어가 방에 갇힌다</b> — 이겼는데 못 나가는 상태.
            if (IsCleared) OnClear();
        }
        else
        {
            progress = 0;
            mistakes++;
            Debug.Log($"[GameManager] 오답! 진행도 0으로 초기화 (누적 오답 {mistakes})");

            // 진행도만 조용히 0이 되면 틀린 게 아프지 않다. 벌은 이미 줬으니
            // 더 뺏는 대신 놀라게 해서 '틀렸다'를 감각으로 남긴다.
            if (wrongAnswerFlash != null) wrongAnswerFlash.Flash();

            // 기준에 닿았으면 '그것'이 다음 방의 이상현상이 된다 (Dress에서 처리).
            if (mistakesBeforeStalker > 0 && mistakes >= mistakesBeforeStalker)
                Debug.Log("[GameManager] 다음 방에는 그것이 있다");
        }

        // 정답/오답과 무관하게 문(들)의 잠금을 푼다.
        // 자동으로 열어주지는 않는다 — 나가는 문은 플레이어가 직접 E로 연다.
        if (GridMode) GridManager.Instance.SetCellExitsLocked(GridManager.Instance.CenterCell, false);
        else pool.Active.SetDoorsLocked(false);
    }

    /// <summary>
    /// 복도 중간 트리거(SeamTrigger)에서 호출: 다음 방으로 진행.
    /// 페이더가 있으면 화면이 완전히 검어진 순간에 실제 스왑(DoAdvance)을 실행해
    /// 건물이 옮겨가는 장면을 가린다. 없으면 즉시 스왑한다.
    /// </summary>
    public void AdvanceToNextRoom()
    {
        // 클리어했다면 다음 방은 없다 — 복도로 나선 이 순간이 끝이다.
        if (IsCleared) { ShowClearScreen(); return; }

        if (screenFader != null) screenFader.FadeThrough(DoAdvance);
        else DoAdvance();
    }

    /// <summary>
    /// 클리어 화면을 띄운다.
    ///
    /// 방을 넘길 때 쓰던 페이더를 <b>그대로</b> 재사용한다 — 암전이 걷히면서
    /// 화면이 드러나므로, 툭 튀어나오는 것보다 "끝났다"가 분명해진다.
    /// 지금껏 암전 뒤에는 늘 <b>또 그 방</b>이 있었으니, 같은 암전 뒤에 다른 것이
    /// 나오는 것 자체가 신호가 된다.
    /// </summary>
    private void ShowClearScreen()
    {
        if (clearScreen == null || clearScreen.activeSelf) return;

        if (screenFader != null) screenFader.FadeThrough(() => clearScreen.SetActive(true));
        else clearScreen.SetActive(true);
    }

    /// <summary>
    /// 실제 진행 처리 (암전 순간): 건물을 반대쪽 슬롯으로 교체한 뒤
    /// 새 방을 꾸미고 플레이어를 그 앞으로 스냅한다.
    /// </summary>
    private void DoAdvance()
    {
        pool.Recycle();   // 옛 건물 컬링 / 새 건물을 반대쪽 슬롯에 세움
        EnterRoom();
    }

    /// <summary>
    /// 게임을 켰을 때 딱 한 번 호출되는 진입. 루프 재진입(EnterRoom)과 달리
    /// 플레이어가 이미 영안실 안(키패드 앞)에 있는 것으로 치므로, 문을 열
    /// 필요도 없고 문 밖으로 스폰시키지도 않는다.
    /// </summary>
    private void EnterFirstRoom()
    {
        // 첫 방은 랜덤을 돌리지 않고 늘 '정상'으로 고정한다.
        // 플레이어가 무엇이 정상인지 눈에 익힌 뒤에야 이상현상을 알아볼 수 있기 때문.
        pool.Active.SetAnomaly(false);
        Debug.Log($"[GameManager] {pool.Active.name} 세팅 — 첫 방이라 이상현상 없음으로 고정");

        judged = false;
        doorwayOccupied = false;
        DismissStalker();
        pool.Active.SetDoorsLocked(true);   // 판정 전까지 복도 문은 잠겨 있다

        var start = pool.Active.FirstEntryPoint;
        if (teleporter != null && start != null)
            teleporter.TeleportTo(start.position, start.rotation);
    }

    /// <summary>
    /// 새 방 한 판을 준비한다 (루프 재진입 — 판정 후 다음 방으로 넘어갈 때만 호출).
    ///
    /// ── 문은 닫아두고 잠금만 푼다 ────────────────────────
    /// 플레이어는 복도(문 밖)에 서게 되고, <b>직접 E로 열고 들어와야</b> 한다.
    /// 나갈 때도 직접 여니 들어올 때도 같게 해서 규칙을 하나로 통일했다.
    ///
    /// 열어둔 채로 시작하지 않는 이유는 <b>상태를 하나로 고정</b>하기 위해서다.
    /// 열어두면 플레이어가 지난 회차에 어느 쪽 문을 열었느냐에 따라 짝짝이가 되고,
    /// 그 차이가 <b>같은 방을 돌려쓴다는 단서</b>가 된다. 늘 '닫힘'이면 단서가 없다.
    ///
    /// 방 안쪽 트리거(RoomEntryTrigger)를 지나면 OnPlayerEnteredRoom()이
    /// 등 뒤에서 문을 쾅 닫고 다시 잠근다.
    /// </summary>
    private void EnterRoom()
    {
        // 이 모듈을 지난번에 썼을 때 플레이어가 열어둔 문이 남아 있다 — 먼저 되돌린다.
        // (Dress보다 앞: 나중에 문을 쓰는 이상현상이 생겨도 덮어쓰지 않도록)
        pool.Active.ResetInteriorDoors();

        // ⚠ 순서 주의: 반드시 Dress보다 <b>먼저</b> 물려야 한다.
        // Dress가 '그것'을 이번 방의 이상현상으로 세울 수 있는데, 뒤에서 물리면
        // 방금 세운 추격자를 그대로 지워버린다.
        DismissStalker();

        Dress(pool.Active);
        judged = false;
        // 옛 모듈 트리거의 점유 상태가 남아 있을 수 있다 (모듈이 꺼지며 Exit을 못 받는 경우).
        doorwayOccupied = false;
        // 문은 <b>닫힌 채로</b> 시작하고, 잠금은 풀어둔다.
        // 플레이어가 직접 E로 열고 들어와야 한다 — 나갈 때와 같은 규칙이다.
        // (잠근 채로 닫아두면 복도에 갇힌다)
        pool.Active.SetDoorsImmediate(false);
        pool.Active.SetDoorsLocked(false);

        var start = pool.Active.StartPoint;
        if (teleporter != null && start != null)
            teleporter.TeleportTo(start.position, start.rotation);
    }

    /// <summary>
    /// 방 안쪽 트리거(RoomEntryTrigger)에서 호출: 플레이어가 문을 지나
    /// 방 안으로 확실히 들어왔다.
    ///
    /// 등 뒤에서 문이 '쾅' 닫히고 다시 잠긴다 — 판정을 끝내야 잠금이 풀린다.
    /// (첫 방은 애초에 문이 닫힌 채로 시작하므로 이 트리거를 탈 일이 없다)
    /// </summary>
    public void OnPlayerEnteredRoom()
    {
        // 판정을 끝낸 뒤라면 지금은 '들어오는' 게 아니라 복도로 '나가는' 길이다.
        if (judged) return;

        // 문간에 몸이 걸쳐 있으면 절대 닫지 않는다 — 문짝에 밀려나기 때문.
        // (정상 흐름에선 문간을 빠져나온 뒤 호출되므로 여기 걸릴 일이 없다. 안전장치)
        if (doorwayOccupied) return;

        pool.Active.SlamDoors();
        pool.Active.SetDoorsLocked(true);
    }

    /// <summary>
    /// 문간 볼륨(RoomEntryTrigger) 점유 상태 갱신.
    /// 점유 중에는 어떤 경로로도 복도 문을 닫지 않는다 — 문짝이 플레이어를 밀어내지 않게.
    /// </summary>
    public void SetDoorwayOccupied(bool occupied) => doorwayOccupied = occupied;

    /// <summary>
    /// 추격자에게 잡혔을 때 호출. 진행도를 잃고 암전 뒤 다음 방으로 넘어간다.
    /// 죽음 화면 대신 '정신을 차려보니 또 그 방'으로 처리해, 잡혔는데 왜 살아
    /// 있는지 모르는 불안감을 남긴다.
    /// </summary>
    public void OnCaughtByStalker()
    {
        progress = 0;
        mistakes = 0;
        Debug.Log("[GameManager] 잡혔다 — 진행도 0");

        // 오답과 같은 신호를 재사용한다 — 방을 안 바꾸는 격자에서는 특히,
        // "방금 무슨 일이 있었다"를 알려줄 연출이 이것 말고 마땅치 않다.
        if (wrongAnswerFlash != null) wrongAnswerFlash.Flash();

        // ⚠️ 격자에서 잡히면 어떻게 되어야 하는지는 아직 정해지지 않았다. 3안처럼
        // 다음 방으로 넘기는 동작(AdvanceToNextRoom)은 암전+순간이동이 전제인데
        // 격자는 플레이어가 순간이동 없이 걸어다니는 구조라 그대로 못 쓴다. 지금은
        // 추격자만 물리고 제자리에 둔다 — 벌칙 연출은 나중에 따로 설계해야 한다.
        if (GridMode) { DismissStalker(); return; }

        AdvanceToNextRoom();
    }

    /// <summary>
    /// 지정 방/칸에 이상현상을 세팅한다 ('새로 꾸미기'). 3안·격자 공용 —
    /// <see cref="IAnomalyHost"/> 하나만 알면 되므로 어느 쪽이 넘어와도 같게 처리한다.
    ///
    /// 누적 오답이 기준에 닿으면 랜덤을 돌리지 않고 <b>'그것'을 이번 방의 이상현상으로
    /// 고정</b>한다. 벌칙이되 규칙 밖의 사고가 아니라, 어디까지나 관찰로 발견하고
    /// O로 판정해야 하는 이상현상 중 하나로 다룬다.
    ///
    /// ⚠️ <b>데이터만 세팅한다 — 진짜 Stalker를 등장시키지 않는다.</b> 격자에서
    /// 이 함수는 재활용 시점(<see cref="OnGridCellRecycled"/>)에도 불리는데, 그건
    /// 플레이어가 아직 안 닿은 <b>먼 칸</b>을 미리 꾸미는 것이다. 여기서
    /// <c>SetStalkerAsAnomaly(IAnomalyHost)</c>(등장까지 시키는 쪽)를 부르면 씬에
    /// 하나뿐인 Stalker가 플레이어 눈앞이 아니라 그 먼 칸으로 즉시 텔레포트해버리고,
    /// 플레이어가 실제로 다른 칸에 들어서는 순간 OnGridCenterChanged가 그 칸엔
    /// '그것' 표시가 없다며 도로 숨겨버린다 — 위치도 엉뚱하고 몸도 꺼진 채로 남는
    /// 원인이었다. <see cref="IAnomalyHost.SetStalkerAsAnomaly"/>(데이터 전용)만
    /// 불러 세팅해 두면, 실제 등장은 플레이어가 그 칸에 들어서는 순간
    /// (OnGridCenterChanged)에만 일어난다.
    /// </summary>
    private void Dress(IAnomalyHost room)
    {
        if (mistakesBeforeStalker > 0 && mistakes >= mistakesBeforeStalker)
        {
            mistakes = 0;
            room.SetStalkerAsAnomaly();
            Debug.Log($"[GameManager] {room.DisplayName} 세팅 — 이상현상: 그것 (관찰해서 O로 판정할 것)");
            return;
        }

        bool has = Random.value < anomalyChance;
        room.SetAnomaly(has);
        Debug.Log($"[GameManager] {room.DisplayName} 세팅 — 이상현상: {(has ? "있음" : "없음")}");
    }

    // ══════════════════════════════════════════════════════════════════
    // 3x3 격자 전용 경로 — GridManager 이벤트에 접붙인 판정 배선.
    // 위의 Judge()/Dress()/SetStalkerAsAnomaly()는 3안과 공유하고,
    // "언제 판정을 리셋하나 / 언제 새로 꾸미나"만 여기서 갈린다.
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 게임 시작 직후 딱 한 번. 3안의 EnterFirstRoom()과 같은 이유로 중앙 칸은
    /// 늘 '정상'으로 고정한다 — 플레이어가 무엇이 정상인지 먼저 익혀야 한다.
    ///
    /// 나머지 8칸도 여기서 한 번은 꾸며 둔다. 재활용된 적이 없는 칸은
    /// (기본값 그대로) 이상현상이 없는 상태인데, 그대로 두면 처음 옆 칸으로
    /// 넘어갔을 때 항상 '정상'으로만 보여 판정이 무의미해진다.
    /// </summary>
    private void BeginGridFirstRoom(GridCell centerCell)
    {
        if (centerCell == null)
        {
            Debug.LogError("[GameManager] GridManager가 중앙 칸을 못 만들어 격자 판정을 시작하지 못합니다.", this);
            return;
        }

        centerCell.SetAnomaly(false);
        Debug.Log($"[GameManager] {centerCell.name} 세팅 — 첫 방이라 이상현상 없음으로 고정");

        foreach (var kv in GridManager.Instance.Cells)
            if (kv.Value != null && kv.Value != centerCell) Dress(kv.Value);

        judged = false;
        mistakes = 0;
        doorwayOccupied = false;

        // 이상현상이 없는 방이어도 판정은 반드시 거쳐야 한다 — 무엇이 '정상'인지
        // 관찰해서 X로 확인하는 것 자체가 학습이다(3안 EnterFirstRoom과 같은 이유).
        GridManager.Instance.SetCellExitsLocked(centerCell, true);
    }

    /// <summary>
    /// 플레이어가 선 칸(중앙)이 바뀔 때마다. 3안의 EnterRoom() 앞부분(judged 리셋 ·
    /// DismissStalker → 다시 세우기)과 같은 역할을, 여기서는 "재활용"이 아니라
    /// "칸을 넘어 걸어 들어감" 시점에 한다.
    /// </summary>
    private void OnGridCenterChanged(GridCell cell)
    {
        judged = false;
        doorwayOccupied = false;

        // 판정 전엔 이 방의 사방 출입문이 전부 잠긴다 — 이상현상을 관찰해서
        // O/X로 답해야 다음 칸으로 나갈 수 있다. Judge()가 판정 직후 다시 푼다.
        GridManager.Instance.SetCellExitsLocked(cell, true);

        if (cell != null && cell.StalkerIsAnomaly) SetStalkerAsAnomaly(cell);
        else DismissStalker();
    }

    /// <summary>
    /// 칸 하나가 재활용으로 자리를 옮긴 직후 — 3안의 DoAdvance()가 다음 방을 Dress()로
    /// 새로 꾸미던 것과 같은 지점이다. 재활용은 언제나 플레이어가 닿기 <b>전</b>에
    /// 일어나므로(2칸 뒤에서 조용히), 다시 꾸며도 눈에 띄지 않는다.
    /// </summary>
    private void OnGridCellRecycled(GridCell cell)
    {
        if (cell != null) Dress(cell);
    }

    /// <summary>
    /// 목표 달성 순간. 여기서 화면을 띄우지는 <b>않는다</b> — 판정 직후는 아직
    /// 영안실 안이라, 끝을 알리기 전에 <b>제 발로 걸어 나가게</b> 두는 편이 낫다.
    /// 실제 클리어 화면은 복도 트리거를 지날 때 ShowClearScreen()이 띄운다.
    /// </summary>
    private void OnClear()
    {
        Debug.Log("[GameManager] 클리어! 🎉 — 복도로 나서면 끝난다");
    }
}
