using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 모듈 2개를 소켓끼리 맞물려 조립하고, 지금 플레이어가 있는 모듈을 추적하는 체인.
/// 공간 반복 트릭의 심장.
///
/// ── 배치 ──────────────────────────────────────────────
///   [방A] ── 복도A ──╫── 복도B ── [방B]
///                   ↑ 이음새(소켓 접합부)
/// 모듈이 막다른 방(출입구 = 복도 끝 하나)이라, 소켓끼리 마주보게 붙이면
/// 자연히 <b>영안실이 양 끝에 서로 등지고</b> 놓인다. 플레이어는 한쪽 방에서
/// 판정 → 복도를 <b>계속 걸어</b> → 반대쪽 방 도착. 매 루프마다 영안실이
/// 앞↔뒤로 뒤집히는 게 이 배치에서 공짜로 나온다.
///
/// ── 프로그래밍 포인트 ──────────────────────────────────
///  • 소켓 기반 조립: 좌표 하드코딩 없이 RoomModule.ConnectTo로 계산 배치.
///    모듈 길이를 바꿔도 씬을 다시 안 만져도 된다.
///  • 재활용: 방 실물은 2개뿐. 무한히 생성하는 대신, 플레이어가 못 보는
///    <b>반대편 방</b>을 몰래 새로 꾸며(Redress) 무한한 것처럼 속인다.
///  • 순간이동·암전이 필요 없다 — 두 모듈이 실제로 이어붙어 있으므로
///    플레이어는 처음부터 끝까지 <b>진짜로 걸어서</b> 이동한다.
///
/// 막다른 모듈은 접합부가 하나뿐이라 2개까지만 이어붙는다 (그래서 개수 고정).
/// </summary>
public class RoomModulePool : MonoBehaviour
{
    /// <summary>막다른 모듈은 소켓이 하나뿐이라 2개가 최대이자 필요 전부.</summary>
    private const int ModuleCount = 2;

    [Header("Setup")]
    [Tooltip("복제해서 이어붙일 방 모듈 프리팹.")]
    [SerializeField] private RoomModule modulePrefab;
    [Tooltip("체인의 첫 모듈이 놓일 기준 위치·회전. 두 번째는 여기에 맞물려 자동 배치된다.")]
    [SerializeField] private Transform rootAnchor;

    private readonly List<RoomModule> modules = new();
    private int currentIndex;

    /// <summary>지금 플레이어가 있는 쪽 모듈.</summary>
    public RoomModule Current => modules[currentIndex];

    /// <summary>반대편(플레이어가 못 보는) 모듈. 여기를 몰래 새로 꾸민다.</summary>
    public RoomModule Far => modules[(currentIndex + 1) % ModuleCount];

    /// <summary>모듈 2개 생성 + 소켓 접합. GameManager.Start()에서 1회 호출.</summary>
    public void Initialize()
    {
        for (int i = 0; i < ModuleCount; i++)
        {
            var m = Instantiate(modulePrefab, transform);
            m.name = $"{modulePrefab.name}_{i}";

            if (i == 0) m.PlaceAt(rootAnchor);                  // 첫 모듈: 기준 앵커에
            else m.ConnectTo(modules[i - 1].SeamSocket);        // 나머지: 앞 모듈 소켓에 맞물림

            modules.Add(m);
        }

        currentIndex = 0;
    }

    /// <summary>
    /// 플레이어가 이음새를 넘어 <paramref name="entered"/> 모듈로 들어왔음을 반영한다.
    /// 이미 그 모듈이면 아무 일도 하지 않는다 — 이음새 위에서 왔다 갔다 해도
    /// 상태가 꼬이지 않도록 <b>멱등</b>하게 만든 것 (카운터를 돌리는 방식의 함정).
    /// </summary>
    /// <returns>실제로 현재 모듈이 바뀌었으면 true.</returns>
    public bool SetCurrent(RoomModule entered)
    {
        int index = modules.IndexOf(entered);
        if (index < 0 || index == currentIndex) return false;

        currentIndex = index;
        return true;
    }
}
