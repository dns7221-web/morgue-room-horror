using UnityEngine;

/// <summary>
/// 복도 쪽 문을 지나 방 안쪽으로 들어왔을 때 걸리는 트리거.
/// 플레이어가 지나가면 GameManager에 "방에 들어왔다"를 알려 문을 닫는다.
///
/// ── 왜 필요한가 ──────────────────────────────────────
/// 새 방으로 진행할 때 플레이어는 복도 쪽(문 밖)에 스폰되므로, 문이 열려
/// 있어야 걸어 들어올 수 있다(GameManager.DoAdvance가 미리 열어둠).
/// 들어온 뒤엔 이 트리거가 문을 닫아 "판정해야 다시 열리는" 긴장감을 만든다.
///
/// 배치: 각 방 모듈 프리팹 내부, 복도 문 바로 안쪽(방 쪽)에 빈 오브젝트 +
/// Collider(Is Trigger). 풀링되는 모듈마다 하나씩 있어야 하므로 프리팹 안에 둔다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoomEntryTrigger : MonoBehaviour
{
    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerTeleporter>() == null) return;

        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayerEnteredRoom();
    }
}
