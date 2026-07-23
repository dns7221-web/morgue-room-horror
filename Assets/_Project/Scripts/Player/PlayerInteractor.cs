using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어의 상호작용 담당.
/// 카메라 정면으로 Raycast를 쏴 <see cref="IInteractable"/>을 찾고,
/// 상호작용 키(E)를 누르면 그 대상의 Interact()를 호출한다.
///
/// 대상이 문인지 서랍인지는 신경 쓰지 않는다(인터페이스로 추상화).
/// 배치 위치: 플레이어(Player) 오브젝트.
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
///  • Raycast + LayerMask : "보고 있는 것"만 사거리 내에서 감지.
///  • 인터페이스 호출      : 구체 타입을 몰라도 Interact() 하나로 처리(확장 용이).
///  • 관심사 분리          : 여기는 '감지/입력'만, 실제 여는 로직은 Door가 담당.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Raycast 기준 카메라. 비우면 Camera.main 사용.")]
    [SerializeField] private Camera playerCamera;

    [Header("Interaction")]
    [Tooltip("상호작용 가능한 최대 거리(m).")]
    [SerializeField] private float interactRange = 3f;
    [Tooltip("이 레이어의 물체만 감지. 기본은 전부.")]
    [SerializeField] private LayerMask interactableLayer = ~0;

    private InputAction interactAction;
    private IInteractable current;   // 지금 조준 중인 대상 (없으면 null)

    /// <summary>지금 조준 중인 대상의 안내 문구(UI용). 대상이 없으면 null.</summary>
    public string CurrentPrompt => current?.Prompt;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        // 상호작용 입력(E)을 코드로 정의 — FirstPersonController와 동일한 방식.
        interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
    }

    private void OnEnable() => interactAction.Enable();
    private void OnDisable() => interactAction.Disable();

    private void Update()
    {
        // 매 프레임 조준 대상 갱신.
        current = FindInteractable();

        // 대상이 있고 이번 프레임에 E를 눌렀다면 상호작용.
        if (current != null && interactAction.WasPressedThisFrame())
            current.Interact();
    }

    /// <summary>카메라 정면으로 Raycast를 쏴 사거리 내 IInteractable을 반환(없으면 null).</summary>
    private IInteractable FindInteractable()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            // 콜라이더가 자식(문짝)에 있고 Door는 부모에 있을 수 있으므로 InParent로 탐색.
            return hit.collider.GetComponentInParent<IInteractable>();
        }
        return null;
    }
}
