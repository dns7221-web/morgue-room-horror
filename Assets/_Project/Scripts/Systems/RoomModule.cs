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
public class RoomModule : MonoBehaviour, IAnomalyHost
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
    [Tooltip("이 방에서 '그것'이 서는 자리. Stalker는 씬 단일 존재라 이 모듈이 직접 소유하지 않고, 자리(Transform)만 들고 있는다.")]
    [SerializeField] private Transform stalkerSpawnPoint;

    /// <summary>옆 모듈이 맞물릴 접합부 Transform.</summary>
    public Transform SeamSocket => seamSocket != null ? seamSocket.transform : null;

    /// <summary>루프 재진입 시 플레이어 스폰 지점 (문 밖).</summary>
    public Transform StartPoint => startPoint;

    /// <summary>게임 최초 진입 시 플레이어 스폰 지점 (영안실 안쪽). 없으면 StartPoint로 대체.</summary>
    public Transform FirstEntryPoint => firstEntryPoint != null ? firstEntryPoint : startPoint;

    /// <summary>로그 식별용 이름 (IAnomalyHost).</summary>
    public string DisplayName => name;

    /// <summary>이번 세팅에서 이 방에 실제로 이상현상이 있는지 (판정 비교용).</summary>
    public bool HasAnomaly { get; private set; }

    /// <summary>
    /// 이번 방의 이상현상을 <b>'그것'으로 고정</b>한다 (방 데이터만 — 물리적 등장은
    /// GameManager가 <see cref="StalkerSpawnPoint"/>를 받아 씬 단일 Stalker에 지시한다).
    ///
    /// 랜덤 변형(시체 소실·이동·증가·서랍)은 전부 정상으로 되돌린다. 정답은 O(있음) —
    /// 플레이어는 <b>관찰로 발견해야</b> 한다. 등 뒤에서 튀어나오는 게 아니라 처음부터
    /// 거기 서 있는 것이 핵심이다.
    /// </summary>
    public void SetStalkerAsAnomaly()
    {
        HasAnomaly = true;
        StalkerIsAnomaly = true;
        if (anomalyManager != null) anomalyManager.SetAnomaly(false);   // 다른 변형은 정상 복원
    }

    /// <summary>이번 방의 이상현상이 '그것'인지 (디버그 표시용).</summary>
    public bool StalkerIsAnomaly { get; private set; }

    /// <summary>이번 방 이상현상 이름 (디버그 표시용).</summary>
    public string AnomalyLabel
    {
        get
        {
            if (!HasAnomaly) return "없음";
            if (StalkerIsAnomaly) return "그것";
            return anomalyManager != null ? anomalyManager.CurrentName : "있음";
        }
    }

    /// <summary>이 방에서 '그것'이 서는 자리 (GameManager가 Stalker.Appear()에 넘긴다).</summary>
    public Transform StalkerSpawnPoint => stalkerSpawnPoint;

    /// <summary>이상현상 유무를 세팅한다. 실제 발동/복원은 AnomalyManager가 담당.</summary>
    public void SetAnomaly(bool has)
    {
        HasAnomaly = has;
        StalkerIsAnomaly = false;
        if (anomalyManager != null) anomalyManager.SetAnomaly(has);
    }

    /// <summary>
    /// 복도 문(들)을 <b>애니메이션 없이 즉시</b> 지정 상태로 만든다.
    ///
    /// ── 왜 Open()/Close()가 아니라 즉시인가 ──────────────
    /// 이 호출은 <b>암전 중</b>에 일어난다. Open()은 1초에 걸쳐 스르륵 여는데
    /// 화면은 0.7초면 다 밝아지므로, 남은 시간만큼 <b>문이 저절로 움직이는 장면</b>이
    /// 플레이어에게 보인다. "새 방에 도착했다"가 아니라 "누가 문을 만지고 있다"가 된다.
    ///
    /// 게다가 Open()/Close()는 이미 그 상태인 문을 <b>그냥 통과시킨다</b>(isOpen 검사).
    /// 그래서 플레이어가 한쪽만 열어뒀다면 한쪽은 가만히 있고 반대쪽만 눈앞에서
    /// 움직이는 짝짝이가 되어, <b>같은 방을 돌려쓴다는 사실이 그대로 드러난다.</b>
    ///
    /// 안쪽 문(ResetInteriorDoors)이 이미 같은 이유로 즉시 처리를 쓴다 —
    /// 같은 상황이므로 같은 방식으로 푼다.
    /// </summary>
    public void SetDoorsImmediate(bool open)
    {
        foreach (var d in hallwayDoors)
            if (d != null) d.SetStateImmediate(open);
    }

    /// <summary>복도 문(들)을 '쾅' 닫는다 (방에 들어선 순간 연출).</summary>
    public void SlamDoors()
    {
        foreach (var d in hallwayDoors)
            if (d != null) d.Slam();
    }

    /// <summary>
    /// 안전구역 판정용 — <b>영안실 쪽 문(이중문)</b>이 전부 닫혀 있는지.
    ///
    /// 공간이 [복도] → [키패드 방] → [영안실] 순이고 추격자는 영안실에서 온다.
    /// 그래서 피신처(키패드 방)를 지키는 문은 <b>복도 문이 아니라 안쪽 이중문</b>이다.
    /// 복도 문은 반대편이라 닫아봐야 추격자를 막지 못한다.
    /// </summary>
    public bool AreShelterDoorsClosed()
    {
        foreach (var d in InteriorDoors)
            if (d != null && d.IsOpen) return false;
        return true;
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
