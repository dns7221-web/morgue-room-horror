using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 개발용 단축키 + 상태 표시판. 빌드에는 넣지 않는다(체크만 끄면 됨).
///
/// ── 왜 필요한가 ──────────────────────────────────────
/// 이 게임은 한 판을 확인하려면 <b>방을 돌고, 관찰하고, 판정하고, 복도를 걷는</b>
/// 과정을 매번 반복해야 한다. 추격자처럼 "누적 오답 3회" 같은 조건이 붙은 기능은
/// 확인 한 번에 몇 분씩 걸린다. 그 반복을 없애는 것이 목적이다.
///
/// 화면 표시판은 <b>연결 누락</b>을 바로 드러내는 역할도 한다. 실제로
/// GameManager의 Stalker 슬롯이 비어 "정답은 있음인데 화면엔 아무것도 없는"
/// 상태를 한참 뒤에야 발견한 적이 있어, 그런 종류의 사고를 눈에 띄게 만든다.
///
/// 배치: 씬 아무 오브젝트에나 붙인다 (GameManager와 같은 곳이 편하다).
/// </summary>
public class DebugPanel : MonoBehaviour
{
    [Header("On / Off")]
    [Tooltip("단축키와 화면 표시를 모두 끈다.")]
    [SerializeField] private bool debugMode = true;
    [Tooltip("화면 좌상단 상태 표시판을 보여준다.")]
    [SerializeField] private bool showOverlay = true;

    [Header("단축키")]
    [Tooltip("이번 방을 무조건 정답으로 판정.")]
    [SerializeField] private Key correctKey = Key.F1;
    [Tooltip("이번 방을 무조건 오답으로 판정.")]
    [SerializeField] private Key wrongKey = Key.F2;
    [Tooltip("판정을 건너뛰고 바로 다음 방으로.")]
    [SerializeField] private Key nextRoomKey = Key.F3;
    [Tooltip("이번 방 이상현상을 즉시 '그것'으로 교체.")]
    [SerializeField] private Key stalkerKey = Key.F4;

    private GUIStyle style;

    private void Update()
    {
        if (!debugMode) return;

        var kb = Keyboard.current;
        var gm = GameManager.Instance;
        if (kb == null || gm == null) return;

        // 실제 이상현상 유무에 맞춰 답을 넣는다 — '무조건 정답/오답'을 만들려면
        // 방마다 답이 다르므로 현재 상태를 보고 반대로 넣어야 한다.
        if (kb[correctKey].wasPressedThisFrame)
        {
            gm.Judge(gm.CurrentRoomHasAnomaly);
            Debug.Log("[Debug] 강제 정답");
        }
        if (kb[wrongKey].wasPressedThisFrame)
        {
            gm.Judge(!gm.CurrentRoomHasAnomaly);
            Debug.Log("[Debug] 강제 오답");
        }
        if (kb[nextRoomKey].wasPressedThisFrame)
        {
            gm.AdvanceToNextRoom();
            Debug.Log("[Debug] 다음 방으로");
        }
        if (kb[stalkerKey].wasPressedThisFrame)
        {
            gm.DebugMakeStalkerAnomaly();
        }
    }

    private void OnGUI()
    {
        if (!debugMode || !showOverlay) return;

        var gm = GameManager.Instance;
        if (gm == null) return;

        style ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white },
            richText = true
        };

        var room = gm.CurrentRoom;
        string anomaly = room != null ? room.AnomalyLabel : "-";
        string answer = gm.CurrentRoomHasAnomaly ? "O(있음)" : "X(없음)";

        // 5안(코너 심리스) 검증용 — 모듈이 2개 켜지는 순간, 추격자가 둘이 되는
        // 순간을 눈으로 봐야 한다. 3안 구조에서는 둘 다 언제나 1이 정상.
        int activeModules = gm.ActiveModuleCount;
        int stalkersInScene = FindObjectsByType<Stalker>(FindObjectsSortMode.None).Length;
        string moduleColor = activeModules == 1 ? "#77dd77" : "#ff5555";
        string stalkerColor = stalkersInScene == 1 ? "#77dd77" : "#ff5555";

        // 연결 누락은 눈에 띄게 — 이것 때문에 '있음인데 아무것도 안 보이는' 사고가 난다.
        string stalkerWarning = gm.Stalker == null
            ? "\n<color=#ff5555>⚠ GameManager의 Stalker 슬롯 비어 있음</color>"
            : "";

        // 추격자가 '있다는데 안 보이는' 상황을 추적하기 위한 줄.
        // 위치 y가 바닥 아래거나 렌더러가 0/N이면 원인이 바로 드러난다.
        string stalkerState = gm.Stalker != null
            ? gm.Stalker.DebugState
            : "-";

        string text =
            $"<b>[DEBUG]</b>  F1 정답 / F2 오답 / F3 다음 방 / F4 그것 소환\n" +
            $"진행도 {gm.Progress}/{gm.ClearGoal}   누적 오답 {gm.Mistakes}/{gm.MistakesBeforeStalker}\n" +
            $"이번 방 이상현상: <b>{anomaly}</b>   정답: {answer}   판정함: {(gm.HasJudged ? "예" : "아니오")}\n" +
            $"피신 상태: {(gm.PlayerIsSheltered ? "안전 (문 닫힘)" : "노출")}\n" +
            $"활성 모듈 <color={moduleColor}>{activeModules}개</color>   추격자(씬) <color={stalkerColor}>{stalkersInScene}마리</color>\n" +
            $"추격자: {stalkerState}" +
            stalkerWarning;

        // 글자가 배경에 묻히지 않게 어두운 판을 깔아준다.
        // 활성 모듈/추격자 검증 줄 + 추격자 상태 줄이 두 줄이라 그만큼 더 잡는다.
        float height = stalkerWarning == "" ? 132f : 152f;
        GUI.Box(new Rect(8, 8, 620, height), GUIContent.none);
        GUI.Label(new Rect(16, 12, 610, height), text, style);
    }
}
