using TMPro;
using UnityEngine;

/// <summary>
/// 진행도 표시 UI. 키패드 위(월드 스페이스)에 붙어 현재 연속 성공 횟수를 보여준다.
///  예: "3 / 8" — 8번 출구식으로 정답 +1 / 오답 0 리셋, 목표 도달 시 클리어 문구.
///
/// GameManager가 관리하는 진행도(Progress/ClearGoal)를 매 프레임 읽어 텍스트에 반영한다.
/// 값 계산·판정은 GameManager가 하고, 여기는 '표시'만 담당한다 (관심사 분리 —
/// InteractionPromptUI와 동일한 철학).
///
/// 배치: 키패드 자식의 월드 스페이스 Canvas 아래 TextMeshProUGUI 오브젝트.
/// (Screen Space로 바꿔도 이 스크립트는 그대로 동작 — 렌더 모드에 의존하지 않음)
/// </summary>
public class ProgressUI : MonoBehaviour
{
    [Tooltip("진행도를 표시할 텍스트. 비우면 같은 오브젝트에서 자동으로 찾는다.")]
    [SerializeField] private TextMeshProUGUI label;

    [Header("Format")]
    [Tooltip("진행도 표시 형식. {0}=현재 진행도, {1}=목표 횟수.")]
    [SerializeField] private string format = "{0} / {1}";
    [Tooltip("클리어 시 표시할 문구.")]
    [SerializeField] private string clearedText = "CLEAR";

    private void Awake()
    {
        if (label == null)
            label = GetComponent<TextMeshProUGUI>();
    }

    private void Update()
    {
        if (label == null || GameManager.Instance == null) return;

        var gm = GameManager.Instance;
        label.text = gm.IsCleared
            ? clearedText
            : string.Format(format, gm.Progress, gm.ClearGoal);
    }
}
