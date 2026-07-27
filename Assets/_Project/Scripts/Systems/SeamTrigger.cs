using UnityEngine;

/// <summary>
/// 복도에 두는 트리거 볼륨. 플레이어가 여기 들어오면 "지금 이 모듈에 있다"를
/// GameManager에 알린다. 각 모듈 프리팹 안에 하나씩 들어있으므로,
/// 이음새를 넘는 순간 <b>넘어간 쪽 모듈</b>의 트리거가 켜진다.
///
/// ── 왜 "몇 번째 방"이 아니라 "어느 모듈"인가 ─────────────
/// 진행 카운터를 +1 하는 방식은 플레이어가 이음새 위에서 왔다 갔다 하면
/// 그대로 꼬인다. 대신 <b>자기가 속한 모듈</b>을 그대로 보고하면, 같은 값이
/// 몇 번 들어와도 결과가 같아(멱등) 왕복·중복 진입에 안전하다.
///
/// 배치: 각 모듈 프리팹의 복도 구간에 빈 오브젝트 + Collider(Is Trigger).
/// 방 안까지 덮을 필요는 없고, 복도를 넉넉히 감싸면 된다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SeamTrigger : MonoBehaviour
{
    private RoomModule owner;

    // 컴포넌트 추가 시 자동으로 Is Trigger 켜기.
    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        // 프리팹 안에서 위로 훑어 자기 모듈을 찾는다 (인스펙터 수동 연결 불필요).
        owner = GetComponentInParent<RoomModule>();
        if (owner == null)
            Debug.LogError($"[SeamTrigger] {name}: 부모에 RoomModule이 없습니다.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        // 플레이어만 반응 (PlayerTeleporter 보유 여부로 식별).
        if (other.GetComponentInParent<PlayerTeleporter>() == null) return;

        if (owner != null && GameManager.Instance != null)
            GameManager.Instance.OnPlayerEnteredModule(owner);
    }
}
