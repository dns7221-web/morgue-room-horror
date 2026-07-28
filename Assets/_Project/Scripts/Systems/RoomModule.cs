using System.Collections.Generic;
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
/// 이 모듈은 <b>막다른 방</b>이다: [영안실] ── 복도 ── (끝 = 소켓)
/// 소켓은 실제로 통과하는 문이 아니라 <b>배치 기준점</b>이다. 이 지점을 축으로
/// 모듈을 뒤집어 반대편에 다시 놓으면, 건물이 좌↔우로 옮겨간 것처럼 보인다.
/// (실제로 벽을 뚫어 이어붙이지는 않는다 — 이음새가 눈에 띄기 때문)
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
    [Tooltip("루프마다(2번째 판정부터) 플레이어가 서는 지점 — 복도 쪽, 문 밖.")]
    [SerializeField] private Transform startPoint;
    [Tooltip("게임을 켰을 때 딱 한 번만 쓰는 지점 — 영안실 안쪽, 키패드 앞. 문을 열 필요가 없다.")]
    [SerializeField] private Transform firstEntryPoint;

    /// <summary>옆 모듈이 맞물릴 접합부 Transform.</summary>
    public Transform SeamSocket => seamSocket != null ? seamSocket.transform : null;

    /// <summary>루프 재진입 시 플레이어 스폰 지점 (문 밖).</summary>
    public Transform StartPoint => startPoint;

    /// <summary>게임 최초 진입 시 플레이어 스폰 지점 (영안실 안쪽). 없으면 StartPoint로 대체.</summary>
    public Transform FirstEntryPoint => firstEntryPoint != null ? firstEntryPoint : startPoint;

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

    /// <summary>복도 문(들)을 '쾅' 닫는다 (방에 들어선 순간 연출).</summary>
    public void SlamDoors()
    {
        foreach (var d in hallwayDoors)
            if (d != null) d.Slam();
    }

    /// <summary>복도 문(들)의 잠금을 설정한다. 풀어주면 플레이어가 E로 직접 열 수 있다.</summary>
    public void SetDoorsLocked(bool locked)
    {
        foreach (var d in hallwayDoors)
            if (d != null) d.SetLocked(locked);
    }

    /// <summary>
    /// 복도 문을 <b>뺀</b> 방 안 문들(플레이어가 자유롭게 여닫는 이중문 등)을
    /// 닫힌 상태로 즉시 되돌린다.
    ///
    /// ── 왜 필요한가 ──────────────────────────────────
    /// 모듈은 몇 개를 돌려쓰므로, 플레이어가 열어둔 문은 그 인스턴스에 그대로 남아
    /// 다음 차례에 열린 채로 등장한다. 매 루프 <b>똑같은 영안실</b>이어야 이상현상을
    /// 판별할 수 있는데, 문 상태가 회차마다 다르면 그걸 이상현상으로 오인하게 된다.
    ///
    /// 복도 문은 건드리지 않는다 — 그쪽은 GameManager가 따로 관리한다.
    /// </summary>
    public void ResetInteriorDoors()
    {
        foreach (var d in InteriorDoors)
            if (d != null) d.SetStateImmediate(false);
    }

    // 자식에서 자동으로 찾는다(복도 문 제외) — 문을 새로 추가해도 인스펙터 연결이 필요 없다.
    private Door[] InteriorDoors
    {
        get
        {
            if (interiorDoors != null) return interiorDoors;

            var all = GetComponentsInChildren<Door>(true);
            var rest = new List<Door>(all.Length);
            foreach (var d in all)
                if (System.Array.IndexOf(hallwayDoors, d) < 0) rest.Add(d);

            interiorDoors = rest.ToArray();
            return interiorDoors;
        }
    }

    private Door[] interiorDoors;

    /// <summary>모듈 전체를 켜고 끈다 (컬링 — 비활성 모듈은 렌더링 부담 제거).</summary>
    public void SetVisible(bool on) => gameObject.SetActive(on);

    /// <summary>지정 앵커의 위치·회전으로 이동시킨다.</summary>
    public void PlaceAt(Transform anchor)
    {
        transform.SetPositionAndRotation(anchor.position, anchor.rotation);
    }

    /// <summary>지정한 위치·회전으로 이동시킨다 (미리 계산해둔 배치 슬롯 적용용).</summary>
    public void PlaceAt(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>
    /// 내 소켓이 주어진 소켓 자세와 <b>정면으로 마주보도록</b> 모듈 전체를 배치한다.
    /// 소켓 지점을 축으로 모듈을 반대편으로 뒤집는 계산.
    ///
    /// ── 계산 ──────────────────────────────────────────
    /// ① 회전: 내 소켓의 forward가 기준 소켓의 <b>반대</b> 방향을 보게 만든다.
    ///    루트에 곱할 보정 회전 = 목표회전 × 현재소켓회전⁻¹
    ///    (소켓은 루트에 고정돼 있으므로, 루트를 R만큼 돌리면 소켓도 R만큼 돈다)
    /// ② 이동: ①이 끝나 소켓이 새 방향을 잡은 <b>뒤에</b>, 두 소켓 위치가
    ///    겹치도록 루트를 평행이동한다. (순서를 바꾸면 회전이 위치를 흐트러뜨린다)
    ///
    /// Transform이 아니라 값(위치·회전)을 받는 이유: 기준이 되는 소켓이
    /// <b>바로 이 모듈 자신의</b> 소켓인 경우가 있어서, 살아있는 Transform을
    /// 참조하면 ①에서 움직이는 순간 기준점까지 같이 흔들린다.
    ///
    /// 좌표를 하나도 하드코딩하지 않으므로, 모듈 길이가 바뀌어도 그대로 동작한다.
    /// </summary>
    public void AlignSeamTo(Vector3 socketPosition, Quaternion socketRotation)
    {
        if (seamSocket == null)
        {
            Debug.LogError($"[RoomModule] {name}: seamSocket이 비어 있어 배치를 계산할 수 없습니다.", this);
            return;
        }

        // ① 회전 — 마주보게(forward 반대)
        Quaternion desired = Quaternion.LookRotation(-(socketRotation * Vector3.forward), Vector3.up);
        transform.rotation = desired * Quaternion.Inverse(SeamSocket.rotation) * transform.rotation;

        // ② 이동 — 회전 후의 소켓 위치를 읽어 평행이동
        transform.position += socketPosition - SeamSocket.position;
    }
}
