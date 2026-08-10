using UnityEngine;

/// <summary>
/// 격자 칸의 문 안쪽에 두는 점유 판정 트리거 — RoomEntryTrigger와 같은 발상이다.
/// 문간을 볼륨으로 통째로 덮어, 플레이어가 <b>그 칸 안으로 완전히 빠져나간
/// 순간</b>에만 GridManager에 알린다.
///
/// 이 트리거는 "나는 북쪽 문이다" 같은 방향을 스스로 들고 있지 않는다.
/// 자기가 속한 GridCell만 알면, GridManager가 그 칸의 좌표와 지금 중앙
/// 좌표를 비교해 이동 방향을 스스로 계산하기 때문이다 — 트리거 설정에서
/// 방향을 사람이 잘못 적어 넣는 사고 자체가 없어진다.
///
/// 배치: 각 GridCell 프리팹의 문 안쪽. Box Collider(Is Trigger)로 문간
/// 전체를 덮고, 로컬 +Z가 <b>칸 안쪽</b>을 향하게 놓을 것.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CellBoundaryTrigger : MonoBehaviour
{
    private GridCell cell;
    private Collider volume;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        cell = GetComponentInParent<GridCell>();
        volume = GetComponent<Collider>();

        if (cell == null)
            Debug.LogError("[CellBoundaryTrigger] 부모 계층에서 GridCell을 찾지 못했습니다.", this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        if (cell == null || GridManager.Instance == null) return;

        // 칸 안쪽으로 완전히 빠져나갔을 때만 진입으로 친다. 문간에서 되돌아
        // 나간 경우엔 반응하지 않는다 (RoomEntryTrigger.IsOnRoomSide와 동일 발상).
        if (IsOnRoomSide(other.transform.position))
            GridManager.Instance.OnPlayerEnteredCell(cell);
    }

    private static bool IsPlayer(Collider other) => other.GetComponentInParent<PlayerTeleporter>() != null;

    private bool IsOnRoomSide(Vector3 worldPosition)
    {
        float centerZ = volume is BoxCollider box ? box.center.z : 0f;
        return transform.InverseTransformPoint(worldPosition).z > centerZ;
    }
}
