using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 대쉬 스태미너 막대 — <b>표시만</b> 한다. 값은 <see cref="FirstPersonController"/>가 들고 있다
/// (ProgressUI·InteractionPromptUI와 같은 철학 — 계산과 표현을 가른다).
///
/// ── 유니티 내장 기능으로 그린다 ───────────────────────
/// 막대를 직접 그리지 않는다. uGUI <see cref="Image"/>의 <c>Image.Type = Filled</c>를 쓰고
/// <see cref="Image.fillAmount"/>(0~1)에 값만 넣는다 — 스프라이트 한 장이면 끝이고
/// 가로/세로/원형도 인스펙터에서 바뀐다. 커스텀 메시나 셰이더가 필요 없다.
///
/// ── 가득 차 있으면 숨는다 ─────────────────────────────
/// 이 포폴의 UI 원칙은 <b>화면 구석 HUD를 피하는 것</b>이다(진행도는 키패드에 박힌
/// 월드 UI다). 스태미너는 플레이어 몸 상태라 세계 안에 둘 자리가 없어 어쩔 수 없이
/// 화면에 두지만, <b>가득 찼을 때는 사라지게</b> 해서 평소 화면을 비워 둔다 —
/// 쓸 때만 나타나니 "지금 자원을 쓰고 있다"는 신호도 겸한다.
///
/// 배치: Screen Space Canvas 아래에 Image 하나(Type: Filled) + 이 스크립트 + CanvasGroup.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class StaminaUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("값을 읽어올 플레이어. 비우면 씬에서 한 번 찾는다.")]
    [SerializeField] private FirstPersonController player;
    [Tooltip("채워질 막대. Image Type을 <b>Filled</b>로 둘 것. 비우면 자식에서 찾는다.")]
    [SerializeField] private Image fill;

    [Header("색")]
    [Tooltip("평소 색.")]
    [SerializeField] private Color normalColor = new Color(0.85f, 0.87f, 0.9f, 0.75f);
    [Tooltip("바닥나서 대쉬를 못 쓰는 동안의 색. 왜 안 달려지는지 색으로 알려준다.")]
    [SerializeField] private Color exhaustedColor = new Color(0.9f, 0.3f, 0.3f, 0.85f);

    [Header("나타남 / 사라짐")]
    [Tooltip("이 비율 이상 차 있고 대쉬도 안 하면 숨는다. 1이면 가득 찼을 때만 숨는다.")]
    [Range(0.5f, 1f)][SerializeField] private float hideAbove = 0.999f;
    [Tooltip("나타나고 사라지는 데 걸리는 시간(초). 툭 튀어나오면 눈에 거슬린다.")]
    [Min(0f)][SerializeField] private float fadeDuration = 0.25f;

    private CanvasGroup group;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        // 막대는 보여주기만 하므로 클릭을 먹지 않게 한다 — 판정 UI 버튼을 가리면 안 된다.
        group.blocksRaycasts = false;
        group.interactable = false;

        if (fill == null) fill = GetComponentInChildren<Image>(true);
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();

        if (fill == null)
            Debug.LogWarning("[StaminaUI] 채울 Image를 찾지 못했습니다 — 막대가 안 보입니다.", this);
        if (player == null)
            Debug.LogWarning("[StaminaUI] 씬에서 FirstPersonController를 찾지 못했습니다.", this);
    }

    private void Update()
    {
        if (player == null || fill == null) return;

        float value = player.StaminaNormalized;
        fill.fillAmount = value;
        fill.color = player.StaminaExhausted ? exhaustedColor : normalColor;

        // 가득 찼고 쓰지도 않으면 숨긴다. 바닥난 상태는 가득일 리 없으니 자연히 보인다.
        bool show = player.IsDashing || value < hideAbove;
        float target = show ? 1f : 0f;

        group.alpha = fadeDuration > 0f
            ? Mathf.MoveTowards(group.alpha, target, Time.deltaTime / fadeDuration)
            : target;
    }
}
