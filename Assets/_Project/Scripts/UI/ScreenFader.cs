using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 화면 전체를 검게 페이드했다가 다시 밝히는 연출.
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
/// 페이드가 '완전히 검어진 순간'에 콜백(onFullyBlack)을 실행한다.
/// 그 사이에 벌어지는 처리(모듈 재활용 = RoomModulePool.Recycle + 플레이어 스냅)를
/// 플레이어가 눈으로 못 보게 가리는 것이 핵심 — 공간 반복 트릭의 '검은 천'.
///
/// 배치: Screen Space - Overlay Canvas 아래, 화면을 꽉 채우는 검은 Image +
///       CanvasGroup을 가진 오브젝트에 붙인다. (CanvasGroup.alpha로 페이드)
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScreenFader : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("검게 어두워지는 시간(초).")]
    [SerializeField] private float fadeOutDuration = 0.4f;
    [Tooltip("완전히 검어진 상태로 머무는 시간(초). 이 사이에 건물 이동이 일어난다.")]
    [SerializeField] private float holdDuration = 0.1f;
    [Tooltip("다시 밝아지는 시간(초).")]
    [SerializeField] private float fadeInDuration = 0.6f;

    private CanvasGroup group;
    private bool isFading;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;              // 시작은 투명
        group.blocksRaycasts = false;
    }

    /// <summary>
    /// 검게 페이드 → (완전 암전 시 onFullyBlack 실행) → 다시 밝게.
    /// 이미 페이드 중이면 무시한다 (중복 트리거 방지).
    /// </summary>
    public void FadeThrough(Action onFullyBlack)
    {
        if (isFading) return;
        StartCoroutine(FadeRoutine(onFullyBlack));
    }

    private IEnumerator FadeRoutine(Action onFullyBlack)
    {
        isFading = true;
        group.blocksRaycasts = true;

        yield return Fade(0f, 1f, fadeOutDuration);    // 어두워짐

        onFullyBlack?.Invoke();                         // 암전 상태에서 건물 이동
        if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

        yield return Fade(1f, 0f, fadeInDuration);      // 밝아짐

        group.blocksRaycasts = false;
        isFading = false;
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
