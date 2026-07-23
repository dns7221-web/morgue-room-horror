using UnityEngine;

/// <summary>
/// 1인칭 손 애니메이션 구동기.
/// 플레이어의 이동 여부(FirstPersonController.IsMoving)를 읽어,
/// 자식으로 달린 모든 손 Animator의 "IsMoving" 파라미터에 전달한다.
/// → Animator가 Idle ↔ Walk 상태를 알아서 전환한다.
///
/// 배치 위치: 두 손(L/R)을 묶은 부모 오브젝트(Hand)에 붙인다.
///
/// ── 설계 의도 ──────────────────────────────────────────
///  • 이동 '판단'은 FirstPersonController가, 애니메이션 '반영'은 여기서 →
///    이동 로직과 표현(연출)을 분리(관심사 분리)한다.
///  • 손이 L/R 두 개의 개별 Animator라도 GetComponentsInChildren로 한 번에 처리.
///  • 파라미터 이름은 StringToHash로 캐싱해 매 프레임 문자열 비교 비용을 없앤다.
/// </summary>
[DisallowMultipleComponent]
public class HandAnimator : MonoBehaviour
{
    [Tooltip("이동 여부를 읽어올 플레이어 컨트롤러. 비워두면 부모/씬에서 자동으로 찾는다.")]
    [SerializeField] private FirstPersonController controller;

    // "IsMoving" 파라미터의 해시(문자열보다 빠름).
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    // 자식으로 달린 손 Animator들(L/R).
    private Animator[] animators;

    private void Awake()
    {
        animators = GetComponentsInChildren<Animator>();

        // 참조가 비어 있으면 부모 계층 → 씬 순으로 자동 탐색.
        if (controller == null)
            controller = GetComponentInParent<FirstPersonController>();
        if (controller == null)
            controller = FindAnyObjectByType<FirstPersonController>();
    }

    private void Update()
    {
        if (controller == null || animators == null)
            return;

        bool moving = controller.IsMoving;
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
                animators[i].SetBool(IsMovingHash, moving);
        }
    }
}
