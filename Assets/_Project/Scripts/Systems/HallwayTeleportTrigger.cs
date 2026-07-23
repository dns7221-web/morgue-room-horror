using UnityEngine;

/// <summary>
/// 어두운 복도 중간에 두는 트리거 볼륨. 플레이어가 지나가면
/// GameManager에 "다음 방으로 진행"(순간이동 + 방 리셋)을 요청한다.
///
/// 이 지점이 곧 "공간 반복의 이음새" — 어둠이 시야를 가린 사이에
/// 텔레포트가 일어나 플레이어는 눈치채지 못한다.
///
/// 배치: 복도 중간에 빈 오브젝트 + Collider(Is Trigger). 플레이어 키보다 크게.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HallwayTeleportTrigger : MonoBehaviour
{
    // 컴포넌트 추가 시 자동으로 Is Trigger 켜기.
    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 플레이어만 반응 (PlayerTeleporter 보유 여부로 식별).
        if (other.GetComponentInParent<PlayerTeleporter>() == null) return;

        if (GameManager.Instance != null)
            GameManager.Instance.AdvanceToNextRoom();
    }
}
