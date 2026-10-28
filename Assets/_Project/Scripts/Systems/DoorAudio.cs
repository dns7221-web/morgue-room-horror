using UnityEngine;

/// <summary>
/// 문 소리 — 열릴 때 '끼익', 다 닫혀 문틀에 닿을 때 '쿵'. 추가 생성 (2026-10-28).
/// <see cref="Door"/>의 신호(<see cref="Door.Opened"/> · <see cref="Door.Shut"/>)를 받아 그 문 자리에서 소리를 낸다.
///
/// 배치 위치: 씬 루트의 "DoorAudio" 오브젝트 하나. <c>Tools ▸ 소리 연결</c>이 만들고 클립까지 채운다.
///
/// ── 설계 의도 ──────────────────────────────────────────
///  • 문마다 붙이지 않고 씬에 하나만 둔다. 문짝은 칸 프리팹 안에 36개라 문마다 달면 프리팹을 고쳐야 하고
///    볼륨 하나 바꾸는 데도 36군데를 손대야 한다. 여기 하나면 칸 프리팹은 한 줄도 안 바뀐다.
///  • 재활용으로 칸이 통째로 옮겨져도 상관없다 — 소리를 낼 때마다 <b>그 순간의</b> 문 위치에서 낸다.
///  • 소리는 유니티 내장 <c>AudioSource.PlayClipAtPoint</c>로 낸다. 그 자리에 임시 3D 소리를 만들고 다 울리면
///    알아서 지운다. 3D라서 등 뒤에서 닫힌 문은 등 뒤에서, 멀리서 같이 닫힌 문(GridManager.CloseAllOpenDoors)은
///    작게 들린다 — 거리 감쇠를 따로 계산하지 않았다.
///  • '쿵'은 지나온 문이 닫히는 순간, 곧 뒷줄이 재활용된 직후에 난다. 맵이 옮겨졌다는 걸 귀로도 알 수 있다.
///
/// ── 이중문 겹침 ───────────────────────────────────────
///  쌍여닫이는 GridManager가 두 짝을 같은 프레임에 닫는다. 그대로 두면 같은 '쿵'이 두 겹으로 겹쳐
///  두 배 크기로 튄다. 그래서 같은 소리를 아주 짧은 시간 안에 가까운 자리에서 또 내려 하면 건너뛴다.
///  (여는 쪽은 짝마다 E를 따로 누르니 거의 안 겹치지만, 같은 규칙을 그대로 적용해 둔다.)
/// </summary>
[DisallowMultipleComponent]
public class DoorAudio : MonoBehaviour
{
    [Header("열림")]
    [Tooltip("문이 열리기 시작할 때. 비우면 이 소리만 빠진다.")]
    [SerializeField] private AudioClip openSound;
    [SerializeField, Range(0f, 1f)] private float openVolume = 0.7f;

    [Header("닫힘")]
    [Tooltip("다 닫혀 문틀에 닿은 순간. 도중에 다시 열린 문에서는 안 난다. 비우면 이 소리만 빠진다.")]
    [SerializeField] private AudioClip shutSound;
    [SerializeField, Range(0f, 1f)] private float shutVolume = 1f;

    [Header("이중문 겹침 막기")]
    [Tooltip("이 시간(초) 안에 같은 소리가 또 오면 같은 문간의 다른 짝으로 본다.")]
    [SerializeField, Min(0f)] private float pairWindow = 0.1f;
    [Tooltip("이 거리(m) 안이면 같은 문간으로 본다. 문간 폭 2m에 여유를 둔 값.")]
    [SerializeField, Min(0f)] private float pairDistance = 2.5f;

    // 소리마다 '마지막으로 낸 시각·자리' — 이중문 두 짝이 겹치는지 보려고 둔다.
    private float lastOpenTime = float.NegativeInfinity;
    private float lastShutTime = float.NegativeInfinity;
    private Vector3 lastOpenPosition;
    private Vector3 lastShutPosition;

    // 신호는 켜져 있는 동안만 받는다. 정적 이벤트라 끌 때 떼지 않으면 씬이 내려간 뒤에도 남는다.
    private void OnEnable()
    {
        Door.Opened += OnDoorOpened;
        Door.Shut += OnDoorShut;
    }

    private void OnDisable()
    {
        Door.Opened -= OnDoorOpened;
        Door.Shut -= OnDoorShut;
    }

    private void OnDoorOpened(Door door) =>
        Play(openSound, openVolume, door.transform.position, ref lastOpenTime, ref lastOpenPosition);

    private void OnDoorShut(Door door) =>
        Play(shutSound, shutVolume, door.transform.position, ref lastShutTime, ref lastShutPosition);

    /// <summary>
    /// 문 자리에서 소리를 한 번 낸다. 같은 소리를 방금(pairWindow 안) 가까운 곳(pairDistance 안)에서
    /// 냈다면 이중문의 다른 짝이므로 건너뛴다.
    /// </summary>
    private void Play(AudioClip clip, float volume, Vector3 position, ref float lastTime, ref Vector3 lastPosition)
    {
        if (clip == null) return;

        bool sameDoorway = Time.time - lastTime < pairWindow &&
                           (position - lastPosition).sqrMagnitude < pairDistance * pairDistance;
        if (sameDoorway) return;

        lastTime = Time.time;
        lastPosition = position;
        AudioSource.PlayClipAtPoint(clip, position, volume);
    }
}
