using UnityEngine;

/// <summary>
/// 플레이어 몸에서 나는 소리 — 발소리(걷기 · 대쉬)와 탈진 숨소리.
/// FirstPersonController의 상태(IsMoving · IsDashing · StaminaExhausted)를 읽어 소리로만 반영한다.
///
/// 배치 위치: FirstPersonController가 붙은 플레이어 오브젝트. AudioSource 두 개(발소리용 · 숨소리용)를 쓴다.
///
/// ── 설계 의도 ──────────────────────────────────────────
///  • 이동 '판단'은 FirstPersonController가, 소리 '반영'은 여기서 — HandAnimator · StaminaUI와 같은 분리.
///    그래서 컨트롤러 코드는 한 줄도 건드리지 않았다.
///  • 발소리는 시간이 아니라 <b>실제로 걸은 거리</b>마다 한 번 낸다. 벽에 막혀 제자리면 안 나고,
///    뒷걸음질(느림)은 저절로 드물게, 대쉬(빠름)는 촘촘하게 난다 — 속도마다 간격을 따로 맞출 필요가 없다.
///  • 같은 소리가 연달아 나면 기계음처럼 들려서, 직전 것을 빼고 고르고 높낮이를 살짝 흔든다.
///  • 숨소리는 발소리와 <b>다른 AudioSource</b>로 낸다. 같은 소스면 발소리마다 바꾸는 높낮이가
///    재생 중인 숨소리에도 걸려 숨이 출렁인다.
///  • 둘 다 2D다 — 내 몸에서 나는 소리라 방향이 필요 없다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAudio : MonoBehaviour
{
    [Tooltip("상태를 읽어올 플레이어 컨트롤러. 비우면 같은 오브젝트 · 부모에서 찾는다.")]
    [SerializeField] private FirstPersonController controller;

    [Header("발소리")]
    [SerializeField] private AudioSource footstepSource;
    [SerializeField] private AudioClip[] walkSteps;
    [Tooltip("대쉬할 때 쓸 발소리. 비우면 걷기 발소리를 쓴다.")]
    [SerializeField] private AudioClip[] runSteps;
    [Tooltip("걸을 때 한 발 사이 거리(m). 걷기 2.5m/s 기준 1.3이면 약 0.5초마다 한 발.")]
    [SerializeField, Min(0.1f)] private float walkStride = 1.3f;
    [Tooltip("대쉬할 때 한 발 사이 거리(m). 대쉬 4.5m/s 기준 1.7이면 약 0.38초마다 한 발.")]
    [SerializeField, Min(0.1f)] private float runStride = 1.7f;
    [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.6f;
    [Tooltip("발소리 높낮이를 이만큼 무작위로 흔든다 (0.05 = ±5%).")]
    [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.05f;

    [Header("탈진 숨소리")]
    [SerializeField] private AudioSource breathSource;
    [Tooltip("스태미너를 바닥까지 쓴 순간 한 번 재생한다.")]
    [SerializeField] private AudioClip exhaustedBreath;
    [SerializeField, Range(0f, 1f)] private float breathVolume = 0.8f;

    private Vector3 lastPosition;
    private float walked;          // 마지막 발소리 뒤로 걸은 거리(m)
    private int lastStep = -1;     // 직전에 낸 발소리 번호 — 같은 소리가 연달아 나는 것을 피한다
    private bool wasExhausted;     // 지난 프레임 탈진 여부 — '막 바닥난 순간'만 잡으려고 둔다

    /// <summary>인스펙터에서 붙이는 순간 소리 통로 두 개(발소리 · 숨소리)를 만들어 연결해 둔다.</summary>
    private void Reset()
    {
        var sources = GetComponents<AudioSource>();
        footstepSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
        breathSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
        foreach (var s in new[] { footstepSource, breathSource })
        {
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;
        }
    }

    private void Awake()
    {
        if (controller == null) controller = GetComponentInParent<FirstPersonController>();
        lastPosition = transform.position;
    }

    private void Update()
    {
        if (controller == null) return;
        Footsteps();
        Breath();
    }

    /// <summary>수평으로 실제 움직인 거리를 쌓다가, 한 보폭만큼 쌓이면 한 발.</summary>
    private void Footsteps()
    {
        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        delta.y = 0f;
        float moved = delta.magnitude;

        // 멈춰 있으면 보폭의 70%를 미리 채워 둔다 — 걷기 시작하자마자(약 0.15초 뒤) 첫 발이 나게.
        // 0에서 시작하면 첫 발이 한 보폭(0.5초)이나 늦어 '미끄러지듯' 출발한다.
        // 한 프레임에 1m 넘게 움직인 건 걸음이 아니라 순간이동(리스폰 등)이라 걸음으로 치지 않는다.
        if (!controller.IsMoving || moved > 1f)
        {
            walked = walkStride * 0.7f;
            return;
        }

        bool dashing = controller.IsDashing;
        walked += moved;
        if (walked < (dashing ? runStride : walkStride)) return;
        walked = 0f;

        var clips = dashing && runSteps != null && runSteps.Length > 0 ? runSteps : walkSteps;
        if (footstepSource == null || clips == null || clips.Length == 0) return;

        int i = PickStep(clips.Length);
        footstepSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        footstepSource.PlayOneShot(clips[i], footstepVolume);
    }

    /// <summary>직전과 다른 발소리를 고른다. 걷기/대쉬 묶음이 바뀌어 직전 번호가 범위를 벗어나면 그냥 고른다.</summary>
    private int PickStep(int count)
    {
        int i;
        if (count == 1) i = 0;
        else if (lastStep < 0 || lastStep >= count) i = Random.Range(0, count);
        else
        {
            i = Random.Range(0, count - 1);   // 직전 것을 뺀 count-1개 중에서
            if (i >= lastStep) i++;
        }
        lastStep = i;
        return i;
    }

    /// <summary>스태미너가 '막 바닥난 순간' 숨소리를 한 번. 바닥난 동안 계속 틀면 숨이 겹쳐 쌓인다.</summary>
    private void Breath()
    {
        bool exhausted = controller.StaminaExhausted;
        if (exhausted && !wasExhausted && breathSource != null && exhaustedBreath != null)
            breathSource.PlayOneShot(exhaustedBreath, breathVolume);
        wasExhausted = exhausted;
    }
}
