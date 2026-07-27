using UnityEngine;

/// <summary>
/// 방 하나를 자립적으로 대표하는 컴포넌트. 모듈 프리팹(LoopGroup.prefab) 루트에 붙는다.
///
/// ── 설계 ──────────────────────────────────────────────
/// 영안실 지오메트리 + 이상현상(AnomalyManager) + 복도 문 + 접합 소켓을
/// 모두 '자식'으로 품는 자립 단위. 모든 참조가 내부로 닫혀 있어 복제본이
/// 깨지지 않고, 덕분에 RoomModulePool이 이 프리팹을 여러 개 복제해 조립할 수 있다.
///
/// ── 형태 ──────────────────────────────────────────────
/// 이 모듈은 <b>막다른 방</b>이다: [영안실] ── 복도 ── (열린 끝 = 소켓)
/// 출입구가 복도 끝 하나뿐이므로, 두 모듈은 소켓끼리 마주보게 붙어
/// 「방A ─ 복도A ─╫─ 복도B ─ 방B」 형태의 왕복 통로를 이룬다.
///
/// GameManager/Pool은 이 모듈에게 "이상현상 켜라 / 문 열어라 / 저기 붙어라"만
/// 지시하고, 실제 처리는 각 하위 시스템에 위임한다 (관심사 분리).
/// </summary>
public class RoomModule : MonoBehaviour
{
    [Header("References (모두 이 프리팹의 자식)")]
    [Tooltip("이 모듈 소속 이상현상 관리자.")]
    [SerializeField] private AnomalyManager anomalyManager;
    [Tooltip("이 모듈의 복도 문(들). 이중문이면 2개.")]
    [SerializeField] private Door[] hallwayDoors;
    [Tooltip("옆 모듈과 맞물릴 복도 끝 접합부. forward는 모듈 바깥을 향해야 한다.")]
    [SerializeField] private RoomSocket seamSocket;
    [Tooltip("게임 시작 시 플레이어가 설 지점 (첫 모듈에서만 사용).")]
    [SerializeField] private Transform startPoint;

    /// <summary>옆 모듈이 맞물릴 접합부 Transform.</summary>
    public Transform SeamSocket => seamSocket != null ? seamSocket.transform : null;

    /// <summary>플레이어 시작 지점 (첫 모듈 한정).</summary>
    public Transform StartPoint => startPoint;

    /// <summary>이번 세팅에서 이 방에 실제로 이상현상이 있는지 (판정 비교용).</summary>
    public bool HasAnomaly { get; private set; }

    /// <summary>이상현상 유무를 세팅한다. 실제 발동/복원은 AnomalyManager가 담당.</summary>
    public void SetAnomaly(bool has)
    {
        HasAnomaly = has;
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

    /// <summary>지정 앵커의 위치·회전으로 이동시킨다 (체인의 첫 모듈 배치용).</summary>
    public void PlaceAt(Transform anchor)
    {
        transform.SetPositionAndRotation(anchor.position, anchor.rotation);
    }

    /// <summary>
    /// 내 소켓이 <paramref name="otherSocket"/>과 정면으로 맞물리도록 모듈 전체를 배치한다.
    ///
    /// ── 계산 ──────────────────────────────────────────
    /// ① 회전: 내 소켓의 forward가 상대 소켓의 <b>반대</b> 방향을 보게 만든다.
    ///    루트에 곱할 보정 회전 = 목표회전 × 현재소켓회전⁻¹
    ///    (소켓은 루트에 고정돼 있으므로, 루트를 R만큼 돌리면 소켓도 R만큼 돈다)
    /// ② 이동: ①이 끝나 소켓이 새 방향을 잡은 <b>뒤에</b>, 두 소켓 위치가
    ///    겹치도록 루트를 평행이동한다. (순서를 바꾸면 회전이 위치를 흐트러뜨린다)
    ///
    /// 좌표를 하나도 하드코딩하지 않으므로, 모듈 길이가 바뀌어도 그대로 동작한다.
    /// </summary>
    public void ConnectTo(Transform otherSocket)
    {
        if (seamSocket == null)
        {
            Debug.LogError($"[RoomModule] {name}: seamSocket이 비어 있어 연결할 수 없습니다.", this);
            return;
        }

        // ① 회전 — 마주보게(forward 반대)
        Quaternion desired = Quaternion.LookRotation(-otherSocket.forward, Vector3.up);
        transform.rotation = desired * Quaternion.Inverse(SeamSocket.rotation) * transform.rotation;

        // ② 이동 — 회전 후의 소켓 위치를 읽어 평행이동
        transform.position += otherSocket.position - SeamSocket.position;
    }
}
