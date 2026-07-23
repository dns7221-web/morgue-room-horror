using UnityEngine;

/// <summary>
/// 키패드(계기판)에 붙는 상호작용 컴포넌트.
/// 플레이어가 바라보고 E를 누르면 판정 UI(JudgmentUI)를 연다.
///
/// IInteractable 구현 → 문(Door)과 똑같은 방식으로 PlayerInteractor가 처리한다.
/// (상호작용 시스템의 확장성 증명: 새 물체 추가에 플레이어 코드 수정 없음)
///
/// 배치: 키패드 오브젝트(Collider 필요, Interactable 레이어).
/// </summary>
public class JudgmentPanel : MonoBehaviour, IInteractable
{
    [Header("UI")]
    [SerializeField] private string prompt = "판정하기 (E)";
    [SerializeField] private string judgedPrompt = "판정 완료";

    public string Prompt
    {
        get
        {
            // 이번 방을 이미 판정했으면 다른 안내 (중복 판정 방지).
            if (GameManager.Instance != null && GameManager.Instance.HasJudged)
                return judgedPrompt;
            return prompt;
        }
    }

    public void Interact()
    {
        // 이미 판정한 방이면 UI를 열지 않는다.
        if (GameManager.Instance != null && GameManager.Instance.HasJudged) return;

        if (JudgmentUI.Instance != null)
            JudgmentUI.Instance.Show();
    }
}
