using UnityEngine;

/// <summary>
/// 복도 문틀을 통째로 감싸는 트리거 볼륨 — <b>문간 점유 센서</b>.
/// 플레이어가 문간을 완전히 빠져나온 순간 GameManager에 "방에 들어왔다"를 알린다.
///
/// ── 왜 얇은 판이 아니라 볼륨인가 ─────────────────────
/// 문턱에 얇은 판을 두고 "지나갔다"를 잡으면, 플레이어 몸이 <b>아직 문짝 궤적
/// 안에 있을 때</b> 슬램이 시작돼 문에 밀려난다. 빠르게 닫을수록 더 심해진다.
///
/// 그래서 문짝이 쓸고 지나가는 구역 전체를 볼륨으로 덮고, 두 가지를 보장한다.
///  ① 볼륨 안에 몸이 걸쳐 있는 동안에는 <b>아예 문을 못 닫게</b> 막는다
///  ② 볼륨을 <b>방 쪽으로</b> 완전히 빠져나간 순간에만 닫는다
/// 몸이 궤적을 벗어난 게 보장되므로 아무리 빠르게 닫아도 밀리지 않는다.
///
/// 복도 쪽으로 되돌아 나간 경우엔 닫지 않는다 — 아직 방에 들어온 게 아니다.
///
/// 배치: 각 방 모듈 프리팹의 복도 문틀 위치. Box Collider(Is Trigger)를
/// <b>문짝이 열렸다 닫히는 궤적 전체</b>를 덮도록 넉넉히 키울 것.
/// 모듈 로컬 +z가 방 안쪽 방향이어야 한다(안/밖 판정 기준).
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoomEntryTrigger : MonoBehaviour
{
    private Collider volume;

    // 컴포넌트 추가 시 자동으로 Is Trigger 켜기.
    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        volume = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;

        // 문간에 몸이 들어섰다 — 빠져나갈 때까지 문을 못 닫게 잠근다.
        if (GameManager.Instance != null)
            GameManager.Instance.SetDoorwayOccupied(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        if (GameManager.Instance == null) return;

        GameManager.Instance.SetDoorwayOccupied(false);

        // 방 쪽으로 빠져나갔을 때만 닫는다. 복도로 되돌아간 거면 그대로 열어둔다.
        if (IsOnRoomSide(other.transform.position))
            GameManager.Instance.OnPlayerEnteredRoom();
    }

    // 플레이어만 반응 (PlayerTeleporter 보유 여부로 식별).
    private static bool IsPlayer(Collider other) => other.GetComponentInParent<PlayerTeleporter>() != null;

    /// <summary>볼륨 중심을 기준으로 방 안쪽(+z)에 있는지.</summary>
    private bool IsOnRoomSide(Vector3 worldPosition)
    {
        float centerZ = volume is BoxCollider box ? box.center.z : 0f;
        return transform.InverseTransformPoint(worldPosition).z > centerZ;
    }
}
