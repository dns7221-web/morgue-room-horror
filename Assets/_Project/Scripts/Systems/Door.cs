using System.Collections;
using UnityEngine;

/// <summary>
/// 경첩식으로 부드럽게 "한 번" 열리는 문. <see cref="IInteractable"/>을 구현해
/// PlayerInteractor가 열 수 있다. (8번 출구식 관문 — 열면 되돌아가지 않음)
///
/// 배치: 문짝 오브젝트(회전 기준점=경첩)에 붙인다. 이중문이면 각 문짝에 하나씩
/// 붙이고 <see cref="openAngle"/>을 서로 반대(예: 90 / -90)로 준다.
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
///  • Coroutine + Slerp : 각도를 시간에 걸쳐 보간 → 뚝 열리지 않고 부드럽게.
///  • 상태(isOpen)      : 이미 열렸으면 재실행/안내를 막는다.
///  • Rigidbody 미사용   : 예측 가능한 스크립트 회전(관찰 게임에 적합).
/// </summary>
public class Door : MonoBehaviour, IInteractable
{
    [Header("Open Motion")]
    [Tooltip("열릴 각도. 반대로 열리게 하려면 음수. (이중문은 좌우를 +/-로)")]
    [SerializeField] private float openAngle = 90f;
    [Tooltip("여는 데 걸리는 시간(초).")]
    [SerializeField] private float openDuration = 1f;
    [Tooltip("회전 축(경첩). 보통 위쪽(Y).")]
    [SerializeField] private Vector3 hingeAxis = Vector3.up;

    [Header("UI")]
    [SerializeField] private string openPrompt = "문 열기 (E)";

    // 이미 열렸으면 안내를 숨긴다.
    public string Prompt => isOpen ? null : openPrompt;

    private bool isOpen;
    private Quaternion closedRotation;

    private void Awake()
    {
        // 닫힌 상태의 회전을 기준으로 저장.
        closedRotation = transform.localRotation;
    }

    /// <summary>PlayerInteractor가 호출. 아직 안 열렸을 때만 여는 동작을 시작한다.</summary>
    public void Interact()
    {
        if (isOpen) return;              // 한 번 열리면 끝
        isOpen = true;
        StopAllCoroutines();
        StartCoroutine(OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        Quaternion target = closedRotation * Quaternion.AngleAxis(openAngle, hingeAxis);
        float t = 0f;
        while (t < openDuration)
        {
            t += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(closedRotation, target, t / openDuration);
            yield return null;           // 다음 프레임까지 대기
        }
        transform.localRotation = target; // 오차 없이 정확히 목표 각도로 마무리
    }
}
