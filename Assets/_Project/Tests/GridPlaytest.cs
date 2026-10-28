#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// 자동 플레이테스트 — 실제 씬을 플레이 모드로 띄워 놓고 플레이어를 걸려서
/// 격자 이동 · 판정 · 이상현상 · 추격자가 규칙대로 도는지 확인한다.
///
/// 사람이 손으로 하던 확인(WASD로 걸어가 보고, E로 문 열고, O/X 누르고)을
/// 그대로 코드로 옮긴 것이다 — 키 입력 대신 같은 경로를 직접 부른다.
///   • 문 열기  : Door.Interact()        (PlayerInteractor가 E에 부르는 것)
///   • 판정     : JudgmentPanel.Interact() → JudgmentUI.OnClick...()  (키패드 → 버튼)
///   • 걷기     : CharacterController.Move()  (FirstPersonController가 WASD로 부르는 것)
///
/// 걷기는 <b>순간이동이 아니라</b> 매 프레임 조금씩 미는 것이라, 문간 트리거
/// (CellBoundaryTrigger)와 재활용이 실제 플레이와 똑같이 작동한다.
///
/// ── 돌리는 법 ──────────────────────────────────────────
///  • 에디터 안: Window ▸ General ▸ Test Runner ▸ PlayMode 탭에서 GridPlaytest 실행
///  • 명령줄(창 없이, 빠름):
///      Unity.exe -projectPath &lt;프로젝트&gt; -batchmode -runTests -testPlatform PlayMode
///                -testFilter GridPlaytest -testResults results.xml -logFile unity.log
///    -batchmode를 빼면 창이 떠서 Game 뷰에서 실제 속도로 걷는 모습이 보인다.
///    같은 프로젝트를 에디터가 열고 있으면 명령줄 실행은 잠금 때문에 안 된다 — 워크트리처럼
///    다른 폴더에서 돌리거나 에디터 안에서 돌릴 것.
///  • 이 파일은 asmdef 없이 Assembly-CSharp에 들어가므로 테스트 러너의
///    "Enable playmode tests for all assemblies"(ProjectSettings의 playModeTestRunnerEnabled)가 켜져 있어야 한다.
///
/// ── 남는 기록 ──────────────────────────────────────────
///  • [PT]  — 테스트 진행·판정·발견한 문제
///  • [OBS] — 관찰자(PlaytestObserver): 중앙 바뀜·재활용·칸별 이상현상·문 열림/닫힘을 시간순으로
///  • 사진  — Logs/playtest-shots/run-{환경변수 PLAYTEST_TAG}/ (게임 카메라 화면)
/// </summary>
public class GridPlaytest
{
    private const string ScenePath = "Assets/_Project/Scenes/HorrorGame.unity";
    private const float WalkSpeed = 2.5f;    // FirstPersonController의 기본 걷기 속도
    private const float HeadlessFrameTime = 0.02f;   // 헤드리스에서 한 프레임에 흐르게 할 게임 시간(초) — SetUp 참고
    private const float DoorWait = 1.4f;     // Door Open Duration(1초) + 여유

    private static readonly Vector2Int N = new Vector2Int(0, 1);
    private static readonly Vector2Int E = new Vector2Int(1, 0);
    private static readonly Vector2Int S = new Vector2Int(0, -1);
    private static readonly Vector2Int W = new Vector2Int(-1, 0);

    private Transform playerRoot;
    private CharacterController playerBody;
    private CoverageAudit audit;
    private PlayerHitRecorder hits;
    private static int shotIndex;   // 사진 번호 — 한 판 동안 이어서 매긴다

    private readonly List<string> errors = new List<string>();     // 게임이 뱉은 에러 로그
    private readonly List<string> findings = new List<string>();   // 테스트가 찾은 문제

    // ──────────────────────────────────────────────────────────────
    // 준비 / 정리
    // ──────────────────────────────────────────────────────────────

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        errors.Clear();
        findings.Clear();

        // 게임이 에러 로그를 내도 그 자리에서 테스트를 끊지 않는다 — 끝까지 걸어보고
        // 한꺼번에 보고해야 "어디까지 되고 어디서 깨지는가"가 남는다.
        LogAssert.ignoreFailingMessages = true;
        Application.logMessageReceived += OnLog;

        // 앞 테스트의 씬이 아직 살아 있으므로, <b>새 격자가 설 때까지</b> 기다려야 한다.
        // 그냥 "GridManager가 null이 아니면 시작"으로 두면 앞 씬의 플레이어를 붙잡은 채
        // 출발해, 씬이 교체되는 순간 그 참조가 파괴돼 버린다.
        var previousGrid = GridManager.Instance;
        EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));

        float waited = 0f;
        while (waited < 60f &&
               (GridManager.Instance == null
                || ReferenceEquals(GridManager.Instance, previousGrid)
                || GridManager.Instance.CenterCell == null
                || GameManager.Instance == null))
        {
            waited += Time.deltaTime;
            yield return null;
        }

        Assert.IsNotNull(GridManager.Instance, "씬에 GridManager가 없다 (씬 로드 실패?)");
        Assert.IsNotNull(GameManager.Instance, "씬에 GameManager가 없다");
        Assert.IsNotNull(GridManager.Instance.CenterCell, "가운데 칸이 만들어지지 않았다");

        var teleporter = UnityEngine.Object.FindAnyObjectByType<PlayerTeleporter>();
        Assert.IsNotNull(teleporter, "씬에 플레이어(PlayerTeleporter)가 없다");
        playerRoot = teleporter.transform;
        playerBody = playerRoot.GetComponent<CharacterController>();
        audit = UnityEngine.Object.FindAnyObjectByType<CoverageAudit>();

        // 관찰자 — 격자 이벤트와 칸마다의 이상현상 변화를 시간순으로 남긴다(씬에 붙어 다음 로드 때 같이 사라진다)
        // 헤드리스에서는 렌더링이 없어 한 프레임이 1ms도 안 걸린다. 그러면 물리 계산(0.02초마다)이
        // 돌기 전에 플레이어가 문간 트리거를 통째로 지나가 버려 칸 진입이 안 잡힌다(겪은 일).
        // 프레임마다 게임 시간을 정확히 0.02초씩 흐르게 고정하면 매 프레임 물리가 한 번 돈다 —
        // 빠르게 돌면서도 실제 플레이와 같은 시간 흐름이 된다(문 여닫힘·추격자 속도도 같이 맞는다).
        if (Application.isBatchMode) Time.captureDeltaTime = HeadlessFrameTime;

        new GameObject("PlaytestObserver").AddComponent<PlaytestObserver>();
        hits = playerRoot.gameObject.AddComponent<PlayerHitRecorder>();   // 무엇에 부딪혔는지 (막혔을 때 보고용)

        PT($"씬 로드 완료 — 칸 {GridManager.Instance.Cells.Count}개, 간격 {GridManager.Instance.Spacing}, " +
           $"플레이어 {playerRoot.position}, 은폐 검사기 {(audit != null ? "있음" : "없음")}");

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Application.logMessageReceived -= OnLog;
        Time.captureDeltaTime = 0f;
        yield return null;
    }

    // ──────────────────────────────────────────────────────────────
    // 1. 격자가 제대로 깔리는가
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator A_격자_초기_배치()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        var grid = GridManager.Instance;

        PT($"번호판 배치:\n{IdMap()}");
        PT($"중앙 좌표 {grid.CenterCoord} · 중앙 칸 번호 {grid.CenterCell.Id} · Radius {grid.Radius}");

        if (grid.Cells.Count != 9) findings.Add($"칸이 9개가 아니다: {grid.Cells.Count}개");
        if (grid.CenterCell.Id != 5) findings.Add($"가운데 칸 번호가 5가 아니다: {grid.CenterCell.Id}");
        if (grid.Spacing.x < 1f || grid.Spacing.y < 1f)
            findings.Add($"칸 간격 실측이 이상하다: {grid.Spacing} (칸이 겹쳐 깔린다)");

        CheckGridInvariants("초기 배치");

        // 첫 칸은 '이상현상 없음'으로 고정돼야 한다 (GameManager.BeginFirstCell)
        if (grid.CenterCell.HasAnomaly)
            findings.Add($"첫 칸에 이상현상이 있다: {grid.CenterCell.AnomalyLabel}");

        // 판정 전에는 못 나가야 한다
        if (!GridManager.Instance.AreBoundaryDoorsClosed(grid.CenterCoord, grid.CenterCoord + N))
            findings.Add("시작부터 북쪽 문이 열려 있다");

        PT($"첫 칸 이상현상: {grid.CenterCell.AnomalyLabel} · 나머지 칸: {AnomalyMap()}");

        yield return null;
        Snap("A-시작방-정면");
        Report("A 격자 초기 배치");
    }

    // ──────────────────────────────────────────────────────────────
    // 2. 사방으로 걸어서 이동 — 재활용이 규칙대로 도는가
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator B_사방으로_걸어_이동()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        var grid = GridManager.Instance;
        var route = new[] { N, E, S, W, N, N, E, S, W, N };

        // 추가 생성 (2026-10-28) — 지나온 문이 문틀에 닿을 때 '쿵' 신호(Door.Shut)가 나는지 센다.
        // 문 소리는 DoorAudio가 이 신호를 받아서 내므로, 신호가 안 나면 소리도 안 난다.
        int shutSignals = 0;
        System.Action<Door> onShut = _ => shutSignals++;
        Door.Shut += onShut;

        foreach (var dir in route)
        {
            Vector2Int before = grid.CenterCoord;
            int beforeId = grid.CenterCell.Id;

            yield return JudgeCurrent();                 // 판정해야 문이 풀린다
            yield return Cross(dir);

            if (grid.CenterCoord != before + dir)
            {
                findings.Add($"{Name(dir)}쪽으로 걸었는데 중앙이 {before} → {grid.CenterCoord} (기대 {before + dir})");
                break;
            }

            PT($"{Name(dir)} 이동 성공 — 좌표 {before}→{grid.CenterCoord}, 칸 번호 {beforeId}→{grid.CenterCell.Id}, " +
               $"재활용 {grid.LastRecycle.from}→{grid.LastRecycle.to}, 이상현상 {grid.CenterCell.AnomalyLabel}\n{IdMap()}");

            CheckGridInvariants($"{Name(dir)} 이동 후");
        }

        // 추가 생성 (2026-10-28) — 마지막 문이 닫히는 순간까지 기다렸다가 센다(쾅이면 0.2초, 천천히면 1초)
        yield return new WaitForSeconds(1.2f);
        Door.Shut -= onShut;
        PT($"'쿵' 신호 {shutSignals}회 — 이동 {route.Length}번(이중문은 짝마다 한 번씩 온다)");
        if (shutSignals == 0)
            findings.Add("지나온 문이 닫혔는데 '쿵' 신호(Door.Shut)가 한 번도 안 났다 — 문 소리가 안 난다");

        if (audit != null)
        {
            PT($"은폐 검사 — 검사 {audit.CheckCount}회, 위반 {audit.ViolationCount}회, 마지막 위반: {audit.LastViolation ?? "없음"}");
            if (audit.ViolationCount > 0) findings.Add($"은폐 위반 {audit.ViolationCount}회 — {audit.LastViolation}");
        }

        Report("B 사방 이동");
    }

    // ──────────────────────────────────────────────────────────────
    // 3. 판정 · 진행도 · 문 잠금
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator C_판정과_진행도()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        var gm = GameManager.Instance;
        var grid = GridManager.Instance;

        // 판정 전에는 문이 잠겨 있어야 한다 — E를 눌러도 안 열린다
        var doors = DoorsBetween(grid.CenterCoord, N);
        if (doors.Length == 0) findings.Add("북쪽 경계에 문짝이 등록돼 있지 않다");
        foreach (var d in doors) if (d != null) d.Interact();
        yield return new WaitForSeconds(0.3f);
        foreach (var d in doors)
            if (d != null && d.IsOpen) findings.Add("판정 전인데 문이 열렸다 (잠금이 안 걸렸다)");
        PT($"판정 전 문 상태 — 잠김 확인, 안내문 '{(doors.Length > 0 && doors[0] != null ? doors[0].Prompt : "?")}'");

        // 정답을 계속 맞춰 클리어까지
        for (int i = 0; i < gm.ClearGoal; i++)
        {
            int before = gm.Progress;
            bool truth = gm.CurrentRoomHasAnomaly;
            yield return JudgeCurrent(truth);          // 진짜 유무대로 = 정답

            if (gm.Progress != before + 1)
                findings.Add($"정답인데 진행도가 {before} → {gm.Progress} (기대 {before + 1})");

            PT($"판정 {i + 1}회 — 이번 칸 {grid.CenterCell.Id}번 '{grid.CenterCell.AnomalyLabel}', 정답 처리, 진행도 {gm.Progress}/{gm.ClearGoal}");

            if (gm.Progress >= gm.ClearGoal) break;

            yield return Cross(i % 2 == 0 ? N : E);     // 직진·꺾기 섞어서
        }

        if (!gm.IsCleared) findings.Add($"{gm.ClearGoal}번 맞췄는데 클리어가 아니다 (진행도 {gm.Progress})");
        PT($"클리어 여부 {gm.IsCleared} · 진행도 {gm.Progress}/{gm.ClearGoal}");

        // 클리어 후에도 문이 풀려서 계속 돌아다닐 수 있어야 한다 (갇히면 안 된다)
        yield return Cross(N);
        PT($"클리어 후에도 이동 가능 — 좌표 {grid.CenterCoord}");

        // 오답은 진행도를 0으로
        bool truth2 = gm.CurrentRoomHasAnomaly;
        yield return JudgeCurrent(!truth2);
        if (gm.Progress != 0) findings.Add($"오답인데 진행도가 {gm.Progress}");
        PT($"오답 처리 — 진행도 {gm.Progress}, 누적 오답 {gm.Mistakes}");

        Report("C 판정과 진행도");
    }

    // ──────────────────────────────────────────────────────────────
    // 4. 이상현상 재추첨 — 같은 칸이 늘 같은 것만 내놓는가 / 눈앞에서 바뀌는가
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator D_이상현상_재추첨()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        var grid = GridManager.Instance;

        // ① 서 있는 동안 이번 칸의 이상현상이 바뀌면 안 된다 (판정 중에 정답이 변한다)
        yield return JudgeCurrent();
        yield return Cross(N);

        var cell = grid.CenterCell;
        string label = cell.AnomalyLabel;
        bool has = cell.HasAnomaly;
        float t = 0f;
        while (t < 3f)
        {
            t += Time.deltaTime;
            if (cell.AnomalyLabel != label || cell.HasAnomaly != has)
            {
                findings.Add($"서 있는 동안 이번 칸 이상현상이 바뀌었다: '{label}' → '{cell.AnomalyLabel}'");
                break;
            }
            yield return null;
        }
        PT($"서 있는 3초 동안 이번 칸 '{label}' 유지 여부: {(cell.AnomalyLabel == label ? "유지" : "바뀜")}");

        // ② 같은 칸을 여러 번 드나들며 — 늘 같은 것만 나오면 외워서 판정이 공짜가 된다
        var seen = new Dictionary<int, List<string>>();
        for (int i = 0; i < 6; i++)
        {
            yield return JudgeCurrent();
            yield return Cross(i % 2 == 0 ? S : N);

            var c = grid.CenterCell;
            if (!seen.TryGetValue(c.Id, out var list)) seen[c.Id] = list = new List<string>();
            list.Add(c.AnomalyLabel);
        }

        foreach (var kv in seen)
            PT($"{kv.Key}번 칸을 {kv.Value.Count}번 방문 — 이상현상 기록: {string.Join(" / ", kv.Value)}");

        Report("D 이상현상 재추첨");
    }

    // ──────────────────────────────────────────────────────────────
    // 5. 추격자 — 3번 틀리면 바로 다음 칸에 (직진)
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator E_추격자_직진_등장과_추격()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        yield return MakeMistakes(3, N);

        int marked = MarkedCells();
        PT($"3번 틀린 직후 — 누적 오답 {GameManager.Instance.Mistakes}/{GameManager.Instance.MistakesBeforeStalker}, " +
           $"'그것' 표시가 걸린 칸 {marked}개, 추격자 활성 {GameManager.Instance.Stalker.IsActive}");
        if (marked == 0) findings.Add("3번 틀렸는데 어느 칸에도 '그것' 표시가 없다");

        yield return Cross(N);

        var gm = GameManager.Instance;
        var grid = GridManager.Instance;
        var stalker = gm.Stalker;

        PT($"다음 칸({grid.CenterCell.Id}번) 진입 — 이상현상 '{grid.CenterCell.AnomalyLabel}', 추격자 활성 {stalker.IsActive}");
        PT($"추격자 상태: {stalker.DebugState}");

        if (!stalker.IsActive) findings.Add("3번 틀리고 다음 칸에 들어왔는데 추격자가 나오지 않았다");
        if (!grid.CenterCell.StalkerIsAnomaly) findings.Add("들어선 칸의 이상현상이 '그것'이 아니다");
        if (grid.GetCoordAt(stalker.transform.position) != grid.CenterCoord)
            findings.Add($"추격자가 다른 칸에 서 있다 — 추격자 {grid.GetCoordAt(stalker.transform.position)}, 플레이어 {grid.CenterCoord}");
        if (MarkedCells() != 1) findings.Add($"'그것' 표시가 {MarkedCells()}개 칸에 남아 있다 (씬의 추격자는 한 마리)");
        if (gm.Mistakes != 0) findings.Add($"추격자가 도착했는데 누적 오답이 {gm.Mistakes}로 남았다");

        // ① 쳐다보면 멈춘다
        // 수정 (2026-10-28): 먼저 '그것'이 화면에 실제로 보이는 자리로 옮긴다. 등장 자리는 섬 너머 구석이라
        // 들어선 문간에서는 섬에 가려 안 보일 수 있다 — 거기서는 다가오는 게 맞는 동작이다(안 보면 다가온다).
        // 예전에 문간에서 그대로 통과했던 건 시야 판정 표본점이 몸 10m 앞 허공에 있었기 때문이다(L 참고).
        if (TryFindVisibleSpot(stalker, out Vector3 vantage, out int vantagePixels))
        {
            playerRoot.position = vantage;
            Physics.SyncTransforms();
            PT($"'그것'이 화면에 보이는 자리로 옮김 — {vantage}, 화면에 {vantagePixels}화소");
        }
        else findings.Add("칸 어디에서도 '그것'이 화면에 뚜렷이 보이는 자리를 못 찾았다");
        Face(stalker.transform.position);
        yield return null;
        Snap("E-그것-등장-쳐다봄");
        Vector3 frozenAt = stalker.transform.position;
        float watched = 0f;
        while (watched < 2f) { watched += Time.deltaTime; yield return null; }
        float drift = Vector3.Distance(frozenAt, stalker.transform.position);
        PT($"쳐다본 2초 — 움직인 거리 {drift:F3}m · {stalker.DebugState}");
        if (drift > 0.05f) findings.Add($"쳐다보는 동안 추격자가 {drift:F2}m 움직였다 (완전히 멈춰야 한다)");

        // ② 등을 돌리면 다가온다 → 잡힌다
        int progressBefore = gm.Progress;
        Face(stalker.transform.position - (stalker.transform.position - playerRoot.position) * 2f); // 반대쪽
        float start = Vector3.Distance(playerRoot.position, stalker.transform.position);
        float timeout = 0f;
        while (timeout < 20f && stalker.IsActive)
        {
            timeout += Time.deltaTime;
            yield return null;
        }
        float end = Vector3.Distance(playerRoot.position, stalker.transform.position);
        PT($"등 돌린 뒤 {timeout:F1}초 — 거리 {start:F1}m → {end:F1}m, 추격자 활성 {stalker.IsActive}, 진행도 {progressBefore}→{gm.Progress}");

        if (stalker.IsActive) findings.Add($"등을 돌리고 20초를 기다렸는데 안 잡혔다 (거리 {start:F1}m → {end:F1}m)");
        else if (gm.Progress != 0) findings.Add($"잡혔는데 진행도가 0이 아니다: {gm.Progress}");

        // 잡힌 뒤 이 칸의 정답은 어떻게 되는가 — 추격자는 사라졌는데 '있음'으로 남아 있으면
        // 관찰로는 알 수 없는 판정이 된다 (빈 방을 보고 O를 눌러야 맞는 상황).
        var caughtCell = grid.CenterCell;
        PT($"잡힌 뒤 이 칸 상태 — 이상현상 '{caughtCell.AnomalyLabel}', 있음 여부 {caughtCell.HasAnomaly}, " +
           $"그것 표시 {caughtCell.StalkerIsAnomaly}, 추격자 활성 {stalker.IsActive}, 판정 완료 {gm.HasJudged}");
        if (!stalker.IsActive && caughtCell.HasAnomaly)
            findings.Add("잡힌 뒤에도 이 칸 정답은 '있음'인데 추격자는 사라져 있다 — " +
                         "빈 방을 보고 O를 눌러야 맞는 판정이 남는다");

        // 사라진 뒤 몸통(충돌 캡슐)은? 렌더러만 끄고 CharacterController를 켜둔 채면 보이지 않는 기둥이 남는다.
        var stalkerCapsule = stalker.GetComponent<CharacterController>();
        if (!stalker.IsActive && stalkerCapsule != null)
        {
            float gap = Vector3.Distance(playerRoot.position, stalker.transform.position);
            PT($"사라진 뒤 '그것'의 충돌 캡슐 — 켜짐 {stalkerCapsule.enabled}, 위치 {stalker.transform.position}, 플레이어까지 {gap:F1}m");
            if (stalkerCapsule.enabled)
                findings.Add($"사라진 뒤에도 '그것'의 충돌 캡슐이 켜진 채 플레이어 {gap:F1}m 앞에 남는다 — 보이지 않는 기둥");
        }

        // 잡힌 뒤엔 방이 비어 보인다 — 보이는 대로 X(없음)를 누르면 정답이어야 한다
        if (!stalker.IsActive && !gm.HasJudged)
        {
            int beforeX = gm.Progress;
            yield return JudgeCurrent(false);
            PT($"잡힌 뒤 빈 방을 보고 X 판정 — 진행도 {beforeX} → {gm.Progress}");
            if (gm.Progress != beforeX + 1)
                findings.Add($"잡힌 뒤 빈 방을 보고 X를 눌렀는데 정답 처리가 안 됐다 (진행도 {beforeX} → {gm.Progress})");
        }

        Report("E 추격자 직진 등장과 추격");
    }

    // ──────────────────────────────────────────────────────────────
    // 6. 추격자 — 꺾어 걸어도 나오는가 (겪은 버그: 영영 안 나왔다)
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator F_추격자_꺾어걸어도_등장()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        yield return MakeMistakes(3, N);     // 북쪽으로 걸어오며 틀렸다
        yield return Cross(E);               // 그런데 이번엔 동쪽으로 꺾는다

        var gm = GameManager.Instance;
        var grid = GridManager.Instance;

        PT($"꺾어 걸은 뒤 — 칸 {grid.CenterCell.Id}번 '{grid.CenterCell.AnomalyLabel}', " +
           $"추격자 활성 {gm.Stalker.IsActive}, 누적 오답 {gm.Mistakes}, 표시된 칸 {MarkedCells()}개");
        PT($"추격자 상태: {gm.Stalker.DebugState}");

        if (!gm.Stalker.IsActive) findings.Add("꺾어 걸었더니 추격자가 나오지 않았다");
        if (grid.GetCoordAt(gm.Stalker.transform.position) != grid.CenterCoord)
            findings.Add("꺾어 걸었더니 추격자가 다른 칸에 섰다");
        if (gm.Mistakes != 0) findings.Add($"누적 오답이 {gm.Mistakes}로 남아 다음 칸에 또 심긴다");

        Report("F 추격자 꺾어걷기");
    }

    // ──────────────────────────────────────────────────────────────
    // 7. 판정 패널이 열려 있는 동안 추격자가 멈추는가
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator G_판정_중에는_추격자가_멈춘다()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        yield return MakeMistakes(3, N);
        yield return Cross(N);

        var gm = GameManager.Instance;
        var stalker = gm.Stalker;
        if (!stalker.IsActive)
        {
            findings.Add("추격자가 안 나와서 '판정 중 정지'를 확인할 수 없었다");
            Report("G 판정 중 추격자 정지");
            yield break;
        }

        // 등을 돌린다 — 패널만 없으면 다가올 상황을 만든다
        Face(playerRoot.position * 2f - stalker.transform.position);
        yield return null;

        var panel = GridManager.Instance.CenterCell.GetComponentInChildren<JudgmentPanel>(true);
        Assert.IsNotNull(panel, "칸에 판정 키패드(JudgmentPanel)가 없다");
        panel.Interact();
        yield return null;
        if (JudgmentUI.Instance == null || !JudgmentUI.Instance.IsOpen)
            findings.Add("키패드를 눌렀는데 판정 패널이 열리지 않았다");

        Vector3 at = stalker.transform.position;
        float t = 0f;
        while (t < 2.5f) { t += Time.deltaTime; yield return null; }
        float moved = Vector3.Distance(at, stalker.transform.position);
        PT($"판정 패널 열어둔 2.5초 — 추격자가 움직인 거리 {moved:F3}m");
        if (moved > 0.05f) findings.Add($"판정 패널이 열려 있는데 추격자가 {moved:F2}m 다가왔다");

        // 패널을 닫으면 다시 움직여야 한다
        JudgmentUI.Instance.OnClickAnomaly();     // '있음'(=그것) 판정
        yield return null;
        Vector3 after = stalker.transform.position;
        t = 0f;
        while (t < 2.5f && stalker.IsActive) { t += Time.deltaTime; yield return null; }
        float moved2 = Vector3.Distance(after, stalker.transform.position);
        PT($"판정 후 2.5초 — 추격자가 움직인 거리 {moved2:F3}m, 진행도 {gm.Progress}, 활성 {stalker.IsActive}");
        if (moved2 < 0.05f && stalker.IsActive)
            findings.Add("판정을 끝냈는데도 추격자가 움직이지 않는다");

        Report("G 판정 중 추격자 정지");
    }

    // ──────────────────────────────────────────────────────────────
    // 8. 열어보고 다른 쪽으로 나가면, 쓰지 않은 문은 어떻게 되는가
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    // 추가 생성 (2026-10-28) — 에디터 Test Runner는 실제 속도로 돌아 기본 제한 3분을 넘긴다(10/27 실패 원인).
    // 제한에 걸리면 걷던 도중에 걸음이 끊겨, 걸음 실패가 아닌데도 '이동 실패'처럼 보이는 기록이 남는다. 10분까지 기다린다.
    [Timeout(600000)]
    public IEnumerator H_쓰지_않고_열어둔_문()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        var grid = GridManager.Instance;

        // ① 판정하고 북·동 두 문을 다 연다 — 열어보고 다른 쪽으로 가는 흔한 상황
        yield return JudgeCurrent();
        yield return OpenDoorsToward(N);
        yield return OpenDoorsToward(E);
        int aId = grid.CenterCell.Id;
        PT($"{aId}번 칸에서 북·동 문을 다 열었다 — 열린 문: {string.Join(", ", OpenDoorList())}");

        // ② 동쪽으로만 나간다 (북문은 쓰지 않았다)
        yield return Walk(E);
        yield return new WaitForSeconds(1.8f);          // 지나온 문이 닫힐 시간

        var stillOpen = OpenDoorList();
        PT($"동쪽으로 나온 뒤 아직 열려 있는 문: {(stillOpen.Count == 0 ? "없음" : string.Join(", ", stillOpen))}");
        if (stillOpen.Count > 0)
            findings.Add($"쓰지 않고 열어둔 문이 그대로 열려 있다 ({string.Join(", ", stillOpen)}) — " +
                         "'방금 지나온 문만 열려 있다'는 은폐 전제가 깨진다");

        // ③ 그 열린 문이 있는 칸으로 돌아가서, 판정하지 않고 나갈 수 있는지 본다
        yield return JudgeCurrent();
        yield return Cross(N);
        yield return JudgeCurrent();
        yield return Cross(W);          // 여기가 ①번 칸의 북쪽 칸이다

        var southDoors = DoorsBetween(grid.CenterCoord, S);
        bool southOpen = false;
        foreach (var d in southDoors) if (d != null && d.IsOpen) southOpen = true;
        PT($"{grid.CenterCell.Id}번 칸 도착 — 남쪽 문 열림 {southOpen}, 판정 완료 {GameManager.Instance.HasJudged}");

        Vector2Int before = grid.CenterCoord;
        yield return Walk(S, false, false);    // 판정하지 않고 남쪽으로 걸어본다 (막히는 것이 정상)

        bool movedWithoutJudging = grid.CenterCoord == before + S;
        PT($"판정하지 않은 채 남쪽으로 걸은 결과 — 중앙 {before} → {grid.CenterCoord}");
        if (movedWithoutJudging)
            findings.Add("판정하지 않은 칸에서 그대로 걸어 나갔다 — 열린 채 남은 문으로 판정 관문이 뚫린다");

        Report("H 쓰지 않고 열어둔 문");
    }

    // ──────────────────────────────────────────────────────────────
    // 9. 돌아보며 걸어도 재활용이 안 보이는가 (이 포폴의 핵심 주장)
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator I_돌아보며_걸어도_안_보이는가()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다
        // 호러에서 흔한 뒷걸음질 — 온 쪽을 계속 보면서 다음 칸으로 넘어간다.
        // 재활용되는 줄은 늘 '뒤'에 있으니, 이 걸음이 은폐 규칙을 가장 세게 때린다.
        for (int i = 0; i < 4; i++)
        {
            yield return JudgeCurrent();
            yield return Cross(N, true);

            var grid = GridManager.Instance;
            PT($"{i + 1}번째 뒷걸음질 — 중앙 {grid.CenterCoord}, 칸 {grid.CenterCell.Id}번, " +
               $"은폐 검사 {(audit != null ? audit.CheckCount : 0)}회 / 위반 {(audit != null ? audit.ViolationCount : 0)}회");

            CheckGridInvariants($"{i + 1}번째 뒷걸음질 후");
        }

        if (audit != null)
            PT($"뒷걸음질 4회 종합 — 은폐 검사 {audit.CheckCount}회, 위반 {audit.ViolationCount}회, " +
               $"마지막 위반: {audit.LastViolation ?? "없음"}");

        Report("I 돌아보며 걷기");
    }

    // ──────────────────────────────────────────────────────────────
    // 10. 무작위 산책 — 판마다 다른 경로·다른 판정으로 걸어서, 정해진 시나리오가 못 밟는 곳을 밟는다
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    // 추가 생성 (2026-10-28) — H와 같은 이유. 14걸음을 실제 속도로 걸으면 3분을 넘긴다.
    [Timeout(600000)]
    public IEnumerator J_무작위_산책()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다

        int seed = System.Environment.TickCount;
        var rng = new System.Random(seed);
        PT($"무작위 산책 시작 — 씨앗 {seed} (같은 씨앗이면 같은 경로)");

        var gm = GameManager.Instance;
        var grid = GridManager.Instance;
        var dirs = new[] { N, E, S, W };
        int meetings = 0;
        int caughtBefore = caughtCount;

        for (int step = 0; step < 14; step++)
        {
            // 70%는 제대로 맞히고 30%는 틀린다 — 그래야 '그것'까지 간다
            bool truth = gm.CurrentRoomHasAnomaly;
            yield return JudgeCurrent(rng.NextDouble() < 0.7 ? truth : !truth);

            var dir = dirs[rng.Next(4)];
            Vector2Int before = grid.CenterCoord;
            yield return Cross(dir);

            if (grid.CenterCoord != before + dir)
            {
                PT($"[{step + 1}] {Name(dir)} 이동 실패 — 이번 산책은 여기서 멈춘다");
                break;
            }

            var cell = grid.CenterCell;
            var stalker = gm.Stalker;
            PT($"[{step + 1}] {Name(dir)} → {cell.Id}번 {cell.Coord} '{cell.AnomalyLabel}' · 진행도 {gm.Progress} · " +
               $"누적 오답 {gm.Mistakes} · 그것 {(stalker.IsActive ? "있음" : "-")}");
            CheckGridInvariants($"산책 {step + 1}걸음");

            if (cell.StalkerIsAnomaly != stalker.IsActive)
                findings.Add($"산책 {step + 1}걸음: 칸 표시('그것' {cell.StalkerIsAnomaly})와 실제 추격자(활성 {stalker.IsActive})가 어긋난다");

            if (stalker.IsActive)
            {
                meetings++;
                if (grid.GetCoordAt(stalker.transform.position) != grid.CenterCoord)
                    findings.Add($"산책 {step + 1}걸음: 추격자가 플레이어와 다른 칸에 섰다");

                yield return JudgeCurrent(true);   // 관찰하면 '있음'이 정답 — 다음 걸음에서 판정은 건너뛴다
            }
        }

        PT($"무작위 산책 끝 — 씨앗 {seed}, '그것' 마주침 {meetings}회, 잡힘 {caughtCount - caughtBefore}회, " +
           $"은폐 위반 {(audit != null ? audit.ViolationCount : 0)}회");
        Report($"J 무작위 산책 (씨앗 {seed})");
    }

    // ──────────────────────────────────────────────────────────────
    // 11. 문짝 없는 문간 — 문짝은 북·동에만 달려 있어서, 격자 끝 줄의 남·서 경계는 채워줄 이웃이 없다
    // ──────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator K_문짝_없는_문간()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 걸어본 뒤 한꺼번에 보고한다

        var grid = GridManager.Instance;

        // ① 격자 전체에서 '등록된 문짝'이 없는 경계 — 격자 끝이라 이웃이 채워주지 못하는 곳.
        //    여기는 막이 문(GridCell.SetEdgeFillers)이 막고 있어야 하므로, 실제로 뚫렸는지를 따로 센다.
        var doorless = new List<string>();
        var holes = new List<string>();
        foreach (var kv in grid.Cells)
            if (kv.Value != null)
                foreach (var dir in new[] { N, E, S, W })
                    if (CountDoors(DoorsBetween(kv.Key, dir)) == 0)
                    {
                        doorless.Add($"{kv.Value.Id}번 {Name(dir)}(막이 문 {FillerState(kv.Value, dir)})");
                        if (IsPhysicallyOpen(kv.Key, dir)) holes.Add($"{kv.Value.Id}번 {Name(dir)}");
                    }
        PT($"등록된 문짝이 없는 경계 {doorless.Count}개 · 그중 실제로 뚫린 곳 {holes.Count}개" +
           (holes.Count > 0 ? $": {string.Join(", ", holes)}" : ""));

        // ② 그중 '가운데 칸의 문 하나만 열면 보이는' 것 — 이웃 칸의 바깥쪽 경계
        var visible = new List<string>();
        foreach (var dir in new[] { N, E, S, W })
            if (IsPhysicallyOpen(grid.CenterCoord + dir, dir)) visible.Add($"{Name(dir)}쪽 이웃의 {Name(dir)} 문간");
        PT($"가운데 칸에서 문 하나만 열면 보이는 뚫린 문간: {(visible.Count == 0 ? "없음" : string.Join(", ", visible))}");
        if (visible.Count > 0)
            findings.Add($"가운데 칸에서 문 하나만 열어도 그 너머가 문짝 없이 뚫려 있다 ({string.Join(", ", visible)}) — 빈 공간이 보인다");

        // ③ 실제로 남쪽 문을 열고 바라본다
        yield return JudgeCurrent();
        yield return OpenDoorsToward(S);
        Face(playerRoot.position + Vector3.back * 10f);
        yield return null;
        Snap("K-남쪽-문-열고-바라봄");

        // ④ 북쪽으로 한 칸 간 뒤 돌아본다 — 방금 재활용으로 치워진 줄 쪽
        yield return Cross(N);
        yield return new WaitForSeconds(1.6f);    // 지나온 문이 닫힐 때까지
        Face(playerRoot.position + Vector3.back * 10f);
        yield return null;
        Snap("K-북으로-한칸-뒤-돌아봄-문닫힘");

        yield return JudgeCurrent();
        yield return OpenDoorsToward(S);
        yield return null;
        Snap("K-북으로-한칸-뒤-남쪽-문-열고-돌아봄");

        // ⑤ 방금 재활용으로 치워진 줄 쪽 — 돌아본 칸의 남쪽 경계가 실제로 막혀 있는가
        Vector2Int behind = grid.CenterCoord + S;
        bool behindHole = IsPhysicallyOpen(behind, S);
        PT($"돌아본 칸 {behind}의 남쪽 경계(재활용으로 치워진 줄 쪽): {(behindHole ? "뚫려 있다" : "막혀 있다")}");
        if (behindHole)
            findings.Add($"재활용으로 치워진 줄 쪽 경계 {behind} 남쪽이 뚫린 채 남는다 — 돌아보면 빈 공간이 보인다");

        // 뚫린 경계를 피신 판정이 '닫힘'으로 보면 판정과 실제가 어긋난다
        if (behindHole && grid.AreBoundaryDoorsClosed(behind, behind + S))
            findings.Add("뚫린 경계를 피신 판정 AreBoundaryDoorsClosed는 '닫힘'으로 본다");

        Report("K 문짝 없는 문간");
    }

    // ──────────────────────────────────────────────────────────────
    // 12. 보는 동안 다가오는가 — '보면 멈춘다' 판정이 실제 화면과 맞는가
    // ──────────────────────────────────────────────────────────────

    private const int VisiblePixels = 15;   // 비교용 화면(가로 320)에 이만큼 넘게 비치면 '보인다'로 친다

    /// <summary>
    /// 추가 생성 (2026-10-28) — 사용자 제보 "쳐다보고 있는데 가끔 그냥 다가온다"를 숫자로 확인한다.
    ///
    /// 추격자가 멈출지 정하는 판정(<c>Stalker.IsSeenByPlayer</c>)과 <b>실제 화면</b>을 칸 곳곳에서 비교한다.
    /// 실제 화면은 렌더링으로 잰다 — 추격자만 무광 자홍색으로 바꿔 찍고 그 색 화소를 센다. 판정은
    /// 선(Linecast)을 긋지만 눈은 화면을 보므로, 유리창처럼 선은 막고 눈에는 비치는 것이 그대로 드러난다.
    ///
    ///  • 화면에 보이는데 '안 보임' 판정 → 그 자리에서 쳐다보는 동안 다가온다 (제보된 버그)
    ///  • 화면에 없는데 '보임' 판정      → 가려져 있는데 멈춘다 (덜 무섭지만 무해 — 세기만 한다)
    ///
    /// 비교하는 동안 추격자의 Update는 멈춰 둔다(움직이거나 잡으면 비교가 안 된다). 판정 함수는 직접 부른다.
    /// 추격자는 실제 등장 자리 + 방 안 여덟 곳에 세우고, 플레이어는 칸 바닥을 1m 눈금으로 훑는다.
    /// 문을 다 닫은 판과 다 연 판(열린 문짝 유리창이 시선에 끼는 경우)을 따로 잰다.
    /// </summary>
    [UnityTest]
    [Timeout(600000)]
    public IEnumerator L_보는_동안_다가오는가()
    {
        LogAssert.ignoreFailingMessages = true;   // 에러 로그가 나와도 끝까지 재본 뒤 한꺼번에 보고한다
        yield return MakeMistakes(3, N);
        yield return Cross(N);

        var gm = GameManager.Instance;
        var grid = GridManager.Instance;
        var stalker = gm.Stalker;
        var flatShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!stalker.IsActive || flatShader == null)
        {
            findings.Add($"비교를 시작하지 못했다 — 추격자 활성 {stalker.IsActive}, 무광 셰이더 {(flatShader != null ? "있음" : "없음")}");
            Report("L 보는 동안 다가오는가");
            yield break;
        }

        const System.Reflection.BindingFlags Private =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var isSeen = typeof(Stalker).GetMethod("IsSeenByPlayer", Private);
        var ensureRefs = typeof(Stalker).GetMethod("EnsureReferences", Private);
        var onScreenField = typeof(Stalker).GetField("lastOnScreen", Private);
        var blockerField = typeof(Stalker).GetField("lastBlocker", Private);

        stalker.enabled = false;            // 재는 동안 움직이거나 잡지 않게
        ensureRefs.Invoke(stalker, null);   // 판정이 쓰는 카메라·플레이어 참조를 채워 둔다

        var cam = Camera.main;
        var body = stalker.GetComponentsInChildren<Renderer>(true);
        var stalkerBody = stalker.GetComponent<CharacterController>();
        // 표본점이 몸 위에 있는지 먼저 본다 — 판정은 표본점까지 선을 긋는 것이라, 몸에서 떨어져 있으면
        // 몸이 아니라 엉뚱한 허공이 보이는지를 재게 된다(2026-10-28 실제 원인: 표본점이 몸 10.37m 앞에 있었다).
        var points = (Transform[])typeof(Stalker).GetField("samplePoints", Private).GetValue(stalker);
        Bounds bodyBounds = body[0].bounds;
        foreach (var r in body) bodyBounds.Encapsulate(r.bounds);
        Vector3 root = stalker.transform.position;
        var outside = new List<string>();
        var heights = new List<string>();
        foreach (var t in points)
        {
            if (t == null) continue;
            float off = Mathf.Sqrt(bodyBounds.SqrDistance(t.position));
            heights.Add($"{t.name} {t.position.y - root.y:F2}m");
            if (off > 0.3f) outside.Add($"{t.name} {off:F1}m");
        }
        Vector3 centerLocal = stalker.transform.InverseTransformPoint(bodyBounds.center);
        PT($"추격자 몸 경계 — 발 기준 높이 {bodyBounds.min.y - root.y:F2}~{bodyBounds.max.y - root.y:F2}m, " +
           $"폭 {bodyBounds.size.x:F2}×{bodyBounds.size.z:F2}m, 중심(추격자 기준) {centerLocal} · " +
           $"표본점 높이 [{string.Join(", ", heights)}] · 몸 밖 표본점: {(outside.Count == 0 ? "없음" : string.Join(", ", outside))}");
        if (outside.Count > 0)
            findings.Add($"시야 판정 표본점이 몸 밖에 있다 ({string.Join(", ", outside)}) — 몸이 아닌 허공까지 선을 긋는다");

        var magenta = new Material(flatShader);
        magenta.SetColor("_BaseColor", Color.magenta);
        magenta.SetColor("_Color", Color.magenta);

        // 비교용 화면은 카메라 화면비 그대로 — 다르면 화면 가장자리에서 '화면 밖' 판정과 어긋난다
        int shotW = 320, shotH = Mathf.Max(1, Mathf.RoundToInt(320 / cam.aspect));
        var shot = new Texture2D(shotW, shotH, TextureFormat.RGB24, false);

        var cell = grid.CenterCell;
        Vector3 playerStart = playerRoot.position;
        Quaternion playerStartRot = playerRoot.rotation;
        Vector3 stalkerStart = stalker.transform.position;

        // 바탕에 원래 자홍색이 있는지 — 있으면 화소 세기가 틀린다
        foreach (var r in body) r.enabled = false;
        int baseline = CountFlatPixels(cam, body, magenta, shot);
        foreach (var r in body) r.enabled = true;

        // 추격자를 세워 볼 자리 — 실제 등장 자리 + 방 안 여덟 곳(칸 기준 좌표)
        var stalkerSpots = new List<Vector3> { stalkerStart };
        foreach (var o in new[] { new Vector2(6, 6), new Vector2(-6, 6), new Vector2(6, -6), new Vector2(-6, -6),
                                  new Vector2(0, 6), new Vector2(0, -6), new Vector2(6, 0), new Vector2(-6, 0) })
        {
            Vector3 p = cell.transform.TransformPoint(new Vector3(o.x, 0f, o.y));
            p.y = stalkerStart.y;
            stalkerSpots.Add(p);
        }

        int samples = 0, visibleCount = 0, frozenHidden = 0, snaps = 0;
        var missed = new List<(int pixels, string where, string why)>();
        var reasons = new Dictionary<string, int>();

        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                // 두 번째 판: 이 칸의 문을 전부 연다 — 열린 문짝(유리창 달림)이 시선에 끼는 경우
                foreach (var dir in new[] { N, E, S, W })
                    foreach (var d in DoorsBetween(grid.CenterCoord, dir))
                        if (d != null) d.Open();
                yield return new WaitForSeconds(DoorWait);
            }
            string doors = pass == 0 ? "문 닫힘" : "문 열림";

            for (int s = 0; s < stalkerSpots.Count; s++)
            {
                stalker.transform.position = stalkerSpots[s];
                Physics.SyncTransforms();
                if (s > 0 && BodyBlocked(stalkerSpots[s], stalker.transform.rotation, stalkerBody, stalkerBody)) continue;   // 섬·벽 속이면 설 수 없는 자리

                for (int x = -9; x <= 9; x++)
                for (int z = -9; z <= 9; z++)
                {
                    if (NearDoorwayTrigger(x, z)) continue;
                    Vector3 p = cell.transform.TransformPoint(new Vector3(x, 0f, z));
                    p.y = playerStart.y;

                    Vector3 gap = stalkerSpots[s] - p;
                    gap.y = 0f;
                    if (gap.magnitude < 2f) continue;   // 잡히는 거리 언저리는 비교할 필요가 없다
                    // 캡슐 중심이 발밑이 아니라 (0.5, 1, -0.6)에 있어서, 바라보는 방향으로 돌린 자세로 재야 한다
                    if (BodyBlocked(p, Quaternion.LookRotation(gap, Vector3.up), playerBody, stalkerBody)) continue;   // 벽·섬·소품 속이면 설 수 없는 자리

                    playerRoot.position = p;
                    Physics.SyncTransforms();
                    Face(stalkerSpots[s]);

                    bool judged = (bool)isSeen.Invoke(stalker, null);
                    int pixels = CountFlatPixels(cam, body, magenta, shot);
                    samples++;

                    if (pixels >= VisiblePixels)
                    {
                        visibleCount++;
                        if (!judged)
                        {
                            bool onScreen = (bool)onScreenField.GetValue(stalker);
                            string why = onScreen ? $"가림 — {blockerField.GetValue(stalker) ?? "?"}" : "화면 밖 판정";
                            reasons[why] = reasons.TryGetValue(why, out int n) ? n + 1 : 1;
                            missed.Add((pixels, $"{doors} · 그것 자리 {s} · 플레이어 칸 좌표 ({x}, {z}) · 거리 {gap.magnitude:F1}m", why));
                            if (pixels >= 200 && snaps < 3) { Snap($"L-보이는데-다가옴-{++snaps}"); }
                        }
                    }
                    else if (judged && pixels == 0) frozenHidden++;

                    if (samples % 25 == 0) yield return null;   // 한 프레임에 너무 오래 머물지 않게
                }
            }
        }

        playerRoot.SetPositionAndRotation(playerStart, playerStartRot);
        stalker.transform.position = stalkerStart;
        Physics.SyncTransforms();
        stalker.enabled = true;
        UnityEngine.Object.Destroy(magenta);
        UnityEngine.Object.Destroy(shot);

        int big = 0;
        foreach (var m in missed) if (m.pixels >= 200) big++;
        PT($"시야 비교 — 표본 {samples}곳(바탕 자홍 화소 {baseline}), 실제로 화면에 보인 곳 {visibleCount}, " +
           $"그중 '안 보임' 판정 = 쳐다보는데 다가오는 곳 {missed.Count}(뚜렷이 보이는 200화소 이상 {big}), " +
           $"화면에 없는데 '보임' 판정 {frozenHidden}");
        foreach (var kv in reasons) PT($"  · {kv.Key}: {kv.Value}곳");
        missed.Sort((a, b) => b.pixels.CompareTo(a.pixels));
        for (int i = 0; i < Mathf.Min(10, missed.Count); i++)
            PT($"  예 {i + 1}: 화소 {missed[i].pixels} · {missed[i].where} · {missed[i].why}");

        if (missed.Count > 0)
            findings.Add($"화면에 보이는데 '안 보임'으로 판정된 자리 {missed.Count}곳(뚜렷이 보이는 곳 {big}) — " +
                         "그 자리에서 쳐다보는 동안 다가온다");

        Report("L 보는 동안 다가오는가");
    }

    /// <summary>
    /// 추가 생성 (2026-10-28) — 지금 칸 바닥(1m 눈금)에서 '그것'이 화면에 뚜렷이(200화소 넘게) 보이는 자리 중
    /// 플레이어와 가장 가까운 곳을 찾는다. 판정 함수가 아니라 실제 화면(CountFlatPixels)으로 고른다 —
    /// 판정으로 고르면 판정이 틀렸을 때 그 틀림을 그대로 물려받는다. 찾는 동안 프레임은 넘기지 않는다.
    /// </summary>
    private bool TryFindVisibleSpot(Stalker stalker, out Vector3 spot, out int spotPixels)
    {
        spot = playerRoot.position;
        spotPixels = 0;
        var flatShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (flatShader == null) return false;

        var cam = Camera.main;
        var body = stalker.GetComponentsInChildren<Renderer>(true);
        var stalkerBody = stalker.GetComponent<CharacterController>();
        var magenta = new Material(flatShader);
        magenta.SetColor("_BaseColor", Color.magenta);
        magenta.SetColor("_Color", Color.magenta);
        var shot = new Texture2D(320, Mathf.Max(1, Mathf.RoundToInt(320 / cam.aspect)), TextureFormat.RGB24, false);

        var cell = GridManager.Instance.CenterCell;
        Vector3 start = playerRoot.position;
        Quaternion startRot = playerRoot.rotation;
        Vector3 target = stalker.transform.position;
        float best = float.MaxValue;
        bool found = false;

        for (int x = -9; x <= 9; x++)
        for (int z = -9; z <= 9; z++)
        {
            if (NearDoorwayTrigger(x, z)) continue;
            Vector3 p = cell.transform.TransformPoint(new Vector3(x, 0f, z));
            p.y = start.y;
            Vector3 gap = target - p;
            gap.y = 0f;
            if (gap.magnitude < 3f) continue;   // 너무 가까우면 쳐다보기 전에 잡힌다
            if (BodyBlocked(p, Quaternion.LookRotation(gap, Vector3.up), playerBody, stalkerBody)) continue;

            float travel = Vector3.Distance(p, start);
            if (travel >= best) continue;   // 이미 찾은 자리보다 멀면 찍어 볼 필요도 없다

            playerRoot.position = p;
            Physics.SyncTransforms();
            Face(target);
            int pixels = CountFlatPixels(cam, body, magenta, shot);
            if (pixels < 200) continue;

            best = travel;
            spot = p;
            spotPixels = pixels;
            found = true;
        }

        playerRoot.SetPositionAndRotation(start, startRot);
        Physics.SyncTransforms();
        UnityEngine.Object.Destroy(magenta);
        UnityEngine.Object.Destroy(shot);
        return found;
    }

    /// <summary>
    /// 문간 진입 트리거(문 평면에서 안쪽 2m · 폭 4m) 근처인지 — 칸 기준 정수 좌표.
    /// 순간이동으로 그 안에 들어갔다 나오면 칸 진입으로 착각하므로 아예 피한다.
    /// </summary>
    private static bool NearDoorwayTrigger(int x, int z) =>
        (Mathf.Abs(x) <= 3 && Mathf.Abs(z) >= 7) || (Mathf.Abs(z) <= 3 && Mathf.Abs(x) >= 7);

    /// <summary>
    /// 그 자리에 몸(캡슐)이 설 수 있는지 — 벽·섬·문·소품 속이면 못 선다. 플레이어 자신과
    /// 재는 중인 추격자 몸(<paramref name="ignore"/>)은 빼고 본다.
    /// </summary>
    private static bool BodyBlocked(Vector3 position, Quaternion rotation, CharacterController shape, CharacterController ignore)
    {
        Vector3 c = position + rotation * shape.center;   // 캡슐 중심은 몸 기준 좌표라 자세(회전)를 따라 돈다
        float half = Mathf.Max(shape.height * 0.5f - shape.radius, 0f);
        Vector3 top = c + Vector3.up * half;
        Vector3 bottom = c - Vector3.up * half + Vector3.up * 0.1f;   // 바닥에 닿는 건 막힘이 아니다
        int mask = ~(1 << LayerMask.NameToLayer("Player"));

        foreach (var hit in Physics.OverlapCapsule(bottom, top, shape.radius, mask, QueryTriggerInteraction.Ignore))
            if (hit != ignore && hit != shape) return true;
        return false;
    }

    /// <summary>
    /// 추격자만 무광 자홍색으로 바꿔 지금 카메라로 찍고, 그 색 화소를 센다 — '실제로 화면에 비쳤나'의 기준.
    /// 조명과 상관없이 몸이 화면에 남긴 자리만 센다(그림자는 자홍색이 아니다). 유리 너머로 비친 것도 센다.
    /// </summary>
    private static int CountFlatPixels(Camera cam, Renderer[] body, Material flat, Texture2D shot)
    {
        var saved = new Material[body.Length][];
        for (int i = 0; i < body.Length; i++)
        {
            saved[i] = body[i].sharedMaterials;
            var swap = new Material[saved[i].Length];
            for (int k = 0; k < swap.Length; k++) swap[k] = flat;
            body[i].sharedMaterials = swap;
        }

        var rt = RenderTexture.GetTemporary(shot.width, shot.height, 24);
        var previousTarget = cam.targetTexture;
        var previousActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previousTarget;
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0);
        shot.Apply(false);
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(rt);

        for (int i = 0; i < body.Length; i++) body[i].sharedMaterials = saved[i];

        // 자홍 = 빨강·파랑은 높고 초록은 낮다. 후처리·유리에 조금 물들어도 잡히게 넉넉히 본다.
        // 수천 번 부르므로 GetPixels32(매번 새 배열) 대신 텍스처 원본 바이트(RGB 3바이트씩)를 그대로 읽는다.
        var raw = shot.GetRawTextureData<byte>();
        int n = 0;
        for (int i = 0; i + 2 < raw.Length; i += 3)
            if (raw[i] - raw[i + 1] > 50 && raw[i + 2] - raw[i + 1] > 50) n++;
        return n;
    }

    /// <summary>
    /// 그 칸의 그 방향 문간이 <b>물리적으로</b> 뚫려 있는가 — 문간 한가운데를 눈높이쯤에서 가로질러
    /// 선을 그어, 문짝이든 막이 문이든 무엇에라도 걸리면 막힌 것이다.
    /// (등록된 문짝만 세면 막이 문처럼 규칙에 안 끼는 대역을 놓친다 — 그래서 실제 콜라이더로 본다.)
    /// </summary>
    private static bool IsPhysicallyOpen(Vector2Int coord, Vector2Int dir)
    {
        var grid = GridManager.Instance;
        if (!grid.Cells.TryGetValue(coord, out var cell) || cell == null) return false;

        var socket = cell.GetSocket(dir);
        if (socket == null) return false;

        Vector3 across = new Vector3(dir.x, 0f, dir.y);
        Vector3 side = new Vector3(-dir.y, 0f, dir.x);   // 문간 너비 방향
        Vector3 mid = socket.transform.position;
        mid.y = cell.transform.position.y + 1.2f;

        Physics.SyncTransforms();

        // 쌍여닫이 문은 정중앙이 두 문짝의 이음매라 선 하나로는 틈으로 빠져나간다(겪은 일 —
        // 멀쩡히 닫힌 문까지 '뚫림'으로 나왔다). 양쪽 문짝 가운데를 하나씩 긋고, 둘 다 빠지면 뚫린 것이다.
        foreach (float offset in new[] { -0.5f, 0.5f })
        {
            Vector3 p = mid + side * offset;
            if (Physics.Linecast(p - across, p + across, ~0, QueryTriggerInteraction.Ignore)) return false;
        }
        return true;
    }

    /// <summary>그 칸에 막이 문(GridCell.SetEdgeFillers)이 켜져 있는지 — 남·서만 있다.</summary>
    private static string FillerState(GridCell cell, Vector2Int dir)
    {
        string name = dir == S ? "EdgeFiller_S" : dir == W ? "EdgeFiller_W" : null;
        if (name == null) return "해당 없음";
        var filler = cell.transform.Find(name);
        return filler == null ? "없음" : filler.gameObject.activeSelf ? "켜짐" : "꺼짐";
    }

    private static int CountDoors(Door[] doors)
    {
        int n = 0;
        foreach (var d in doors) if (d != null) n++;
        return n;
    }

    /// <summary>
    /// 지금 플레이어 눈(Camera.main)에 보이는 장면을 PNG로 남긴다 — 헤드리스로 돌려도 "무엇이 보였나"를
    /// 사람이 나중에 확인할 수 있게. 워크트리의 <c>Logs/playtest-shots/run-판번호/</c>에 쌓인다(깃 무시 폴더).
    /// </summary>
    private void Snap(string label)
    {
        var cam = Camera.main;
        if (cam == null) return;

        string run = System.Environment.GetEnvironmentVariable("PLAYTEST_TAG") ?? "0";
        string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs", "playtest-shots", $"run-{run}");
        System.IO.Directory.CreateDirectory(dir);

        const int width = 960, height = 540;
        var rt = RenderTexture.GetTemporary(width, height, 24);
        var previousTarget = cam.targetTexture;
        var previousActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previousTarget;

        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(rt);

        string path = System.IO.Path.Combine(dir, $"{++shotIndex:000}-{label}.png");
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
        PT($"사진 저장 — {System.IO.Path.GetFileName(path)}");
    }

    // ──────────────────────────────────────────────────────────────
    // 플레이어 조작 — 사람이 키보드로 하던 것과 같은 경로를 부른다
    // ──────────────────────────────────────────────────────────────

    /// <summary>이번 칸을 관찰해 정답대로 판정한다 (실제 유무를 그대로 답한다).</summary>
    private IEnumerator JudgeCurrent()
    {
        yield return JudgeCurrent(GameManager.Instance.CurrentRoomHasAnomaly);
    }

    /// <summary>키패드를 눌러 판정 패널을 열고 O/X를 누른다.</summary>
    private IEnumerator JudgeCurrent(bool sayAnomaly)
    {
        var cell = GridManager.Instance.CenterCell;

        if (GameManager.Instance.HasJudged)
        {
            PT($"판정 건너뜀 — {(cell != null ? cell.Id : -1)}번 칸 {GridManager.Instance.CenterCoord}은 이미 판정됨");
            yield break;
        }

        bool truth = GameManager.Instance.CurrentRoomHasAnomaly;
        string label = cell != null ? cell.AnomalyLabel : "?";
        var panel = cell != null ? cell.GetComponentInChildren<JudgmentPanel>(true) : null;
        if (panel == null)
        {
            findings.Add($"{(cell != null ? cell.Id.ToString() : "?")}번 칸에 판정 키패드가 없다");
            GameManager.Instance.Judge(sayAnomaly);
            yield return null;
            yield break;
        }

        panel.Interact();
        yield return null;

        if (JudgmentUI.Instance == null)
        {
            findings.Add("씬에 JudgmentUI가 없다");
            yield break;
        }
        if (!JudgmentUI.Instance.IsOpen) findings.Add("키패드를 눌렀는데 판정 패널이 열리지 않았다");

        if (sayAnomaly) JudgmentUI.Instance.OnClickAnomaly();
        else JudgmentUI.Instance.OnClickNormal();
        yield return null;

        if (JudgmentUI.Instance.IsOpen) findings.Add("판정했는데 패널이 닫히지 않았다");

        var gm = GameManager.Instance;
        PT($"판정 — {cell.Id}번 칸 {cell.Coord} '{label}' · 답 {(sayAnomaly ? "O" : "X")} / 실제 {(truth ? "있음" : "없음")} " +
           $"({(sayAnomaly == truth ? "정답" : "오답")}) → 진행도 {gm.Progress}, 누적 오답 {gm.Mistakes}");
        if (!gm.HasJudged) findings.Add($"{cell.Id}번 칸에서 O/X를 눌렀는데 판정이 안 됐다");
    }

    /// <summary>문을 E로 열고 옆 칸으로 걸어 들어간다.</summary>
    private IEnumerator Cross(Vector2Int dir) { yield return Cross(dir, false); }

    private IEnumerator Cross(Vector2Int dir, bool lookBack)
    {
        yield return OpenDoorsToward(dir);
        yield return Walk(dir, true, lookBack);
    }

    private IEnumerator OpenDoorsToward(Vector2Int dir)
    {
        var doors = DoorsBetween(GridManager.Instance.CenterCoord, dir);
        if (doors.Length == 0)
        {
            findings.Add($"{Name(dir)} 경계에 문짝이 없다 — 나갈 수 없다");
            yield break;
        }

        // 이미 열린 문에 E를 또 누르면 <b>닫힌다</b>(Door.Interact는 토글). 닫힌 것만 연다.
        foreach (var d in doors) if (d != null && !d.IsOpen) d.Interact();
        yield return new WaitForSeconds(DoorWait);

        foreach (var d in doors)
            if (d != null && !d.IsOpen)
                findings.Add($"{Name(dir)} 문이 열리지 않았다 (안내문 '{d.Prompt}')");
    }

    /// <summary>
    /// 두 칸 사이 경계의 문짝들. 문짝은 북·동에만 달려 있어서, 남·서쪽으로 나갈 때는
    /// <b>이웃 칸이 들고 있는 반대쪽 문</b>을 찾아야 한다 (GridManager.GetBoundaryDoors와 같은 규칙).
    /// </summary>
    private static Door[] DoorsBetween(Vector2Int from, Vector2Int dir)
    {
        var cells = GridManager.Instance.Cells;

        if (dir == N || dir == E)
            return cells.TryGetValue(from, out var cell) && cell != null
                ? cell.GetDoors(dir) : System.Array.Empty<Door>();

        return cells.TryGetValue(from + dir, out var neighbor) && neighbor != null
            ? neighbor.GetDoors(-dir) : System.Array.Empty<Door>();
    }

    /// <summary>
    /// 문간을 지나 옆 칸으로 걸어 들어간다 (순간이동 아님 — 매 프레임 조금씩 민다).
    ///
    /// ── 방 가운데로 직진하면 안 된다 ──────────────────────
    /// 영안실 방 <b>가운데에는 부검대(섬)</b>가 있어서, 칸 중심을 향해 직진하면
    /// 4m쯤 가서 그 섬에 박힌다. 사람은 당연히 섬을 비껴 문간으로 걸으므로,
    /// 목표를 <b>문간 → 그 안쪽</b> 두 지점으로 나누고, 막히면 비껴 돌아간다.
    /// </summary>
    private IEnumerator Walk(Vector2Int dir) { yield return Walk(dir, true, false); }

    /// <param name="reportFailure">막히는 것이 <b>정상인</b> 확인(문이 닫혀 있어야 하는 경우)에서는 false.</param>
    /// <param name="lookBack">온 쪽을 돌아보며 걷는다 — 호러에서 흔한 '뒷걸음질'.</param>
    private IEnumerator Walk(Vector2Int dir, bool reportFailure, bool lookBack)
    {
        var grid = GridManager.Instance;
        Vector2Int want = grid.CenterCoord + dir;
        if (!grid.Cells.TryGetValue(want, out var targetCell) || targetCell == null)
        {
            Note(reportFailure, $"{want} 칸이 격자에 없어서 걸어갈 수 없다");
            yield break;
        }

        var startCell = grid.CenterCell;
        var socket = startCell != null ? startCell.GetSocket(dir) : null;
        Vector3 worldDir = new Vector3(dir.x, 0f, dir.y);
        Vector3 doorway = socket != null
            ? socket.transform.position
            : startCell.transform.position + worldDir * (grid.Spacing.x * 0.5f);
        Vector3 goal = doorway + worldDir * 6f;     // 문간을 지나 안쪽 6m — 문간 트리거를 완전히 벗어나는 지점

        // 길을 찾고 → 따라 걷고 → 막히면 그 자리에서 다시 찾는다 (열리는 문짝처럼 길이 바뀌는 경우)
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // 다시 찾을 때마다 장애물과의 여유를 넓힌다 — 문짝 끝·소품 모서리에 어깨가 걸리는 길을 피한다
            float clearance = 0.9f + attempt * 0.3f;
            var path = PlanPath(playerRoot.position, goal, startCell.transform.position,
                                targetCell.transform.position, clearance, out string why);
            if (path == null)
            {
                Note(reportFailure, $"{Name(dir)}쪽으로 갈 길이 없다 — {why} (중앙 {grid.CenterCoord})");
                yield break;
            }

            bool arrived = false;
            yield return Follow(path, dir, lookBack, ok => arrived = ok);
            if (arrived) break;

            if (attempt == 2)
            {
                Note(reportFailure, $"{Name(dir)}쪽 길을 따라가다 세 번 막혔다 — 막힌 자리 {lastBlockedAt}, " +
                                    $"앞을 막은 것 '{lastBlocker}' (중앙 {grid.CenterCoord})");
                yield break;
            }
        }

        if (grid.CenterCoord != want)
            Note(reportFailure, $"{Name(dir)} 문간을 지나 {want} 칸 안까지 걸었는데 칸 진입이 안 잡혔다 (중앙 {grid.CenterCoord})");
    }

    // 마지막으로 막힌 자리와 원인 — 길 찾기가 실패했을 때 보고에 쓴다
    private Vector3 lastBlockedAt;
    private string lastBlocker;

    /// <summary>
    /// 두 칸(지금 칸 + 갈 칸)을 0.5m 눈금으로 덮고, 눈금마다 <b>플레이어 몸 크기의 캡슐</b>이 들어가는지
    /// 실제 물리로 검사해 장애물 지도를 만든 뒤 너비 우선으로 길을 찾는다.
    ///
    /// 손으로 경유지를 박지 않는 이유 — 방 배치(섬·소품·문짝)가 바뀌어도 그대로 돌아야 하고,
    /// 길이 <b>아예 없을 때</b>(닫힌 문, 문짝이 문간을 막음)를 '하네스가 서툴러서'와 구분할 수 있어야 한다.
    /// </summary>
    private List<Vector3> PlanPath(Vector3 from, Vector3 to, Vector3 cellA, Vector3 cellB, float clearance, out string why)
    {
        why = null;
        const float res = 0.5f;

        Vector2 half = GridManager.Instance.Spacing * 0.5f;
        float xMin = Mathf.Min(cellA.x, cellB.x) - half.x, xMax = Mathf.Max(cellA.x, cellB.x) + half.x;
        float zMin = Mathf.Min(cellA.z, cellB.z) - half.y, zMax = Mathf.Max(cellA.z, cellB.z) + half.y;
        int w = Mathf.CeilToInt((xMax - xMin) / res) + 1;
        int h = Mathf.CeilToInt((zMax - zMin) / res) + 1;

        // 캡슐 — 발밑 턱(Step Offset)보다 위에서 머리까지. 반지름은 실제보다 조금 작게(눈금 오차 흡수).
        float feet = playerBody.bounds.min.y;
        float r = playerBody.radius * clearance;
        float yLo = feet + playerBody.stepOffset + r;
        float yHi = Mathf.Max(yLo, feet + playerBody.height - r);

        // 자기 몸은 장애물이 아니다. 플레이어가 전용 레이어면 마스크로 빼고, 아니면 잠깐 끈다.
        int playerLayer = playerRoot.gameObject.layer;
        bool disableSelf = playerLayer == 0;
        int mask = disableSelf ? ~0 : ~(1 << playerLayer);

        Physics.SyncTransforms();   // 재활용으로 옮겨진 칸이 물리에 아직 안 반영됐을 수 있다
        if (disableSelf) playerBody.enabled = false;

        var free = new bool[w, h];
        int blockedCount = 0;
        for (int x = 0; x < w; x++)
            for (int z = 0; z < h; z++)
            {
                float px = xMin + x * res, pz = zMin + z * res;
                free[x, z] = !Physics.CheckCapsule(new Vector3(px, yLo, pz), new Vector3(px, yHi, pz),
                                                   r, mask, QueryTriggerInteraction.Ignore);
                if (!free[x, z]) blockedCount++;
            }

        if (disableSelf) playerBody.enabled = true;

        Vector2Int ToNode(Vector3 p) => new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt((p.x - xMin) / res), 0, w - 1),
            Mathf.Clamp(Mathf.RoundToInt((p.z - zMin) / res), 0, h - 1));

        var start = ToNode(from);
        var goal = ToNode(to);
        free[start.x, start.y] = true;     // 지금 서 있는 자리는 당연히 설 수 있다

        // 목표 눈금이 소품에 걸리면 가장 가까운 빈 눈금으로 옮긴다 (3m 안)
        if (!free[goal.x, goal.y])
        {
            bool found = false;
            for (int ring = 1; ring <= 6 && !found; ring++)
                for (int dx = -ring; dx <= ring && !found; dx++)
                    for (int dz = -ring; dz <= ring && !found; dz++)
                    {
                        int gx = goal.x + dx, gz = goal.y + dz;
                        if (gx < 0 || gz < 0 || gx >= w || gz >= h || !free[gx, gz]) continue;
                        goal = new Vector2Int(gx, gz);
                        found = true;
                    }
            if (!found) { why = "목표 지점 주변 3m가 전부 막혀 있다"; return null; }
        }

        // 너비 우선 탐색 (8방향, 모서리 끼기 금지)
        var prev = new int[w * h];
        for (int i = 0; i < prev.Length; i++) prev[i] = -1;
        int s = start.x + start.y * w, g = goal.x + goal.y * w;
        var queue = new Queue<int>();
        prev[s] = s;
        queue.Enqueue(s);

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            if (c == g) break;
            int cx = c % w, cz = c / w;

            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h || !free[nx, nz]) continue;
                    if (dx != 0 && dz != 0 && (!free[cx + dx, cz] || !free[cx, cz + dz])) continue;

                    int n = nx + nz * w;
                    if (prev[n] != -1) continue;
                    prev[n] = c;
                    queue.Enqueue(n);
                }
        }

        if (prev[g] == -1)
        {
            why = $"두 칸 사이가 끊겨 있다 (막힌 눈금 {blockedCount}/{w * h}) — 문이 닫혔거나 문간이 막혔다";
            return null;
        }

        var nodes = new List<Vector3>();
        for (int c = g; c != s; c = prev[c])
            nodes.Add(new Vector3(xMin + (c % w) * res, from.y, zMin + (c / w) * res));
        nodes.Reverse();

        // 눈금을 건너뛰지 않는다 — 몇 개씩 건너뛰면 그 사이 직선이 문틀 모서리를 가로질러 어깨가 걸린다(겪은 일)
        return nodes;
    }

    /// <summary>경유지를 차례로 걷는다. 한 자리에서 오래 막히면 실패로 돌려준다(부른 쪽이 다시 길을 찾는다).</summary>
    private IEnumerator Follow(List<Vector3> path, Vector2Int travelDir, bool lookBack, System.Action<bool> done)
    {
        foreach (var waypoint in path)
        {
            int blocked = 0;
            for (int frame = 0; frame < 6000; frame++)
            {
                // 게임 시간 기준 — 헤드리스는 SetUp에서 프레임당 0.02초로 고정했다
                float stride = WalkSpeed * Mathf.Min(Time.deltaTime, 0.05f);

                Vector3 to = waypoint - playerRoot.position;
                to.y = 0f;
                if (to.magnitude < 0.35f) break;

                Vector3 heading = to.normalized;

                // 실제 플레이와 같게 — 가는 쪽을 보고 걷는다(돌아보며 걸을 때만 반대로).
                Face(lookBack
                    ? playerRoot.position - new Vector3(travelDir.x, 0f, travelDir.y)
                    : playerRoot.position + heading);

                Vector3 was = playerRoot.position;
                playerBody.Move(heading * stride);
                Vector3 moved = playerRoot.position - was;
                moved.y = 0f;

                if (moved.magnitude < stride * 0.2f)
                {
                    if (++blocked > 120)
                    {
                        lastBlockedAt = playerRoot.position;
                        lastBlocker = hits != null && hits.LastSideHit != null ? hits.LastSideHit : "알 수 없음";
                        done(false);
                        yield break;
                    }
                }
                else blocked = 0;

                yield return null;
            }
        }

        done(true);
    }

    /// <summary>문제로 올릴지, 기록만 남길지.</summary>
    private void Note(bool report, string message)
    {
        if (report) findings.Add(message);
        else PT("(막히는 것이 정상인 확인) " + message);
    }

    /// <summary>지금 격자에서 열려 있는 문을 전부 모은다 — 문짝은 북·동에만 있다.</summary>
    private static List<string> OpenDoorList()
    {
        var open = new List<string>();
        foreach (var kv in GridManager.Instance.Cells)
        {
            var cell = kv.Value;
            if (cell == null) continue;

            foreach (var dir in new[] { N, E })
                foreach (var door in cell.GetDoors(dir))
                    if (door != null && door.IsOpen)
                        open.Add($"{cell.Id}번 칸 {Name(dir)}문");
        }
        return open;
    }

    /// <summary>그 지점을 바라보게 시선을 돌린다 (마우스 대신).</summary>
    private void Face(Vector3 worldPoint)
    {
        Vector3 to = worldPoint - playerRoot.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.001f) playerRoot.rotation = Quaternion.LookRotation(to, Vector3.up);
    }

    /// <summary>일부러 n번 틀리며 한 칸씩 전진한다 — 추격자 등장 조건을 만든다.</summary>
    private IEnumerator MakeMistakes(int count, Vector2Int dir)
    {
        var gm = GameManager.Instance;
        for (int i = 0; i < count; i++)
        {
            bool truth = gm.CurrentRoomHasAnomaly;
            yield return JudgeCurrent(!truth);        // 진짜와 반대로 = 오답
            PT($"일부러 오답 {i + 1}회 — 누적 오답 {gm.Mistakes}/{gm.MistakesBeforeStalker}, 진행도 {gm.Progress}");
            if (i < count - 1) yield return Cross(dir);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 검사 / 보고
    // ──────────────────────────────────────────────────────────────

    private void CheckGridInvariants(string where)
    {
        var grid = GridManager.Instance;
        var center = grid.CenterCell;
        if (center == null) { findings.Add($"{where}: 중앙 칸이 null"); return; }

        if (grid.Cells.Count != 9) findings.Add($"{where}: 칸 수가 {grid.Cells.Count}개");

        var ids = new List<int>();
        Vector3 centerPos = center.transform.position;

        foreach (var kv in grid.Cells)
        {
            var cell = kv.Value;
            if (cell == null) { findings.Add($"{where}: {kv.Key} 자리가 비었다"); continue; }
            if (cell.Coord != kv.Key) findings.Add($"{where}: 좌표 불일치 — 사전 {kv.Key}, 칸 {cell.Coord}");
            ids.Add(cell.Id);

            Vector2Int d = kv.Key - grid.CenterCoord;
            if (Mathf.Abs(d.x) > grid.Radius || Mathf.Abs(d.y) > grid.Radius)
                findings.Add($"{where}: {cell.Id}번 칸이 격자 밖 {kv.Key} (중앙 {grid.CenterCoord})");

            Vector3 expect = centerPos + new Vector3(d.x * grid.Spacing.x, 0f, d.y * grid.Spacing.y);
            float off = Vector3.Distance(expect, cell.transform.position);
            if (off > 0.05f)
                findings.Add($"{where}: {cell.Id}번 칸이 제자리에서 {off:F2}m 어긋났다 " +
                             $"(좌표 {kv.Key}, 위치 {cell.transform.position}, 기대 {expect})");
        }

        ids.Sort();
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == i + 1) continue;
            findings.Add($"{where}: 칸 번호가 1~9가 아니다 — [{string.Join(",", ids)}]");
            break;
        }

        Vector2Int playerCoord = grid.GetCoordAt(playerRoot.position);
        if (playerCoord != grid.CenterCoord)
            findings.Add($"{where}: 플레이어가 선 칸 {playerCoord}과 격자가 아는 중앙 {grid.CenterCoord}이 다르다");

        if (audit != null && audit.ViolationCount > 0)
            findings.Add($"{where}: 은폐 위반 누적 {audit.ViolationCount}회 — {audit.LastViolation}");
    }

    private int MarkedCells()
    {
        int n = 0;
        foreach (var kv in GridManager.Instance.Cells)
            if (kv.Value != null && kv.Value.StalkerIsAnomaly) n++;
        return n;
    }

    private static string IdMap()
    {
        var grid = GridManager.Instance;
        var sb = new System.Text.StringBuilder();
        int r = grid.Radius;
        for (int y = r; y >= -r; y--)
        {
            for (int x = -r; x <= r; x++)
            {
                var coord = grid.CenterCoord + new Vector2Int(x, y);
                string cellText = grid.Cells.TryGetValue(coord, out var c) && c != null ? c.Id.ToString() : "·";
                sb.Append(x == 0 && y == 0 ? $"[{cellText}]" : $" {cellText} ");
                sb.Append('\t');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string AnomalyMap()
    {
        var parts = new List<string>();
        foreach (var kv in GridManager.Instance.Cells)
            if (kv.Value != null) parts.Add($"{kv.Value.Id}:{kv.Value.AnomalyLabel}");
        parts.Sort();
        return string.Join(", ", parts);
    }

    private static string Name(Vector2Int dir) =>
        dir == N ? "북" : dir == E ? "동" : dir == S ? "남" : dir == W ? "서" : dir.ToString();

    private static void PT(string message) => Debug.Log("[PT] " + message);

    private int caughtCount;   // GameManager.OnCaughtByStalker가 남기는 로그를 센다

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (condition != null && condition.StartsWith("[GameManager] 잡혔다")) caughtCount++;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (condition != null && condition.StartsWith("[PT]")) return;
        errors.Add($"{type}: {condition}");
    }

    private void Report(string title)
    {
        foreach (var f in findings) Debug.Log($"[PT] ⚠ 발견 — {f}");
        foreach (var e in errors) Debug.Log($"[PT] ⚠ 에러 로그 — {e}");
        PT($"=== {title} 끝 — 발견 {findings.Count}건, 에러 로그 {errors.Count}건 ===");

        var all = new List<string>(findings);
        all.AddRange(errors);
        if (all.Count > 0)
            Assert.Fail($"{title}:\n  - " + string.Join("\n  - ", all));
    }
}

/// <summary>
/// 테스트가 씬에 붙이는 관찰자. 격자 이벤트(중앙 바뀜·재활용)와 <b>칸마다의 이상현상 변화</b>를
/// 매 프레임 비교해 시간순으로 남긴다 — "언제, 어느 칸이, 무엇 직후에 바뀌었나"를 되짚기 위해서다.
/// 특히 <b>플레이어가 서 있는 칸</b>의 정답이 바뀌면 따로 표시한다.
/// </summary>
public class PlaytestObserver : MonoBehaviour
{
    private readonly Dictionary<GridCell, string> lastLabel = new Dictionary<GridCell, string>();
    private readonly List<(Door door, string name, bool open)> doors = new List<(Door, string, bool)>();
    private GridManager grid;
    private bool lastJudged;
    private int lastMistakes = -1;

    private void Start()
    {
        grid = GridManager.Instance;
        if (grid == null) return;

        grid.CenterChanged += c => Log($"중앙 바뀜 → {c.Id}번 {c.Coord}");
        grid.CellRecycled += c => Log($"재활용 → {c.Id}번 칸이 {c.Coord}로");

        // 문짝은 북·동에만 있다 — 칸 번호는 평생 안 바뀌므로 이름을 한 번만 붙여 둔다
        foreach (var kv in grid.Cells)
        {
            if (kv.Value == null) continue;
            foreach (var dir in new[] { new Vector2Int(0, 1), new Vector2Int(1, 0) })
            {
                var leaves = kv.Value.GetDoors(dir);
                for (int i = 0; i < leaves.Length; i++)
                    if (leaves[i] != null)
                        doors.Add((leaves[i], $"{kv.Value.Id}번 칸 {(dir.y == 1 ? "북" : "동")}문{(i == 0 ? "①" : "②")}", leaves[i].IsOpen));
            }
        }

        Snapshot(false);
    }

    private void Update() => Snapshot(true);

    private void Snapshot(bool report)
    {
        if (grid == null) return;

        var center = grid.CenterCell;
        foreach (var kv in grid.Cells)
        {
            var cell = kv.Value;
            if (cell == null) continue;

            string now = cell.AnomalyLabel;
            if (report && lastLabel.TryGetValue(cell, out var before) && before != now)
                Log($"{cell.Id}번 칸 이상현상 '{before}' → '{now}'" +
                    (cell == center ? "   ◀◀ 플레이어가 서 있는 칸!" : ""));
            lastLabel[cell] = now;
        }

        for (int i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (d.door == null || d.door.IsOpen == d.open) continue;
            if (report) Log($"{d.name} {(d.door.IsOpen ? "열림" : "닫힘")} (중앙 {(center != null ? center.Id : -1)}번)");
            doors[i] = (d.door, d.name, d.door.IsOpen);
        }

        var gm = GameManager.Instance;
        if (gm == null) return;

        if (report && gm.HasJudged != lastJudged)
            Log($"판정 상태 {lastJudged} → {gm.HasJudged} (중앙 {(center != null ? center.Id : -1)}번)");
        if (report && lastMistakes >= 0 && gm.Mistakes != lastMistakes)
            Log($"누적 오답 {lastMistakes} → {gm.Mistakes}");

        lastJudged = gm.HasJudged;
        lastMistakes = gm.Mistakes;
    }

    private static void Log(string message) =>
        Debug.Log($"[OBS] t={Time.time:F2} {message}");
}

/// <summary>
/// 플레이어에 붙여 <b>무엇에 부딪혔는지</b> 남긴다. 길이 막혔을 때 "문짝 끝인지, 소품인지, 문턱인지"를
/// 가르려면 CharacterController가 실제로 닿은 콜라이더가 필요하다(바닥은 뺀다).
/// </summary>
public class PlayerHitRecorder : MonoBehaviour
{
    public string LastSideHit { get; private set; }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y > 0.7f) return;   // 바닥
        float height = hit.point.y - transform.position.y;
        LastSideHit = $"{hit.collider.name} (레이어 {LayerMask.LayerToName(hit.collider.gameObject.layer)}, " +
                      $"발에서 {height:F2}m 높이, 부모 '{(hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "-")}')";
    }
}
#endif


