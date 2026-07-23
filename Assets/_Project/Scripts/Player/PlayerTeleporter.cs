using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어를 지정 지점으로 순간이동시킨다. (공간 반복 트릭의 핵심)
///
/// ── 핵심(함정) ─────────────────────────────────────────
/// CharacterController는 자기 위치를 스스로 관리하기 때문에, 켜진 상태로
/// transform.position을 바꾸면 무시되거나 충돌로 튕길 수 있다.
/// → 순간이동 순간에만 잠깐 끄고(position 변경) 다시 켜는 것이 안전하다.
///
/// 배치: 플레이어(Player) 오브젝트.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerTeleporter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("순간이동 도착 지점(시작 지점). 빈 오브젝트를 방 입구에 두고 연결.")]
    [SerializeField] private Transform startPoint;

    [Header("Debug")]
    [Tooltip("체크 시 T키로 순간이동 테스트 (나중에 GameManager가 호출하면 끄면 됨).")]
    [SerializeField] private bool enableTestKey = true;

    private CharacterController controller;
    private InputAction testAction;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (enableTestKey)
            testAction = new InputAction("TeleportTest", InputActionType.Button, "<Keyboard>/t");
    }

    private void OnEnable() => testAction?.Enable();
    private void OnDisable() => testAction?.Disable();

    private void Update()
    {
        // 임시 테스트: T키로 순간이동 확인.
        if (enableTestKey && testAction.WasPressedThisFrame())
            TeleportToStart();
    }

    /// <summary>플레이어를 시작 지점(startPoint)으로 순간이동시킨다. 외부(GameManager 등)에서 호출.</summary>
    public void TeleportToStart()
    {
        if (startPoint == null)
        {
            Debug.LogWarning("[PlayerTeleporter] startPoint가 지정되지 않았습니다.");
            return;
        }
        TeleportTo(startPoint.position, startPoint.rotation);
    }

    /// <summary>지정한 위치·회전으로 순간이동. CharacterController를 잠깐 껐다 켜서 안전하게 이동.</summary>
    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;                        // ① 잠깐 끄기
        transform.SetPositionAndRotation(position, rotation); // ② 위치·회전 변경
        controller.enabled = true;                         // ③ 다시 켜기
    }
}
