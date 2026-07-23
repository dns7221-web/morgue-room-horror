using TMPro;
using UnityEngine;

/// <summary>
/// 조준 중인 대상의 안내 문구를 화면에 표시하는 UI.
/// PlayerInteractor.CurrentPrompt 값을 매 프레임 읽어 텍스트에 반영한다.
///  예: 문 조준 → "문 열기 (E)" / 잠긴 문 → "잠겨 있다" / 키패드 → "판정하기 (E)"
///
/// 데이터(문자열)는 각 IInteractable이 제공하고, 표시는 여기서만 담당 (관심사 분리).
/// 배치: Canvas 아래 TextMeshProUGUI 오브젝트에 붙인다.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    [Tooltip("프롬프트를 읽어올 플레이어의 PlayerInteractor.")]
    [SerializeField] private PlayerInteractor interactor;
    [Tooltip("문구를 표시할 텍스트. 비우면 같은 오브젝트에서 자동으로 찾는다.")]
    [SerializeField] private TextMeshProUGUI label;

    private void Awake()
    {
        if (label == null)
            label = GetComponent<TextMeshProUGUI>();
    }

    private void Update()
    {
        if (interactor == null || label == null) return;

        // 판정 UI가 열려 있는 동안엔 프롬프트를 숨긴다 (겹침 방지).
        bool uiOpen = JudgmentUI.Instance != null && JudgmentUI.Instance.IsOpen;
        string prompt = uiOpen ? null : interactor.CurrentPrompt;

        label.text = prompt ?? string.Empty;
    }
}
