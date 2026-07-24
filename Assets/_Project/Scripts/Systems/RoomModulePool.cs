using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 모듈을 소수(기본 2개)만 만들어 돌려쓰는 풀. 공간 반복 트릭의 심장.
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
///  • 오브젝트 풀링: 무한 생성 대신 몇 개를 순환 → 메모리/GC 부담 없음.
///  • 컬링 최적화: 한 번에 하나만 활성(SetActive), 나머지는 꺼서 렌더링 제거.
///  • 재활용(treadmill): 이음새(어두운 복도)를 지나는 순간 뒤 모듈을 앞으로 돌린다.
///
/// 실제 스왑 타이밍은 GameManager가 '화면 암전 순간'에 Recycle()을 호출해 맞춘다.
/// </summary>
public class RoomModulePool : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("복제해서 돌려쓸 방 모듈 프리팹.")]
    [SerializeField] private RoomModule modulePrefab;
    [Tooltip("활성 모듈이 놓일 위치·회전 (플레이어 앞 관찰 지점).")]
    [SerializeField] private Transform activeAnchor;
    [Tooltip("비활성(대기) 모듈이 숨는 위치. 화면 밖 아무 데나.")]
    [SerializeField] private Transform parkAnchor;
    [Tooltip("돌려쓸 모듈 개수. 2면 충분 (필요시 늘려도 됨).")]
    [SerializeField, Min(2)] private int poolSize = 2;

    private readonly List<RoomModule> pool = new();
    private int activeIndex;

    /// <summary>현재 활성(플레이어가 있는) 모듈.</summary>
    public RoomModule Active => pool[activeIndex];

    /// <summary>풀 생성 + 첫 모듈 배치. GameManager.Start()에서 1회 호출.</summary>
    public void Initialize()
    {
        for (int i = 0; i < poolSize; i++)
        {
            var m = Instantiate(modulePrefab, parkAnchor.position, parkAnchor.rotation);
            m.SetVisible(false);
            pool.Add(m);
        }

        activeIndex = 0;
        Active.PlaceAt(activeAnchor);
        Active.SetVisible(true);
    }

    /// <summary>
    /// 화면 암전 순간 호출: 다음 모듈을 앵커로 올려 활성화하고,
    /// 방금까지의 모듈은 파킹 위치로 치우며 비활성화(컬링)한다.
    /// </summary>
    /// <returns>새로 활성화된 모듈.</returns>
    public RoomModule Recycle()
    {
        var prev = Active;

        activeIndex = (activeIndex + 1) % pool.Count;
        Active.PlaceAt(activeAnchor);   // 새 것 등장
        Active.SetVisible(true);

        prev.PlaceAt(parkAnchor);       // 옛 것 소멸(컬링)
        prev.SetVisible(false);

        return Active;
    }
}
