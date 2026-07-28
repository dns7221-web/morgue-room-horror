using UnityEngine;

/// <summary>
/// 추격자로부터 몸을 피할 수 있는 구역 — <b>영안실(이상현상 체크 구간)</b>.
///
/// ── 규칙 ──────────────────────────────────────────────
/// 이 구역에 <b>들어와 있고 + 복도 문이 닫혀 있으면</b> 안전하다.
/// 둘 중 하나라도 어긋나면 추격자는 그대로 다가온다.
///
/// 즉 <b>뛰어들어오는 것만으로는 부족하고</b>, 돌아서서 문을 닫아야 한다.
/// 다급하게 도망쳐 들어와 문고리를 잡는 그 순간이 이 규칙의 목적이다.
///
/// ── 왜 여기가 안전구역인가 ────────────────────────────
/// 어차피 판정하러 돌아와야 하는 곳이다. 별도의 은신처를 새로 만들면
/// 동선이 하나 더 늘지만, 이미 가야 하는 곳을 안전구역으로 삼으면
/// <b>도망치는 방향과 진행하는 방향이 같아진다.</b>
///
/// 배치: 방 모듈 프리팹 안, 영안실 쪽에 Collider(Is Trigger).
/// 플레이어가 숨을 만한 범위를 넉넉히 덮을 것.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SafeZone : MonoBehaviour
{
    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        if (GameManager.Instance != null) GameManager.Instance.SetInSafeZone(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        if (GameManager.Instance != null) GameManager.Instance.SetInSafeZone(false);
    }

    // 플레이어만 반응 (PlayerTeleporter 보유 여부로 식별 — 다른 트리거들과 같은 방식).
    private static bool IsPlayer(Collider other) => other.GetComponentInParent<PlayerTeleporter>() != null;
}
