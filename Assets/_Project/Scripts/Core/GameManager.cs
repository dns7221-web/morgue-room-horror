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

    /// <summary>
    /// 플레이어가 지금 안전한지 — <b>안전구역(키패드 방) 안 + 영안실 쪽 이중문이 닫힘</b>.
    /// 뛰어들어오는 것만으로는 부족하고, 돌아서서 문을 닫아야 성립한다.
    /// </summary>
    public bool PlayerIsSheltered
    {
        get
        {
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
    /// <summary>현재 방에 이상현상이 있는지 (디버그·강제 판정용).</summary>
    public bool CurrentRoomHasAnomaly => pool != null && pool.Active != null && pool.Active.HasAnomaly;
    /// <summary>현재 활성 모듈 (디버그 표시용).</summary>
    public RoomModule CurrentRoom => pool != null ? pool.Active : null;

    /// <summary>씬 단일 추격자 (디버그 표시용).</summary>
    public Stalker Stalker => stalker;

    /// <summary>[디버그] 이번 방의 이상현상을 즉시 '그것'으로 바꾼다.</summary>
    public void DebugMakeStalkerAnomaly()
    {
        if (pool == null || pool.Active == null) return;
        SetStalkerAsAnomaly(pool.Active);
        judged = false;
        Debug.Log("[Debug] 이번 방 이상현상을 '그것'으로 교체");
    }

    /// <summary>
    /// 이 방을 '그것' 방으로 세팅하고, 씬 단일 추격자를 그 방의 스폰 지점에 등장시킨다.
    /// 방 데이터 세팅과 물리적 등장을 한 곳에서 묶어, 한쪽만 호출해 생기는
    /// "정답은 있음인데 화면엔 아무것도 없는" 사고를 막는다.
    /// </summary>
    private void SetStalkerAsAnomaly(RoomModule module)
    {
        module.SetStalkerAsAnomaly();

        if (stalker == null)
        {
            Debug.LogWarning("[GameManager] Stalker가 연결돼 있지 않아 '있음'인데 아무것도 나타나지 않습니다.", this);
            return;
        }
        stalker.Appear(module.StalkerSpawnPoint);
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
    }

    private void Start()
    {
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
        judged = true;

        bool correct = (playerSaysAnomaly == pool.Active.HasAnomaly);
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

        // 정답/오답과 무관하게 복도 문(들)의 잠금을 푼다.
        // 자동으로 열어주지는 않는다 — 나가는 문은 플레이어가 직접 E로 연다.
        pool.Active.SetDoorsLocked(false);
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
        AdvanceToNextRoom();
    }

    /// <summary>
    /// 지정 모듈에 이상현상을 세팅한다 (방 '새로 꾸미기').
    ///
    /// 누적 오답이 기준에 닿으면 랜덤을 돌리지 않고 <b>'그것'을 이번 방의 이상현상으로
    /// 고정</b>한다. 벌칙이되 규칙 밖의 사고가 아니라, 어디까지나 관찰로 발견하고
    /// O로 판정해야 하는 이상현상 중 하나로 다룬다.
    /// </summary>
    private void Dress(RoomModule module)
    {
        if (mistakesBeforeStalker > 0 && mistakes >= mistakesBeforeStalker)
        {
            mistakes = 0;
            SetStalkerAsAnomaly(module);
            Debug.Log($"[GameManager] {module.name} 세팅 — 이상현상: 그것 (관찰해서 O로 판정할 것)");
            return;
        }

        bool has = Random.value < anomalyChance;
        module.SetAnomaly(has);
        Debug.Log($"[GameManager] {module.name} 세팅 — 이상현상: {(has ? "있음" : "없음")}");
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
