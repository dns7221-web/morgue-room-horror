using UnityEngine;

/// <summary>
/// 방 하나를 자립적으로 대표하는 컴포넌트. 모듈 프리팹(RoomModule.prefab) 루트에 붙는다.
///
/// ── 설계 ──────────────────────────────────────────────
/// 영안실 지오메트리 + 이상현상(AnomalyManager) + 복도 문 + 시작점 마커를
/// 모두 '자식'으로 품는 자립 단위. 덕분에 RoomModulePool이 이 프리팹을
/// 복제해 2개를 돌려쓸 수 있다 (모든 참조가 내부로 닫혀 있어 복제본이 안 깨짐).
///
/// GameManager/Pool은 이 모듈에게 "이상현상 켜라 / 문 열어라 / 여기 놓여라"만
/// 지시하고, 실제 처리는 각 하위 시스템에 위임한다 (관심사 분리).
/// </summary>
public class RoomModule : MonoBehaviour
{
    [Header("References (모두 이 프리팹의 자식)")]
    [Tooltip("이 모듈 소속 이상현상 관리자.")]
    [SerializeField] private AnomalyManager anomalyManager;
    [Tooltip("이 모듈의 복도 문(들). 이중문이면 2개.")]
    [SerializeField] private Door[] hallwayDoors;
    [Tooltip("플레이어가 스냅될 시작 지점 마커.")]
    [SerializeField] private Transform startPoint;

    /// <summary>플레이어 순간이동 목적지(위치·회전).</summary>
    public Transform StartPoint => startPoint;

    /// <summary>이번 세팅에서 이 방에 실제로 이상현상이 있는지 (판정 비교용).</summary>
    public bool CurrentHasAnomaly { get; private set; }

    /// <summary>이상현상 유무를 세팅한다. 실제 발동/복원은 AnomalyManager가 담당.</summary>
    public void SetAnomaly(bool has)
    {
        CurrentHasAnomaly = has;
        if (anomalyManager != null) anomalyManager.SetAnomaly(has);
    }

    /// <summary>복도 문(들)을 연다.</summary>
    public void OpenDoors()
    {
        foreach (var d in hallwayDoors)
            if (d != null) d.Open();
    }

    /// <summary>복도 문(들)을 닫는다.</summary>
    public void CloseDoors()
    {
        foreach (var d in hallwayDoors)
            if (d != null) d.Close();
    }

    /// <summary>모듈 전체를 켜고 끈다 (컬링 — 비활성 모듈은 렌더링 부담 제거).</summary>
    public void SetVisible(bool on) => gameObject.SetActive(on);

    /// <summary>지정 앵커의 위치·회전으로 이동시킨다.</summary>
    public void PlaceAt(Transform anchor)
    {
        transform.SetPositionAndRotation(anchor.position, anchor.rotation);
    }
}
