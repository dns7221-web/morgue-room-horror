using UnityEngine;

/// <summary>
/// 이상현상 판정 UI (O: 있음 / X: 없음).
///
/// ── 흐름 ──────────────────────────────────────────────
///  키패드(JudgmentPanel)에서 Show() 호출
///   → 패널 표시 + 커서 잠금 해제 + 플레이어 조작 잠금 (1인칭 ↔ UI 모드 전환)
///  버튼 클릭(OnClickAnomaly / OnClickNormal)
///   → GameManager.Judge(답) 전달 → Hide() → 1인칭 모드 복귀
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
///  • uGUI Button의 onClick 이벤트(이벤트 기반)로 판정 입력을 받는다.
///  • 커서 잠금/해제 + 조작 잠금을 한 곳에서 일관되게 전환 (상태 전환 관리).
///  • UI는 '입력 수집'만 하고 판정 로직은 GameManager에 위임 (관심사 분리).
///
/// 배치: Canvas 오브젝트에 붙이고, 판정 패널(panel)과 플레이어 컨트롤러를 연결.
/// 버튼 2개의 OnClick()에 각각 OnClickAnomaly / OnClickNormal을 연결한다.
/// </summary>
public class JudgmentUI : MonoBehaviour
{
    public static JudgmentUI Instance { get; private set; }

    [Header("References")]
    [Tooltip("O/X 버튼이 들어 있는 판정 패널 (평소 비활성).")]
    [SerializeField] private GameObject panel;
    [Tooltip("플레이어 1인칭 컨트롤러 (UI 열릴 때 조작을 잠근다).")]
    [SerializeField] private FirstPersonController playerController;

    /// <summary>판정 패널이 열려 있는지.</summary>
    public bool IsOpen => panel != null && panel.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (panel != null) panel.SetActive(false);   // 시작은 닫힌 상태
    }

    /// <summary>판정 UI 열기 — UI 모드로 전환 (커서 표시, 조작 잠금).</summary>
    public void Show()
    {
        if (IsOpen) return;
        panel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (playerController != null) playerController.ControlsLocked = true;
    }

    /// <summary>판정 UI 닫기 — 1인칭 모드로 복귀 (커서 잠금, 조작 재개).</summary>
    public void Hide()
    {
        if (!IsOpen) return;
        panel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (playerController != null) playerController.ControlsLocked = false;
    }

    /// <summary>[O 버튼] 이상현상이 '있다'고 판정. (버튼 OnClick에 연결)</summary>
    public void OnClickAnomaly()
    {
        if (GameManager.Instance != null) GameManager.Instance.Judge(true);
        Hide();
    }

    /// <summary>[X 버튼] 이상현상이 '없다'고 판정. (버튼 OnClick에 연결)</summary>
    public void OnClickNormal()
    {
        if (GameManager.Instance != null) GameManager.Instance.Judge(false);
        Hide();
    }
}
