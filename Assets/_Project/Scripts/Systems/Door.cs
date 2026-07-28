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
    [Tooltip("'쾅' 닫히는 데 걸리는 시간(초). 방에 들어선 순간 튕기듯 닫히는 연출용.")]
    [SerializeField] private float slamDuration = 0.07f;
    [Tooltip("쾅 닫힌 뒤 문틀에 부딪혀 되튀는 각도. 0이면 반동 없이 그냥 멈춘다.")]
    [SerializeField] private float slamReboundAngle = 6f;
    [Tooltip("반동(튕겼다 제자리로) 전체에 걸리는 시간(초).")]
    [SerializeField] private float slamReboundDuration = 0.13f;
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

    /// <summary>
    /// 문을 '쾅' 닫는다 — Close()와 결과는 같지만 훨씬 빠르고, 닫힌 뒤 문틀에
    /// 부딪혀 살짝 되튀는 반동이 붙는다. 방에 들어선 순간 등 뒤에서 문이
    /// 튕기듯 닫히는 연출용.
    ///
    /// <b>주의</b>: 문짝이 쓸고 지나가는 자리에 플레이어가 있으면 밀려난다.
    /// 호출 전에 문간이 비었는지 확인할 것 (RoomEntryTrigger가 그 역할을 한다).
    /// </summary>
    public void Slam()
    {
        if (!isOpen) return;
        isOpen = false;

        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(SlamRoutine());
    }

    /// <summary>잠금 상태를 설정. 잠그면 플레이어가 상호작용으로 못 연다.</summary>
    public void SetLocked(bool locked) => isLocked = locked;

    /// <summary>
    /// 애니메이션 없이 즉시 열림/닫힘 상태로 만든다.
    /// 암전 중 방을 처음 상태로 되돌릴 때 사용 — 문이 여닫히는 장면이 보이면 안 되기 때문.
    /// </summary>
    public void SetStateImmediate(bool open)
    {
        if (motion != null) { StopCoroutine(motion); motion = null; }
        isOpen = open;
        transform.localRotation = TargetOf(open ? openAngle : 0f);
    }

    private IEnumerator SlamRoutine()
    {
        // ① 쾅 — 현재 각도에서 닫힘까지 최단 시간에 내리꽂는다.
        yield return Rotate(TargetOf(0f), slamDuration);

        if (slamReboundAngle <= 0f || slamReboundDuration <= 0f) yield break;

        // ② 문틀에 부딪혀 열린 쪽으로 살짝 되튄다. (openAngle 부호 = 열리는 방향)
        float bounceBack = slamReboundDuration * 0.4f;
        yield return Rotate(TargetOf(Mathf.Sign(openAngle) * slamReboundAngle), bounceBack);

        // ③ 다시 제자리로 잦아든다.
        yield return Rotate(TargetOf(0f), slamReboundDuration - bounceBack);
    }

    private void StartMotion(float angle) => StartMotion(angle, openDuration);

    private void StartMotion(float angle, float duration)
    {
        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(Rotate(TargetOf(angle), duration));
    }

    /// <summary>닫힘 자세를 기준으로 지정 각도만큼 연 목표 회전.</summary>
    private Quaternion TargetOf(float angle) => closedRotation * Quaternion.AngleAxis(angle, hingeAxis);

    private IEnumerator Rotate(Quaternion target, float duration)
    {
        // 0초 이하면 보간할 게 없다 — 나누기 0을 피해 즉시 확정.
        if (duration <= 0f)
        {
            transform.localRotation = target;
            yield break;
        }

        Quaternion start = transform.localRotation; // 현재 각도에서 시작(토글 중간에도 자연스럽게)
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(start, target, t / duration);
            yield return null;
        }
        transform.localRotation = target;
    }
}
