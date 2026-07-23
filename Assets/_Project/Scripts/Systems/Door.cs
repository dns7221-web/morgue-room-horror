using System.Collections;
using UnityEngine;

/// <summary>
/// 경첩식으로 부드럽게 여닫히는 문. <see cref="IInteractable"/>을 구현한다.
///
/// 두 가지 용도를 한 스크립트로 커버한다:
///  • 플레이어가 직접 여는 문(영안실 문) — Interact()로 열고 닫기(토글)
///  • 코드가 제어하는 문(복도 문)        — 평소 isLocked=true라 못 열고,
///    GameManager가 Open()/Close()로 직접 여닫는다.
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
///  • Open/Close/SetLocked를 public API로 노출 → 외부(GameManager)가 제어.
///  • Interact()는 잠금 상태를 존중하지만, Open/Close는 코드 전용이라 잠금을 무시.
///  • Coroutine + Slerp로 현재 각도에서 목표 각도까지 부드럽게 보간(중간에 방향 바뀌어도 자연스러움).
/// </summary>
public class Door : MonoBehaviour, IInteractable
{
    [Header("Open Motion")]
    [Tooltip("열릴 각도. 반대로 열리게 하려면 음수. (이중문은 좌우를 +/-로)")]
    [SerializeField] private float openAngle = 90f;
    [Tooltip("여닫는 데 걸리는 시간(초).")]
    [SerializeField] private float openDuration = 1f;
    [Tooltip("회전 축(경첩). 보통 위쪽(Y).")]
    [SerializeField] private Vector3 hingeAxis = Vector3.up;

    [Header("State")]
    [Tooltip("잠기면 플레이어가 상호작용으로 열 수 없다. 코드(GameManager)로만 여닫는 문에 사용.")]
    [SerializeField] private bool isLocked = false;

    [Header("UI")]
    [SerializeField] private string openPrompt = "문 열기 (E)";
    [SerializeField] private string lockedPrompt = "잠겨 있다";

    public string Prompt
    {
        get
        {
            if (isOpen) return null;                 // 이미 열렸으면 안내 없음
            return isLocked ? lockedPrompt : openPrompt;
        }
    }

    /// <summary>문이 현재 열려 있는지.</summary>
    public bool IsOpen => isOpen;

    private bool isOpen;
    private Quaternion closedRotation;
    private Coroutine motion;

    private void Awake()
    {
        // 닫힌 상태의 회전을 기준으로 저장.
        closedRotation = transform.localRotation;
    }

    /// <summary>플레이어 상호작용. 잠겨 있으면 무시, 아니면 열고/닫기 토글.</summary>
    public void Interact()
    {
        if (isLocked) return;
        if (isOpen) Close();
        else Open();
    }

    /// <summary>문을 연다. (코드에서도 호출 — 잠금 무시)</summary>
    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        StartMotion(openAngle);
    }

    /// <summary>문을 닫는다. (코드에서도 호출 — 잠금 무시)</summary>
    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        StartMotion(0f);
    }

    /// <summary>잠금 상태를 설정. 잠그면 플레이어가 상호작용으로 못 연다.</summary>
    public void SetLocked(bool locked) => isLocked = locked;

    private void StartMotion(float angle)
    {
        if (motion != null) StopCoroutine(motion);
        Quaternion target = closedRotation * Quaternion.AngleAxis(angle, hingeAxis);
        motion = StartCoroutine(RotateTo(target));
    }

    private IEnumerator RotateTo(Quaternion target)
    {
        Quaternion start = transform.localRotation; // 현재 각도에서 시작(토글 중간에도 자연스럽게)
        float t = 0f;
        while (t < openDuration)
        {
            t += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(start, target, t / openDuration);
            yield return null;
        }
        transform.localRotation = target;
    }
}
