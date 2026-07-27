using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 전체 진행을 지휘하는 두뇌(중앙 매니저).
///
/// ── 담당 ──────────────────────────────────────────────
///  • 진행도 관리 (0 ~ clearGoal, 정답 +1 / 오답 0)
///  • 판정 처리   (플레이어 O/X vs 실제 이상현상 유무)
///  • 루프 진행   (판정 → 문 열림 → 복도 통과 → 반대편 방 도착)
///  • 목표 도달 시 클리어
///
/// ── 공간 반복 ─────────────────────────────────────────
///   [방A] ── 복도A ──╫── 복도B ── [방B]
/// 방은 실물 2개뿐이고 서로 등지고 이어붙어 있다. 플레이어는 순간이동도
/// 암전도 없이 <b>실제로 걸어서</b> 두 방을 오간다. 무한한 것처럼 느껴지는 건,
/// 플레이어가 한쪽 방에 갇힌 사이 <b>반대편 방을 몰래 새로 꾸미기</b> 때문이다
/// (Redress). 눈에 보이는 순간엔 절대 건드리지 않는 게 규칙.
///
/// 다른 스크립트(판정 UI, 복도 트리거)가 쉽게 접근하도록 싱글톤으로 둔다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("방 모듈 체인. 모듈 2개를 소켓끼리 맞물려 조립한다 (Current가 곧 현재 방).")]
    [SerializeField] private RoomModulePool pool;
    [Tooltip("게임 시작 시 플레이어를 첫 방 시작 지점에 세우는 데만 사용 (루프 중엔 안 씀).")]
    [SerializeField] private PlayerTeleporter teleporter;

    [Header("Rules")]
    [Tooltip("이 횟수만큼 연속 성공하면 클리어.")]
    [SerializeField] private int clearGoal = 8;
    [Tooltip("방에 이상현상이 나타날 확률 (0~1).")]
    [SerializeField, Range(0f, 1f)] private float anomalyChance = 0.5f;

    [Header("Debug")]
    [Tooltip("체크 시 O(있음)/X(없음) 키로 판정 테스트. 판정 UI 만들면 끄면 됨.")]
    [SerializeField] private bool enableTestKeys = true;

    // ── 상태 ──
    private int progress;
    private bool judged;   // 이번 방을 이미 판정했는지 (중복 방지)

    public int Progress => progress;
    /// <summary>클리어에 필요한 연속 성공 횟수 (진행도 UI가 "n / 목표" 표시에 사용).</summary>
    public int ClearGoal => clearGoal;
    public bool IsCleared => progress >= clearGoal;
    /// <summary>이번 방을 이미 판정했는지 (키패드가 중복 판정을 막는 데 사용).</summary>
    public bool HasJudged => judged;

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
        pool.Initialize();          // 모듈 2개 생성 + 소켓 접합

        Dress(pool.Current);        // 시작 방
        Dress(pool.Far);            // 반대편 방도 미리 꾸며둔다 (복도 너머로 보이므로)

        pool.Current.CloseDoors();  // 시작 방은 잠근 채로 — 판정해야 열린다
        judged = false;

        // 플레이어를 첫 방 시작 지점에 세운다 (여기서만 순간이동을 쓴다).
        var start = pool.Current.StartPoint;
        if (teleporter != null && start != null)
            teleporter.TeleportTo(start.position, start.rotation);
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

        bool correct = (playerSaysAnomaly == pool.Current.HasAnomaly);
        if (correct)
        {
            progress++;
            Debug.Log($"[GameManager] 정답! 진행도 {progress}/{clearGoal}");
            if (IsCleared) { OnClear(); return; }
        }
        else
        {
            progress = 0;
            Debug.Log("[GameManager] 오답! 진행도 0으로 초기화");
        }

        // 정답/오답과 무관하게 문을 열어 다음 루프로 나가게 한다.
        pool.Current.OpenDoors();
    }

    /// <summary>
    /// 복도 트리거(SeamTrigger)에서 호출: 플레이어가 이음새를 넘어 반대편 모듈로 넘어갔다.
    /// 같은 모듈을 다시 알려오는 경우(이음새 위 왕복 등)는 무시된다.
    /// </summary>
    public void OnPlayerEnteredModule(RoomModule module)
    {
        if (!pool.SetCurrent(module)) return;   // 이미 그 모듈이면 아무 일도 없음

        judged = false;                          // 새 방이니 다시 판정 가능
        Debug.Log($"[GameManager] 이음새 통과 — 이상현상: {(pool.Current.HasAnomaly ? "있음" : "없음")}");
    }

    /// <summary>
    /// 방 안쪽 트리거(RoomEntryTrigger)에서 호출: 플레이어가 문을 지나 방 안으로 들어왔다.
    ///
    /// 여기서 두 가지를 한다.
    ///  ① 문을 닫는다 — 판정해야 다시 열린다 (긴장감).
    ///  ② <b>반대편 방을 새로 꾸민다</b> — 플레이어가 문 닫힌 방 안에 있는
    ///     지금이 반대편을 건드릴 수 있는 유일하게 안전한 타이밍이다.
    ///     문도 미리 열어둬야 나중에 도착했을 때 걸어 들어갈 수 있다.
    /// </summary>
    public void OnPlayerEnteredRoom()
    {
        pool.Current.CloseDoors();

        Dress(pool.Far);
        pool.Far.OpenDoors();
    }

    /// <summary>지정 모듈에 이상현상 유무를 랜덤 세팅한다 (방 '새로 꾸미기').</summary>
    private void Dress(RoomModule module)
    {
        bool has = Random.value < anomalyChance;
        module.SetAnomaly(has);
        Debug.Log($"[GameManager] {module.name} 세팅 — 이상현상: {(has ? "있음" : "없음")}");
    }

    private void OnClear()
    {
        Debug.Log("[GameManager] 클리어! 🎉");
        // 나중: 클리어 연출/엔딩 화면
    }
}
