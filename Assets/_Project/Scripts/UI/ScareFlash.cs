using System.Collections;
using UnityEngine;

/// <summary>
/// 오답 순간 화면에 <b>아주 잠깐</b> 그림 하나를 때려박는 연출.
///
/// ── 왜 필요한가 ──────────────────────────────────────
/// 원작(8번 출구)은 틀리면 왔던 길을 되돌아가야 해서 실패가 <b>몸으로</b> 느껴진다.
/// 이 게임은 진행도 숫자만 0으로 바뀌어, 틀렸다는 걸 알아도 <b>아프지가 않았다.</b>
/// 벌은 이미 있으니(진행도 소멸) 여기서 더 뺏지 않는다 — 대신 <b>놀라게</b> 해서
/// "틀렸다"를 감각으로 남긴다.
///
/// ── ScreenFader와 무엇이 다른가 ──────────────────────
/// 구조는 똑같다(CanvasGroup 알파를 코루틴으로 움직인다). 목적이 정반대다.
///  • ScreenFader — 건물이 옮겨가는 걸 <b>감추려고</b> 천천히 덮는다
///  • ScareFlash  — 못 보고 지나칠 수 없게 <b>때린다</b>
/// 그래서 기본값도 반대다. 페이더는 0.4초에 걸쳐 어두워지지만, 이쪽은
/// <b>등장 시간이 0</b>이다 — 서서히 나타나는 놀람은 놀람이 아니다.
///
/// 머무는 시간을 짧게(0.1초 안팎) 두는 것이 핵심이다. 길게 두면 플레이어가
/// 그림을 <b>뜯어보게</b> 되어 무서움이 사라지고, 짧으면 "방금 뭐였지?"가 남는다.
///
/// 배치: Screen Space - Overlay Canvas 아래, 화면을 꽉 채우는 Image +
///       CanvasGroup을 가진 오브젝트에 붙인다. (ScreenFader와 같은 방식)
///
/// ── 소리 (2026-10-28 추가) ───────────────────────────
/// 그림만으로는 화면을 안 보고 있던 순간(고개를 돌린 사이)엔 놀람이 통째로 빠진다.
/// 소리를 <b>이 부품 안에</b> 같이 둔다 — 번쩍임은 오답·잡힘 두 곳에서 불리므로, 소리를
/// 부르는 쪽마다 따로 달면 한쪽을 빠뜨리기 쉽다. "깜놀 = 그림 + 소리"를 한 곳에서 낸다.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScareFlash : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("나타나는 시간(초). 0이면 즉시 — 놀람 연출은 보통 0이 맞다.")]
    [SerializeField] private float fadeInDuration = 0f;
    [Tooltip("띄운 채 머무는 시간(초). 길면 뜯어보게 되어 안 무섭다.")]
    [SerializeField] private float holdDuration = 0.12f;
    [Tooltip("사라지는 시간(초). 잔상을 남기려면 등장보다 살짝 길게.")]
    [SerializeField] private float fadeOutDuration = 0.18f;

    // 추가 생성 (2026-10-28)
    [Header("Sound")]
    [Tooltip("번쩍일 때 같이 울릴 소리. 2D AudioSource에 클립을 넣어 연결한다. 비워도 그림만 번쩍인다.")]
    [SerializeField] private AudioSource scareSound;

    private CanvasGroup group;
    private Coroutine running;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;

        // 절대 클릭을 먹지 않게 한다. 판정 UI 위에 잠깐 뜨는 물건이라,
        // 이게 레이캐스트를 막으면 O/X 버튼이 씹힐 수 있다.
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    /// <summary>한 번 번쩍인다. 연달아 부르면 앞의 것을 끊고 다시 시작한다.</summary>
    /// <param name="playSound">
    /// 수정 (2026-10-28) — 소리도 같이 낼지. 다른 소리가 이 순간을 대신 맡을 때만 false로 부른다
    /// (예: 3번째 오답은 '그것' 경고음만 낸다 — 둘이 겹치면 경고가 묻힌다).
    /// </param>
    public void Flash(bool playSound = true)
    {
        // 이미 번쩍이는 중이면 무시하지 않고 <b>다시 시작</b>한다.
        // 무시하면 두 번째 오답에서 아무 반응이 없어 '먹통'으로 느껴진다.
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FlashRoutine());

        // 추가 생성 (2026-10-28) — 소리도 그림과 같은 규칙으로 처음부터 다시 낸다(PlayOneShot처럼 겹쳐 쌓지 않는다)
        if (playSound && scareSound != null) scareSound.Play();
    }

    private IEnumerator FlashRoutine()
    {
        yield return Fade(group.alpha, 1f, fadeInDuration);

        if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

        yield return Fade(1f, 0f, fadeOutDuration);

        running = null;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f) { group.alpha = to; yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        group.alpha = to;
    }
}
