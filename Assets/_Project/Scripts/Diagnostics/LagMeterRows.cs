#if UNITY_EDITOR || DEVELOPMENT_BUILD
using DHS.Diagnostics;
using UnityEngine;

/// <summary>
/// 이 프로젝트가 렉 미터(<see cref="LagMeter"/>)에게 <b>"이 구간도 재 달라"</b>고 알려주는 자리.
///
/// ── 왜 LagMeter 안에 안 적나 ──────────────────────────
/// <see cref="LagMeter"/>는 다른 유니티 작업물에도 그대로 떨어뜨려 쓸 도구다. 재고 싶은 구간
/// 이름을 그 파일 안에 적으면 <b>그 프로젝트 전용 파일</b>이 되어, 옮길 때마다 목록을 고쳐야 한다
/// (Momentum에서는 <c>PerchPointFinder.Scan</c>이 그 안에 박혀 있었다).
///
/// 그래서 <b>프로젝트가 자기를 소개하는 방향</b>으로 뒤집었다 — 도구는 자기가 뭘 재는지 모르고,
/// 프로젝트가 등록한다. 덕분에 <see cref="LagMeter"/>는 어느 작업물에 떨어뜨려도 손댈 데가 없다.
///
/// ── 순서가 보장되는 이유 ──────────────────────────────
/// <see cref="LagMeter"/>는 <c>AfterSceneLoad</c>에 스스로 생긴다. 등록은 그보다 먼저 끝나야
/// 표에 줄이 생기므로 <c>BeforeSceneLoad</c>에서 한다 — 유니티가 이 둘의 순서를 보장한다.
///
/// ── 새 구간을 재고 싶을 때 ────────────────────────────
///  ① 재고 싶은 코드에 <c>ProfilerMarker</c>를 하나 만들고 <c>using (marker.Auto()) { ... }</c>로 감는다
///  ② 아래 <see cref="Register"/>에 그 이름을 한 줄 더한다
/// 끝이다. 마커는 <b>처음 실제로 돌 때</b> 프로파일러에 등록되므로, 아직 한 번도 안 지나간 구간은
/// 잠시 "측정 안 됨"으로 보인다 (렉 미터가 1초마다 다시 찾는다).
/// </summary>
public static class LagMeterRows
{
    // 유니티 내장 단계 이름 — 들여쓰기(부모 줄)를 맞추는 데만 쓴다.
    private const string PhysicsStep = "FixedUpdate.PhysicsFixedUpdate";
    private const string UpdateStep = "Update.ScriptRunBehaviourUpdate";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        // ── 격자 재활용 ───────────────────────────────────────────────────
        // 이 포폴의 핵심 주장이 "끊김 없이 걸어서 넘어간다"인데, 재활용이 무거우면 그 프레임이
        // 튀어서 주장 자체가 깨진다. 지금까지는 안 튀는 것을 <b>눈으로만</b> 확인했다.
        //
        // 부모가 물리 단계인 이유: 재활용을 부르는 CellBoundaryTrigger.OnTriggerExit이
        // 물리 시뮬레이션 중에 불려서, 이 시간은 Update가 아니라 물리 쪽에 쌓인다.
        LagMeter.RegisterMarker("GridManager.Recycle", "격자 재활용", PhysicsStep);

        // 재활용 안에서 일어나는 두 가지를 따로 본다 — 합계만 보면 어느 쪽이 무거운지 모른다.
        LagMeter.RegisterMarker("CoverageAudit.Check", "└ 은폐 검사", "GridManager.Recycle");
        LagMeter.RegisterMarker("GameManager.Dress", "└ 이상현상 재추첨", "GridManager.Recycle");

        // ── 추격자 시야 판정 ──────────────────────────────────────────────
        // 위 셋은 한 칸 넘어갈 때 한 번이지만, 이것은 <b>매 프레임</b> 돈다(절두체 + 표본점 Linecast).
        // 상시 비용은 이쪽이라, 둘을 같은 표에서 보는 것이 의미가 있다.
        LagMeter.RegisterMarker("Stalker.Sight", "추격자 시야", UpdateStep);
    }
}
#endif
