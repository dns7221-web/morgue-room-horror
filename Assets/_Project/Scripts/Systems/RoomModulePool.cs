using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 모듈을 소수(기본 2개)만 만들어 돌려쓰는 풀. 공간 반복 트릭의 심장.
///
/// ── 어떻게 속이나 ──────────────────────────────────────
/// 화면이 완전히 검어진 <b>암전 순간</b>에 이 세 가지가 동시에 일어난다.
///   ① 지금 건물을 끄고(컬링) 화면 밖 대기 위치로 치운다
///   ② 다음 건물을 <b>반대쪽 슬롯</b>에 켜서 세운다
///   ③ 플레이어를 새 건물의 복도 문 앞으로 스냅
/// 밝아지면 플레이어는 "복도를 걷다 보니 다시 영안실 문 앞"이라고 느낀다.
///
/// 씬뷰에서 보면 건물이 오른쪽에서 사라지고 왼쪽에 다시 서는 게 보인다.
/// 화면에 존재하는 건물은 <b>언제나 하나뿐</b>이라, 두 건물을 이어붙였을 때처럼
/// 이음새가 눈에 띌 일이 없다.
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
///  • 오브젝트 풀링: 무한 생성 대신 몇 개를 순환 → 메모리/GC 부담 없음.
///  • 컬링 최적화: 한 번에 하나만 활성(SetActive), 나머지는 꺼서 렌더링 제거.
///  • 배치 슬롯 실측: 좌우 슬롯을 씬에 손으로 찍지 않고 <b>소켓으로 계산</b>한다
///    (Initialize 참고). 모듈 크기가 바뀌어도 씬을 다시 안 만져도 된다.
///
/// 실제 스왑 타이밍은 GameManager가 '화면 암전 순간'에 Recycle()을 호출해 맞춘다.
/// </summary>
public class RoomModulePool : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("복제해서 돌려쓸 방 모듈 프리팹.")]
    [SerializeField] private RoomModule modulePrefab;
    [Tooltip("첫 번째 배치 슬롯. 두 번째 슬롯은 모듈 소켓을 기준으로 자동 계산된다.")]
    [SerializeField] private Transform rootAnchor;
    [Tooltip("비활성(대기) 모듈이 숨는 위치. 화면 밖 아무 데나.")]
    [SerializeField] private Transform parkAnchor;
    [Tooltip("돌려쓸 모듈 개수. 2면 충분 (필요시 늘려도 됨).")]
    [SerializeField, Min(2)] private int poolSize = 2;

    private readonly List<RoomModule> pool = new();

    // 번갈아 쓸 배치 슬롯 2곳 (좌/우). Initialize에서 소켓으로 실측해 채운다.
    private readonly List<Pose> slots = new();

    private int activeIndex;
    private int slotIndex;

    /// <summary>현재 활성(플레이어가 있는) 모듈.</summary>
    public RoomModule Active => pool[activeIndex];

    /// <summary>풀 생성 + 배치 슬롯 계산 + 첫 모듈 배치. GameManager.Start()에서 1회 호출.</summary>
    public void Initialize()
    {
        for (int i = 0; i < poolSize; i++)
        {
            var m = Instantiate(modulePrefab, transform);
            m.name = $"{modulePrefab.name}_{i}";
            pool.Add(m);
        }

        MeasureSlots();

        // 전부 화면 밖으로 치우고 끈 뒤, 첫 모듈만 슬롯 0에 세운다.
        foreach (var m in pool)
        {
            m.PlaceAt(parkAnchor);
            m.SetVisible(false);
        }

        activeIndex = 0;
        slotIndex = 0;
        Active.PlaceAt(slots[0].position, slots[0].rotation);
        Active.SetVisible(true);
    }

    /// <summary>
    /// 배치 슬롯 두 곳을 <b>모듈 자신을 자로 삼아</b> 실측한다.
    ///
    /// 슬롯0 = rootAnchor 그대로.
    /// 슬롯1 = 소켓 지점을 축으로 180° 뒤집은 자리 — 건물이 복도 끝 너머
    ///         반대편에 서는 위치다. 좌표를 씬에 하드코딩하지 않으므로,
    ///         복도를 늘리든 줄이든 슬롯이 알아서 따라온다.
    /// </summary>
    private void MeasureSlots()
    {
        var probe = pool[0];

        probe.PlaceAt(rootAnchor);
        slots.Add(new Pose(probe.transform.position, probe.transform.rotation));

        if (probe.SeamSocket == null)
        {
            // 소켓이 없으면 뒤집을 기준이 없다. 크래시 대신 슬롯 하나로 굴러가게 두고
            // (= 예전처럼 제자리 반복) 원인을 로그로 남긴다.
            Debug.LogError(
                $"[RoomModulePool] {modulePrefab.name}에 Seam Socket이 없어 반대편 슬롯을 계산할 수 없습니다. " +
                "건물이 늘 같은 자리에 나타납니다.", this);
            return;
        }

        // 소켓 자세를 '값'으로 떠 놓는다 — probe를 움직이면 소켓도 같이 움직이므로.
        Vector3 seamPos = probe.SeamSocket.position;
        Quaternion seamRot = probe.SeamSocket.rotation;

        probe.AlignSeamTo(seamPos, seamRot);
        slots.Add(new Pose(probe.transform.position, probe.transform.rotation));
    }

    /// <summary>
    /// 화면 암전 순간 호출: 다음 모듈을 <b>반대쪽 슬롯</b>에 세워 활성화하고,
    /// 방금까지의 모듈은 화면 밖으로 치우며 비활성화(컬링)한다.
    /// </summary>
    /// <returns>새로 활성화된 모듈.</returns>
    public RoomModule Recycle()
    {
        var prev = Active;

        activeIndex = (activeIndex + 1) % pool.Count;
        slotIndex = (slotIndex + 1) % slots.Count;   // ← 좌↔우 번갈아

        Active.PlaceAt(slots[slotIndex].position, slots[slotIndex].rotation);
        Active.SetVisible(true);                     // 새 것 등장

        prev.PlaceAt(parkAnchor);
        prev.SetVisible(false);                      // 옛 것 소멸(컬링)

        return Active;
    }
}
