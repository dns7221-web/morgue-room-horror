using UnityEngine;

/// <summary>
/// GameManager가 "판정 대상 방"에게 요구하는 최소 계약.
///
/// <see cref="RoomModule"/>(3안)과 <see cref="GridCell"/>(격자)은 서로 다른
/// 클래스이면서 모양이 같다 — 둘 다 이상현상 유무를 들고 있고, 추격자가 설 자리를
/// 안다. 상속으로 묶을 수 없어(각자 다른 조립 방식을 갖는 별개 컴포넌트) 인터페이스로
/// 묶는다. 그래서 GameManager의 판정 로직(Judge/Dress)을 3안용·격자용 두 벌로
/// 쪼개지 않고 하나로 유지할 수 있다.
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
