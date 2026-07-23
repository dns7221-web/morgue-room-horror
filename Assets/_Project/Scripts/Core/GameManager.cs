using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 전체 진행을 지휘하는 두뇌(중앙 매니저).
///
/// ── 담당 ──────────────────────────────────────────────
///  • 진행도 관리 (0 ~ clearGoal, 정답 +1 / 오답 0)
///  • 판정 처리   (플레이어 O/X vs 실제 이상현상 유무)
///  • 루프 진행   (판정 → 복도 문 열기 → 트리거에서 텔레포트 + 새 방 세팅)
///  • 목표 도달 시 클리어
///
/// 다른 스크립트(판정 UI, 복도 트리거)가 쉽게 접근하도록 싱글톤으로 둔다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("플레이어 순간이동 담당.")]
    [SerializeField] private PlayerTeleporter teleporter;
    [Tooltip("코드로 여닫는 복도 문(들). 이중문이면 문짝 2개 모두 등록 (Is Locked 체크된 것).")]
    [SerializeField] private Door[] hallwayDoors;
    [Tooltip("이상현상 관리자. 방 세팅 시 랜덤 이상현상을 켜고 끈다.")]
    [SerializeField] private AnomalyManager anomalyManager;

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
    private bool currentRoomHasAnomaly;
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
        SetupRoom();   // 첫 방 준비
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

        bool correct = (playerSaysAnomaly == currentRoomHasAnomaly);
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

        // 정답/오답과 무관하게 복도 문(들)을 열어 다음 루프로 나가게 한다.
        foreach (var door in hallwayDoors)
            if (door != null) door.Open();
    }

    /// <summary>복도 중간 트리거에서 호출: 순간이동 + 다음 방 세팅.</summary>
    public void AdvanceToNextRoom()
    {
        if (teleporter != null) teleporter.TeleportToStart();
        foreach (var door in hallwayDoors)
            if (door != null) door.Close();
        SetupRoom();
    }

    /// <summary>새 방 준비: 이상현상 유무 랜덤 결정 + 판정 상태 초기화.</summary>
    private void SetupRoom()
    {
        currentRoomHasAnomaly = Random.value < anomalyChance;
        judged = false;
        if (anomalyManager != null) anomalyManager.SetAnomaly(currentRoomHasAnomaly);
        Debug.Log($"[GameManager] 새 방 준비 — 이상현상: {(currentRoomHasAnomaly ? "있음" : "없음")}");
    }

    private void OnClear()
    {
        Debug.Log("[GameManager] 클리어! 🎉");
        // 나중: 클리어 연출/엔딩 화면
    }
}
