using UnityEngine;

/// <summary>
/// '그것'이 다가오는 소리. 추격자(<see cref="Stalker"/>)가 <b>실제로 움직인 동안에만</b> 소리를 키우고,
/// 멈추면(쳐다볼 때·판정 패널·피신) 서서히 줄인다.
///
/// 배치 위치: Stalker와 같은 오브젝트(씬 루트의 추격자). AudioSource가 같이 붙는다.
///
/// ── 설계 의도 ──────────────────────────────────────────
///  • 움직임 '판단'은 Stalker가, 소리 '반영'은 여기서 — HandAnimator가 FirstPersonController를
///    읽는 것과 같은 분리다. 그래서 Stalker에는 IsMoving 값 하나만 더했다.
///  • 3D 소리라 <b>등 뒤 어느 쪽에서 오는지</b> 들린다. 돌아보는 순간 소리가 잦아들어서
///    "보면 멈춘다"는 규칙을 귀로도 확인시킨다.
///  • 루프를 Play/Stop으로 껐다 켜지 않고 <b>볼륨만</b> 밀어 올리고 내린다. 껐다 켜면 '틱' 소리가 나고,
///    쳐다봤다 뗐다 할 때마다 처음부터 다시 재생돼 기계적으로 들린다.
///  • 추격자는 칸이 아니라 씬 루트에 있어서(Stalker 주석 참고) 재활용으로 칸이 옮겨져도 소리 위치가 튀지 않는다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class StalkerAudio : MonoBehaviour
{
    [Tooltip("움직임을 읽어올 추격자. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private Stalker stalker;
    [Tooltip("다가올 때의 볼륨 (0~1).")]
    [SerializeField, Range(0f, 1f)] private float movingVolume = 0.8f;
    [Tooltip("볼륨이 0에서 최대까지 가는 데 걸리는 시간(초). 짧을수록 쳐다보는 즉시 끊긴다.")]
    [SerializeField, Min(0.01f)] private float fadeTime = 0.25f;

    private AudioSource source;

    /// <summary>
    /// 인스펙터에서 붙이는 순간 AudioSource 기본값을 맞춰 둔다 — 루프 · 3D · 자동 재생 끔.
    /// 1인칭에서 칸 하나(약 20m) 밖은 안 들리게 거리 감쇠를 직선으로 둔다(기본 로그 감쇠는 수백 m까지 들린다).
    /// </summary>
    private void Reset()
    {
        var s = GetComponent<AudioSource>();
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 2f;
        s.maxDistance = 20f;
        s.volume = 0f;
    }

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        if (stalker == null) stalker = GetComponent<Stalker>();
        source.volume = 0f;
    }

    private void Update()
    {
        if (stalker == null || source.clip == null) return;

        // 사라진 상태(등장 전 · 잡은 뒤 · 다른 칸으로 빠져나감)면 바로 끈다.
        // 여기서 서서히 줄이면 '그것'이 없는 빈 방에서 끄는 소리가 잠깐 남는다.
        if (!stalker.IsActive)
        {
            if (source.isPlaying) source.Stop();
            source.volume = 0f;
            return;
        }

        if (!source.isPlaying) source.Play();

        // 움직이면 movingVolume까지, 멈추면 0까지 — fadeTime 동안 일정한 속도로 따라간다
        float target = stalker.IsMoving ? movingVolume : 0f;
        source.volume = Mathf.MoveTowards(source.volume, target, movingVolume / fadeTime * Time.deltaTime);
    }
}
