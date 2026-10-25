using UnityEngine;

/// <summary>
/// GameManager가 "판정 대상 방/칸"에게 요구하는 최소 계약.
///
/// ── 왜 구현체가 하나인데 인터페이스인가 ────────────────
/// 원래는 <b>두 벌</b>이었다. 3안의 <c>RoomModule</c>(암전+순간이동으로 돌려쓰던 건물)과
/// 격자의 <see cref="GridCell"/>은 서로 다른 클래스이면서 모양이 같았다 — 둘 다 이상현상
/// 유무를 들고 있고, 추격자가 설 자리를 안다. 상속으로 묶을 수 없어(각자 다른 조립
/// 방식을 갖는 별개 컴포넌트) 이 인터페이스로 묶었고, 그래서 GameManager의 판정
/// 로직(Judge/Dress)을 두 벌로 쪼개지 않고 하나로 유지할 수 있었다.
///
/// 지금은 3안 경로를 제거해 구현체가 <see cref="GridCell"/> 하나다
/// (<c>RoomModule</c> 완성본은 <b>main 브랜치</b>에 있다). 계약은 그대로 남겨둔다 —
/// 판정 로직이 "칸"의 구체적 구조를 몰라도 되게 하는 경계가 여기이고, 무대를 또
/// 바꿀 때 꽂을 자리도 여기다.
/// </summary>
public interface IAnomalyHost
{
    /// <summary>
    /// 로그 식별용 이름. 재활용 한 번에 여러 칸이 한꺼번에 꾸며지는데(격자는 최대 3칸,
    /// 부팅 시엔 8칸), 이름 없이 "세팅 — 이상현상: 있음/없음"만 찍으면 <b>서로 다른 방
    /// 얘기가 한 방이 지 멋대로 뒤집히는 것처럼</b> 보인다. GameObject의 name을 그대로 쓴다.
    /// </summary>
    string DisplayName { get; }

    /// <summary>이번 세팅에서 이 방에 실제로 이상현상이 있는지 (판정 비교용).</summary>
    bool HasAnomaly { get; }

    /// <summary>이번 방의 이상현상이 '그것'(추격자)인지.</summary>
    bool StalkerIsAnomaly { get; }

    /// <summary>이 방에서 '그것'이 서는 자리.</summary>
    Transform StalkerSpawnPoint { get; }

    /// <summary>이상현상 유무를 세팅한다. 실제 발동/복원은 각자의 AnomalyManager가 담당.</summary>
    void SetAnomaly(bool has);

    /// <summary>이번 방의 이상현상을 '그것'으로 고정한다.</summary>
    void SetStalkerAsAnomaly();
}
