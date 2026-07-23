using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 1인칭 플레이어 컨트롤러 (Unity 새 Input System 기반).
///
/// ── 역할 ──────────────────────────────────────────────
///  • WASD 이동        : CharacterController로 물리 이동 처리
///  • 마우스 시점 회전  : 몸통 = 좌우(Yaw), 카메라 = 상하(Pitch)로 분리
///  • 중력             : 바닥에 붙이고, 공중에선 아래로 가속
///
/// ── 주요 함수 ─────────────────────────────────────────
///  • Awake()               : 컴포넌트 캐싱 + 입력 액션(Move/Look)을 코드로 정의
///  • OnEnable / OnDisable() : 입력 액션 활성/비활성 (이벤트·리소스 누수 방지)
///  • Start()               : 마우스 커서 잠금
///  • Update()              : 매 프레임 '시점 → 이동' 순서로 갱신
///  • HandleLook()          : 마우스 델타로 몸통 Yaw / 카메라 Pitch 회전
///  • HandleMovement()      : 바라보는 방향 기준으로 이동 + 중력 적용
///
/// ── 설계 의도 ─────────────────────────────────────────
///  • 입력 바인딩을 코드에 내장 → 별도 .inputactions 에셋이나
///    PlayerInput 컴포넌트 세팅 없이 이 스크립트 하나로 단독 동작한다.
///  • 이동을 Rigidbody가 아닌 CharacterController로 처리 → 벽 충돌·계단 등
///    캐릭터 이동 특유의 처리를 엔진이 대신 해줘 호러식 도보 이동에 적합하다.
///  • Yaw(몸통)와 Pitch(카메라)를 분리 → 몸은 항상 수평을 유지해
///    이동 방향이 시선 상하에 영향받지 않는다(1인칭의 정석 구조).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("걷기 속도 (m/s). 호러 페이싱은 느리게.")]
    [SerializeField] private float moveSpeed = 2.5f;
    [Tooltip("중력 가속도 (m/s²). 음수.")]
    [SerializeField] private float gravity = -9.81f;

    [Header("Look")]
    [Tooltip("플레이어의 자식 카메라 Transform (눈높이에 배치).")]
    [SerializeField] private Transform cameraPivot;
    [Tooltip("마우스 감도 (마우스 픽셀 이동량당 회전 각도).")]
    [SerializeField] private float lookSensitivity = 0.1f;
    [Tooltip("위/아래로 볼 수 있는 최대 각도.")]
    [SerializeField] private float pitchClamp = 85f;

    [Header("Options")]
    [Tooltip("시작 시 마우스 커서를 화면 중앙에 잠글지 여부.")]
    [SerializeField] private bool lockCursor = true;

    // 캐싱된 컴포넌트 (GetComponent 반복 호출 방지)
    private CharacterController controller;

    // 코드로 생성하는 입력 액션 (에셋 의존 없음)
    private InputAction moveAction;
    private InputAction lookAction;

    private float pitch;              // 누적된 상하 시점 각도 (Clamp 대상)
    private float verticalVelocity;   // 중력에 의한 수직 속도

    /// <summary>
    /// 지금 이동 입력이 있는지 여부. 손 애니메이션(HandAnimator) 등
    /// 외부에서 걷기/정지 상태를 알아야 할 때 참조한다.
    /// </summary>
    public bool IsMoving { get; private set; }

    /// <summary>
    /// true면 시점·이동 조작을 잠근다 (UI가 열려 마우스를 써야 할 때).
    /// 판정 UI(JudgmentUI)가 열릴 때 켜고, 닫힐 때 끈다.
    /// </summary>
    public bool ControlsLocked { get; set; }

    /// <summary>
    /// 컴포넌트 캐싱과 입력 액션 정의를 담당한다.
    /// Move는 WASD → Vector2 합성(2DVector), Look은 마우스 델타에 바인딩한다.
    /// 입력을 여기서 코드로 만들기 때문에 인스펙터/에셋 세팅이 필요 없다.
    /// </summary>
    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // WASD 네 키를 하나의 Vector2로 합성한다.
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        // 마우스의 프레임 간 이동량(delta)을 시점 입력으로 사용한다.
        lookAction = new InputAction("Look", InputActionType.Value, binding: "<Mouse>/delta");
    }

    /// <summary>오브젝트 활성화 시 입력 액션을 켠다. (끄지 않으면 콜백이 계속 살아있음)</summary>
    private void OnEnable()
    {
        moveAction.Enable();
        lookAction.Enable();
    }

    /// <summary>비활성화 시 입력 액션을 꺼서 리소스·이벤트 누수를 막는다.</summary>
    private void OnDisable()
    {
        moveAction.Disable();
        lookAction.Disable();
    }

    /// <summary>게임 시작 시 마우스 커서를 화면 중앙에 잠그고 숨긴다(1인칭 조작감).</summary>
    private void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>
    /// 매 프레임 입력을 반영한다.
    /// 시점(Look)을 먼저 처리해 이동 방향의 기준이 되는 몸통 각도를 갱신한 뒤,
    /// 그 방향을 기준으로 이동(Movement)을 처리한다.
    /// </summary>
    private void Update()
    {
        // UI가 열려 있는 동안은 시점·이동을 멈춘다 (마우스를 UI에 양보).
        if (ControlsLocked)
        {
            IsMoving = false;   // 손 애니메이션도 Idle로
            return;
        }

        HandleLook();
        HandleMovement();
    }

    /// <summary>
    /// 마우스 델타를 받아 좌우(Yaw)는 몸통 전체를, 상하(Pitch)는 카메라만 회전시킨다.
    /// Pitch는 일정 각도로 Clamp해 고개가 뒤로 넘어가는 것을 방지한다.
    /// </summary>
    private void HandleLook()
    {
        // 마우스 델타는 이미 '프레임 간 이동량'이라 Time.deltaTime을 곱하지 않는다.
        Vector2 look = lookAction.ReadValue<Vector2>() * lookSensitivity;

        // Yaw: 몸통 전체를 좌우로 회전 → 이동 방향도 함께 돈다.
        transform.Rotate(Vector3.up, look.x);

        // Pitch: 카메라만 상하로 기울이고, 뒤집히지 않게 각도를 제한한다.
        pitch = Mathf.Clamp(pitch - look.y, -pitchClamp, pitchClamp);
        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>
    /// 바라보는 방향(몸통 기준)으로 수평 이동시키고, 중력을 더해 CharacterController로 이동한다.
    /// 대각선 이동이 빨라지지 않도록 방향 벡터의 크기를 1로 제한한 뒤 속도를 곱한다.
    /// </summary>
    private void HandleMovement()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();

        // 입력이 있으면 이동 중으로 표시(손 애니메이션 전환 등에 사용).
        IsMoving = input.sqrMagnitude > 0.01f;

        // 몸통이 바라보는 방향(right/forward) 기준으로 이동 벡터를 만든다.
        Vector3 horizontal = transform.right * input.x + transform.forward * input.y;
        // 대각선(예: W+D)일 때 √2배 빨라지는 것을 막고, 속도를 곱한다.
        horizontal = Vector3.ClampMagnitude(horizontal, 1f) * moveSpeed;

        // 바닥에 붙어있으면 살짝 눌러줘서 isGrounded를 안정화(0이면 경사·틈에서 떨림).
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        // 공중에선 중력으로 계속 아래 가속.
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal;
        velocity.y = verticalVelocity;

        // 속도(m/s) × 이번 프레임 시간 = 이번 프레임 이동량.
        controller.Move(velocity * Time.deltaTime);
    }
}
