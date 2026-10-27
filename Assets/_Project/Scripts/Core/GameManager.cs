using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 전체 진행을 지휘하는 두뇌(중앙 매니저).
///
/// ── 담당 ──────────────────────────────────────────────
///  • 진행도 관리 (0 ~ clearGoal, 정답 +1 / 오답 0)
///  • 판정 처리   (플레이어 O/X vs 실제 이상현상 유무)
///  • 칸 전환마다 판정 리셋, 재활용된 칸을 새 이상현상으로 다시 꾸미기
///  • 누적 오답이 기준에 닿으면 '그것'(추격자)을 그 칸의 이상현상으로 세우기
///
/// ── 한 루프의 흐름 (3x3 격자) ─────────────────────────
///  1. 선 칸을 관찰해 이상현상이 있는지 판단한다
///  2. 키패드에서 O/X 판정 → 사방 문 잠금이 풀린다
///  3. 나가는 문을 직접 E로 열고 옆 칸으로 걸어 들어간다
///  4. GridManager가 지나온 문을 곧장 닫고, 뒤처진 행/열을 진행 방향 앞으로 옮긴다
///  5. 옮겨간 칸은 여기(OnGridCellRecycled)에서 새 이상현상으로 다시 꾸며진다
///
/// 플레이어는 <b>순간이동하지 않는다</b> — 카메라는 제 발로 걷고 칸 쪽이 재활용된다.
/// 암전도 없다. 방 리셋과 이상현상 재추첨을 재활용이 대신 맡는다.
///
/// ── 3안(암전+순간이동)은 어디 갔나 ────────────────────
/// 격자로 갈아타기 전에는 RoomModulePool로 건물 2채를 좌우 슬롯에 번갈아 세우고
/// 암전으로 스왑을 가렸다. 그 경로는 전부 제거했다 — 완성본은 <b>main 브랜치</b>에
/// 통째로 있고, 왜 바꿨는지는 README에 적혀 있다. 여기엔 격자 경로 하나만 남는다.
///
/// 다른 스크립트(판정 UI, 키패드, 표시판)가 쉽게 접근하도록 싱글톤으로 둔다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("오답 순간 잠깐 번쩍이는 그림. 비워도 동작에는 지장 없다(연출만 빠짐).")]
    [SerializeField] private ScareFlash wrongAnswerFlash;
    [Tooltip("씬 단일 추격자. 칸이 몇 개 깔리든 이 한 마리만 존재한다 — 어느 칸에 세울지는 그때그때 GridCell.StalkerSpawnPoint를 넘겨 지시한다.")]
    [SerializeField] private Stalker stalker;

    [Header("Rules")]
    [Tooltip("이 횟수만큼 연속 성공하면 클리어.")]
    [SerializeField] private int clearGoal = 8;
    [Tooltip("칸에 이상현상이 나타날 확률 (0~1).")]
    [SerializeField, Range(0f, 1f)] private float anomalyChance = 0.5f;
    [Tooltip("이 횟수만큼 누적으로 틀리면 추격자가 나타난다. 0이면 등장하지 않는다.")]
    [SerializeField, Min(0)] private int mistakesBeforeStalker = 3;
    // 추가 생성 (2026-10-26)
    [Tooltip("떠난 칸을 다시 꾸미기까지 기다리는 시간(초). 문이 닫히는 시간(Door의 Open Duration, 기본 1초)보다 길어야 바뀌는 게 안 보인다.")]
    [SerializeField, Min(0f)] private float redressDelay = 1.3f;

    [Header("Debug")]
    [Tooltip("체크 시 O(있음)/X(없음) 키로 판정 테스트. 판정 UI 만들면 끄면 됨.")]
    [SerializeField] private bool enableTestKeys = true;

    // ── 상태 ──
    private int progress;
    private bool judged;            // 이번 칸을 이미 판정했는지 (중복 방지)

    // 누적 오답 수. progress는 틀릴 때마다 0으로 리셋되므로 '몇 번 틀렸나'를
    // 따로 세야 한다. 추격자 등장 조건에 쓰고, 등장시키면 다시 0으로 돌린다.
    private int mistakes;
    // 추가 생성 (2026-10-26) — 방금 떠난 칸. 다시 꾸밀 대상을 기억해 둔다.
    private GridCell previousCell;

    /// <summary>
    /// 플레이어가 지금 안전한지 — 추격자 피신 규칙.
    ///
    /// 기준은 "칸"이 아니라 <b>경계 문</b>이다. 격자는 방 유형이 없어 3안의
    /// '안전한 방(키패드 방)' 개념이 그대로 안 옮겨지지만, 3안 규칙의 본질은 사실
    /// "나와 추격자 사이 문이 닫혀 있다"였다. 그걸 그대로 일반화한다 —
    /// <b>내 칸 ≠ 추격자가 있는 칸이고, 그 사이 경계 문이 닫혀 있으면 안전.</b>
    ///
    /// 같은 칸에 있으면 문을 다 닫아도 안전할 수 없다(자기 칸은 경계가 아니므로
    /// 첫 조건에서 이미 걸러진다). 인접하지 않으면 애초에 위협이 안 닿으므로
    /// 안전으로 본다 (AreBoundaryDoorsClosed가 처리).
    /// </summary>
    public bool PlayerIsSheltered
    {
        get
        {
            var gm = GridManager.Instance;
            if (stalker == null || gm == null) return false;

            Vector2Int playerCoord = gm.CenterCoord;
            Vector2Int stalkerCoord = gm.GetCoordAt(stalker.transform.position);
            if (playerCoord == stalkerCoord) return false;

            return gm.AreBoundaryDoorsClosed(playerCoord, stalkerCoord);
        }
    }

    public int Progress => progress;
    /// <summary>클리어에 필요한 연속 성공 횟수 (진행도 UI가 "n / 목표" 표시에 사용).</summary>
    public int ClearGoal => clearGoal;
    public bool IsCleared => progress >= clearGoal;
    /// <summary>이번 칸을 이미 판정했는지 (키패드가 중복 판정을 막는 데 사용).</summary>
    public bool HasJudged => judged;

    /// <summary>누적 오답 수 (디버그 표시용).</summary>
    public int Mistakes => mistakes;
    /// <summary>추격자 등장까지 필요한 누적 오답 수. 0이면 등장하지 않음.</summary>
    public int MistakesBeforeStalker => mistakesBeforeStalker;
    /// <summary>지금 선 칸에 이상현상이 있는지 (디버그·강제 판정용).</summary>
    public bool CurrentRoomHasAnomaly => CurrentAnomalyHost != null && CurrentAnomalyHost.HasAnomaly;

    /// <summary>
    /// 지금 판정 대상인 칸. Judge()·Dress()가 여기 하나만 본다.
    ///
    /// 타입이 <see cref="IAnomalyHost"/>인 이유는 이 계약을 <b>3안의 RoomModule과
    /// 공유하던 것</b>이기 때문이다(완성본은 main 브랜치). 구현체가 GridCell 하나로
    /// 줄었어도 계약을 남겨두면 판정 로직이 "칸"의 구체적 구조를 몰라도 된다.
    /// </summary>
    private IAnomalyHost CurrentAnomalyHost => GridManager.Instance != null
        ? GridManager.Instance.CenterCell
        : null;

    /// <summary>씬 단일 추격자 (디버그 표시용).</summary>
    public Stalker Stalker => stalker;

    /// <summary>[디버그] 이번 칸의 이상현상을 즉시 '그것'으로 바꾼다.</summary>
    public void DebugMakeStalkerAnomaly()
    {
        var room = CurrentAnomalyHost;
        if (room == null) return;
        SetStalkerAsAnomaly(room);
        judged = false;
        Debug.Log("[Debug] 이번 칸 이상현상을 '그것'으로 교체");
    }

    /// <summary>
    /// 이 칸을 '그것' 칸으로 세팅하고, 씬 단일 추격자를 그 스폰 지점에 등장시킨다.
    /// 칸 데이터 세팅과 물리적 등장을 한 곳에서 묶어, 한쪽만 호출해 생기는
    /// "정답은 있음인데 화면엔 아무것도 없는" 사고를 막는다.
    /// </summary>
    private void SetStalkerAsAnomaly(IAnomalyHost room)
    {
        room.SetStalkerAsAnomaly();

        // 추가 생성 — 누적 오답 청산은 <b>표시를 심은 순간이 아니라 벌칙이 실제로
        // 도착한 이 순간</b>에 한다. 심는 순간에 0으로 돌리면, 그 표시가 재활용으로
        // 지워질 때 벌칙이 영구히 사라져 "3번 틀렸는데 아무것도 안 나온다"가 된다.
        // 여기로 옮기면 지워져도 다음 재활용에서 다시 심긴다 — 벌칙이 반드시 도착한다.
        mistakes = 0;

        // 수정 (2026-10-27) — 남은 표시를 지우는 것도 <b>여기서</b> 한다.
        //
        // 전에는 Dress()가 심을 때 지웠는데, Dress는 CenterChanged보다 먼저 불려서
        // 플레이어가 방금 들어선 칸의 표시까지 지워버렸다(위 Dress 주석 참고).
        // "청산"은 벌칙이 집행된 이 자리에서 한 번에 하는 것이 맞다 — 누적 오답을
        // 0으로 돌리는 것과 같은 순간, 같은 이유다.
        //
        // 플레이어가 앞으로 걸어오는 동안 표시가 여러 칸에 심길 수 있는데(매 재활용마다
        // 앞 칸에 하나씩), 그대로 두면 나중에 그 칸에 들어갔을 때 이미 청산된 벌칙이
        // 한 번 더 집행된다. 씬의 추격자는 한 마리이므로 표시도 하나만 남긴다.
        ClearOtherStalkerMarks(room);

        if (stalker == null)
        {
            Debug.LogWarning("[GameManager] Stalker가 연결돼 있지 않아 '있음'인데 아무것도 나타나지 않습니다.", this);
            return;
        }
        stalker.Appear(room.StalkerSpawnPoint);
    }

    /// <summary>씬 단일 추격자를 물린다.</summary>
    private void DismissStalker()
    {
        if (stalker != null) stalker.Vanish();
    }

    private InputAction judgeYesAction; // O = 있음
    private InputAction judgeNoAction;  // X = 없음

    // 추가 생성 — 렉 미터용 계측 구간. 이름을 LagMeterRows가 그대로 등록한다.
    private static readonly ProfilerMarker s_dressMarker = new ProfilerMarker("GameManager.Dress");

    private void Awake()
    {
        // 싱글톤 세팅
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (enableTestKeys)
        {
            judgeYesAction = new InputAction("JudgeYes", InputActionType.Button, "<Keyboard>/o");
            judgeNoAction = new InputAction("JudgeNo", InputActionType.Button, "<Keyboard>/x");
        }
    }

    private void OnEnable()
    {
        judgeYesAction?.Enable();
        judgeNoAction?.Enable();
    }

    private void OnDisable()
    {
        judgeYesAction?.Disable();
        judgeNoAction?.Disable();

        if (GridManager.Instance == null) return;

        // Initialized까지 떼는 이유: Start에서 칸이 아직 없었으면 그걸 구독한 채로
        // 꺼질 수 있다. 구독만 남으면 다음에 켜질 때 첫 칸 세팅이 두 번 돈다.
        GridManager.Instance.Initialized -= BeginFirstCell;
        GridManager.Instance.CenterChanged -= OnGridCenterChanged;
        GridManager.Instance.CellRecycled -= OnGridCellRecycled;
    }

    private void Start()
    {
        var grid = GridManager.Instance;
        if (grid == null)
        {
            Debug.LogError("[GameManager] 씬에서 GridManager를 찾지 못해 판정을 시작하지 못합니다.", this);
            return;
        }

        // 판정 리셋(칸 전환)·재꾸미기(재활용)를 GridManager 이벤트에 접붙인다.
        // GridManager가 직접 GameManager를 부르지 않는 이유는 관심사 분리 —
        // "칸이 어디 있나"와 "그 칸에 뭐가 있어야 하나"를 갈라 둔다.
        grid.CenterChanged += OnGridCenterChanged;
        grid.CellRecycled += OnGridCellRecycled;

        // GridManager.Start()가 이 스크립트보다 먼저 돌았을 수도, 나중에 돌 수도
        // 있다(스크립트 실행 순서는 보장되지 않는다). 이미 칸이 서 있으면 곧장
        // 시작하고, 아니면 Initialized를 기다린다 — 둘 다 안전하다.
        if (grid.CenterCell != null) BeginFirstCell(grid.CenterCell);
        else grid.Initialized += BeginFirstCell;
    }

    private void Update()
    {
        if (!enableTestKeys) return;
        if (judgeYesAction.WasPressedThisFrame()) Judge(true);
        if (judgeNoAction.WasPressedThisFrame()) Judge(false);
    }

    /// <summary>
    /// 계기판(판정 UI)에서 호출. 플레이어의 답과 실제 이상현상 유무를 비교한다.
    /// </summary>
    /// <param name="playerSaysAnomaly">플레이어가 O(있음)를 골랐으면 true.</param>
    public void Judge(bool playerSaysAnomaly)
    {
        if (judged) return;   // 이미 판정한 칸이면 무시

        var room = CurrentAnomalyHost;
        if (room == null)
        {
            Debug.LogWarning("[GameManager] 판정할 칸을 찾지 못해 무시합니다.", this);
            return;
        }

        judged = true;

        bool correct = (playerSaysAnomaly == room.HasAnomaly);
        if (correct)
        {
            progress++;
            Debug.Log($"[GameManager] 정답! 진행도 {progress}/{clearGoal}");

            // ⚠ 여기서 return하면 안 된다. 아래 문 잠금 해제를 건너뛰어
            // <b>클리어한 플레이어가 칸에 갇힌다</b> — 이겼는데 못 나가는 상태.
            if (IsCleared) OnClear();
        }
        else
        {
            progress = 0;
            mistakes++;
            Debug.Log($"[GameManager] 오답! 진행도 0으로 초기화 (누적 오답 {mistakes})");

            // 진행도만 조용히 0이 되면 틀린 게 아프지 않다. 벌은 이미 줬으니
            // 더 뺏는 대신 놀라게 해서 '틀렸다'를 감각으로 남긴다.
            if (wrongAnswerFlash != null) wrongAnswerFlash.Flash();

            // 기준에 닿았으면 '그것'이 다음 칸의 이상현상이 된다 (Dress에서 처리).
            if (mistakesBeforeStalker > 0 && mistakes >= mistakesBeforeStalker)
                Debug.Log("[GameManager] 다음 칸에는 그것이 있다");
        }

        // 정답/오답과 무관하게 사방 출입문의 잠금을 푼다.
        // 자동으로 열어주지는 않는다 — 나가는 문은 플레이어가 직접 E로 연다.
        GridManager.Instance.SetCellExitsLocked(GridManager.Instance.CenterCell, false);
    }

    /// <summary>
    /// 추격자에게 잡혔을 때 호출. 진행도를 잃고 추격자가 물러간다.
    /// 죽음 화면 대신 '정신을 차려보니 또 그 방'으로 처리해, 잡혔는데 왜 살아
    /// 있는지 모르는 불안감을 남긴다.
    ///
    /// ── 벌칙은 이것으로 확정이다 ──────────────────────────
    /// 진행도 0 + ScareFlash(오답과 같은 신호) + 추격자 소멸. 그 이상은 넣지 않는다.
    /// 방을 바꾸지 않는 격자에서 "방금 무슨 일이 있었다"를 알려줄 연출은 이것이
    /// 가장 자연스럽고, 벌 자체는 이미 진행도 소멸로 충분히 줬다. 3안이 쓰던
    /// 암전+순간이동(AdvanceToNextRoom)은 격자 구조에 애초에 맞지 않아 버렸다.
    /// </summary>
    public void OnCaughtByStalker()
    {
        progress = 0;
        mistakes = 0;
        Debug.Log("[GameManager] 잡혔다 — 진행도 0");

        if (wrongAnswerFlash != null) wrongAnswerFlash.Flash();

        DismissStalker();
    }

    /// <summary>
    /// 지정 칸에 이상현상을 세팅한다 ('새로 꾸미기').
    ///
    /// 누적 오답이 기준에 닿으면 랜덤을 돌리지 않고 <b>'그것'을 이번 칸의 이상현상으로
    /// 고정</b>한다. 벌칙이되 규칙 밖의 사고가 아니라, 어디까지나 관찰로 발견하고
    /// O로 판정해야 하는 이상현상 중 하나로 다룬다.
    ///
    /// ⚠️ <b>데이터만 세팅한다 — 진짜 Stalker를 등장시키지 않는다.</b> 이 함수는
    /// 재활용 시점(<see cref="OnGridCellRecycled"/>)에도 불리는데, 그건 플레이어가
    /// 아직 안 닿은 <b>먼 칸</b>을 미리 꾸미는 것이다. 여기서
    /// <c>SetStalkerAsAnomaly(IAnomalyHost)</c>(등장까지 시키는 쪽)를 부르면 씬에
    /// 하나뿐인 Stalker가 플레이어 눈앞이 아니라 그 먼 칸으로 즉시 텔레포트해버리고,
    /// 플레이어가 실제로 다른 칸에 들어서는 순간 OnGridCenterChanged가 그 칸엔
    /// '그것' 표시가 없다며 도로 숨겨버린다 — 위치도 엉뚱하고 몸도 꺼진 채로 남는
    /// 원인이었다. <see cref="IAnomalyHost.SetStalkerAsAnomaly"/>(데이터 전용)만
    /// 불러 세팅해 두면, 실제 등장은 플레이어가 그 칸에 들어서는 순간
    /// (OnGridCenterChanged)에만 일어난다.
    /// </summary>
    /// <param name="allowStalker">
    /// 이 칸이 '그것'을 받을 자격이 있는지. 재활용은 한 번에 여러 칸을 옮기는데
    /// 그중 <b>플레이어 정면 칸</b>만 true다 (OnGridCellRecycled 주석 참고).
    /// </param>
    private void Dress(IAnomalyHost room, bool allowStalker)
    {
        // 추가 생성 — 재활용 프레임에 얹히는 몫. 재활용 자체(좌표 계산 + 배치)보다
        // 이상현상 복원·발동이 더 무거울 수 있어, 합계가 아니라 따로 봐야 원인이 갈린다.
        using var _ = s_dressMarker.Auto();

        if (allowStalker && mistakesBeforeStalker > 0 && mistakes >= mistakesBeforeStalker)
        {
            // ⚠️ 여기서 mistakes를 0으로 돌리지 않는다 — 청산은 벌칙이 실제로 도착한
            // 순간(SetStalkerAsAnomaly)에 한다. 그래야 이 표시가 재활용으로 지워져도
            // 다음 재활용에서 다시 심겨 벌칙이 반드시 도착한다.
            //
            // ⚠️ 여기서 <b>다른 칸의 표시를 지우지도 않는다</b> (겪은 버그, 2026-10-27).
            // 이 함수는 CenterChanged보다 <b>먼저</b> 불린다(GridManager.OnPlayerEnteredCell
            // 참고 — 재활용이 끝난 뒤에야 CenterChanged가 울린다). 그래서 여기서 지우면
            // <b>플레이어가 방금 들어선 칸의 표시까지 지워져</b>, 그 칸에 도착한 순간
            // "표시 없음"이 되어 추격자가 안 나온다. 누적 오답도 그대로 남아 다음 칸에
            // 또 심기고, 표시가 영원히 한 칸 앞으로 도망친다. 게다가 지워진 칸은
            // SetAnomaly(false)로 '없음'이 되어 <b>들어가는 방마다 이상현상이 사라졌다.</b>
            room.SetStalkerAsAnomaly();
            Debug.Log($"[GameManager] {room.DisplayName} 세팅 — 이상현상: 그것 (관찰해서 O로 판정할 것)");
            return;
        }

        bool has = Random.value < anomalyChance;
        room.SetAnomaly(has);
        Debug.Log($"[GameManager] {room.DisplayName} 세팅 — 이상현상: {(has ? "있음" : "없음")}");
    }

    /// <summary>
    /// 다른 칸에 남아 있는 '그것' 표시를 지운다 (지운 칸은 정상으로 복원).
    ///
    /// 씬의 추격자는 <b>한 마리</b>라 격자의 표시도 한 칸만 있어야 한다. 플레이어가
    /// 정면이 아니라 옆으로 돌아 걸으면 전환마다 새 정면 칸에 표시가 생기는데, 옛
    /// 표시를 지우지 않으면 나중에 그 칸에 들어갔을 때 <b>이미 청산된 벌칙이 한 번
    /// 더</b> 집행된다.
    ///
    /// 표시가 걸린 칸은 언제나 플레이어가 아직 안 닿은 칸이라, 정상으로 되돌려도
    /// 눈에 띄지 않는다.
    /// </summary>
    private void ClearOtherStalkerMarks(IAnomalyHost keep)
    {
        var grid = GridManager.Instance;
        if (grid == null) return;

        foreach (var kv in grid.Cells)
        {
            var other = kv.Value;
            if (other == null || ReferenceEquals(other, keep)) continue;
            if (other.StalkerIsAnomaly) other.SetAnomaly(false);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // GridManager 이벤트에 접붙인 판정 배선 —
    // "언제 판정을 리셋하나 / 언제 새로 꾸미나"가 여기서 결정된다.
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 게임 시작 직후 딱 한 번. 가운데 칸은 늘 '정상'으로 고정한다 —
    /// 플레이어가 무엇이 정상인지 눈에 익힌 뒤에야 이상현상을 알아볼 수 있기 때문.
    ///
    /// 나머지 칸도 여기서 한 번은 꾸며 둔다. 재활용된 적이 없는 칸은
    /// (기본값 그대로) 이상현상이 없는 상태인데, 그대로 두면 처음 옆 칸으로
    /// 넘어갔을 때 항상 '정상'으로만 보여 판정이 무의미해진다.
    /// </summary>
    private void BeginFirstCell(GridCell centerCell)
    {
        if (centerCell == null)
        {
            Debug.LogError("[GameManager] GridManager가 중앙 칸을 못 만들어 판정을 시작하지 못합니다.", this);
            return;
        }

        centerCell.SetAnomaly(false);
        Debug.Log($"[GameManager] {centerCell.name} 세팅 — 첫 칸이라 이상현상 없음으로 고정");

        // allowStalker: false — 아직 한 번도 안 틀렸으니 '그것'이 설 이유가 없다.
        // mistakes가 0이라 어차피 안 걸리지만, 첫 부팅에 추격자가 서는 경우는
        // 설계상 존재하지 않는다는 것을 호출부에서 못 박아 둔다.
        foreach (var kv in GridManager.Instance.Cells)
            if (kv.Value != null && kv.Value != centerCell) Dress(kv.Value, false);

        judged = false;
        mistakes = 0;
        previousCell = centerCell;   // 추가 생성 (2026-10-26)

        // 이상현상이 없는 칸이어도 판정은 반드시 거쳐야 한다 — 무엇이 '정상'인지
        // 관찰해서 X로 확인하는 것 자체가 학습이다.
        GridManager.Instance.SetCellExitsLocked(centerCell, true);
    }

    /// <summary>
    /// 플레이어가 선 칸(중앙)이 바뀔 때마다 — 즉 "칸을 넘어 걸어 들어감" 시점.
    /// 판정을 리셋하고, 이 칸이 '그것' 칸이면 그때 실제로 추격자를 세운다.
    /// </summary>
    private void OnGridCenterChanged(GridCell cell)
    {
        judged = false;

        // 추가 생성 (2026-10-26) — 방금 떠난 칸을 다시 꾸민다.
        //
        // ── 왜 필요한가 (겪은 버그) ───────────────────────
        // Dress()는 <b>재활용될 때만</b> 불렸다. 그런데 재활용되는 것은 "뒤처진 줄"뿐이라,
        // 두 칸을 왔다 갔다 하면 그 두 칸은 <b>영영 뒤처진 줄이 되지 않는다.</b> 결과적으로
        // 이상현상이 칸에 굳어서, 되돌아가면 늘 같은 것이 나왔다 — "7번은 늘 서랍 열림,
        // 9번은 늘 없음"처럼. 칸이 아홉 개뿐이라 외우면 판정이 공짜가 되고,
        // 무엇보다 <b>같은 방을 돌려쓴다는 단서</b>가 된다. 문 흐름을 통일해 없앴던
        // 그 단서와 같은 종류다.
        //
        // ── 왜 바로 안 꾸미고 미루나 ──────────────────────
        // 이 이벤트는 GridManager가 <b>지나온 문을 닫기 전에</b> 울린다. 지금 꾸미면
        // 활짝 열린 문 너머에서 이상현상이 바뀌는 것이 보인다. 문이 닫히기를 기다렸다
        // 꾸민다 — 재활용을 닫힌 문 뒤에 숨기는 것과 같은 규칙이다.
        if (previousCell != null && previousCell != cell)
            StartCoroutine(RedressAfterDoorCloses(previousCell));
        previousCell = cell;

        // 판정 전엔 이 칸의 사방 출입문이 전부 잠긴다 — 이상현상을 관찰해서
        // O/X로 답해야 다음 칸으로 나갈 수 있다. Judge()가 판정 직후 다시 푼다.
        GridManager.Instance.SetCellExitsLocked(cell, true);

        if (cell != null && cell.StalkerIsAnomaly) SetStalkerAsAnomaly(cell);
        else DismissStalker();
    }

    /// <summary>
    /// 칸 하나가 재활용으로 자리를 옮긴 직후 — 그 칸을 새 이상현상으로 다시 꾸민다.
    /// 재활용은 언제나 플레이어가 닿기 <b>전</b>에 일어나므로(뒤에서 조용히),
    /// 다시 꾸며도 눈에 띄지 않는다.
    ///
    /// ── '그것'은 정면 칸에만 심는다 (겪은 버그) ───────────
    /// 재활용은 한 번에 <b>한 줄(한 변만큼)</b>을 옮긴다. 그중 플레이어가 문으로
    /// 곧장 걸어갈 수 있는 칸은 <b>정면 하나뿐</b>이고, 나머지는 대각선이라 문으로
    /// 이어져 있지 않다(두 번 움직여야 닿는다).
    ///
    /// 전에는 옮겨지는 순서대로 첫 칸에 심었는데, 그 첫 칸이 하필 대각선이었다
    /// (GridManager의 재배치 루프가 줄의 한쪽 끝에서 시작한다). 그래서 닿기 전에
    /// 그 칸이 또 재활용되면 표시가 지워지고, <b>3번 틀렸는데 아무것도 안 나오는
    /// 판</b>이 생겼다.
    ///
    /// <see cref="GridManager.RecycleRecord.to"/>가 이미 그 정면 칸 좌표다
    /// (뒤처진 줄의 대표 좌표 + 진행 방향 × 한 변 == 새 중앙 + 진행 방향).
    /// 새 API를 만들 필요 없이 그걸 그대로 쓴다.
    /// </summary>
    /// <summary>
    /// 떠난 칸을 <paramref name="cell"/>, 문이 닫힌 뒤에 새 이상현상으로 바꾼다.
    ///
    /// 기다리는 시간은 <see cref="redressDelay"/> — 문이 닫히는 데 걸리는 시간보다
    /// 길어야 한다(Door의 Open Duration 기본 1초).
    ///
    /// <b>되돌아왔으면 하지 않는다.</b> 기다리는 사이 플레이어가 그 칸으로 다시
    /// 들어왔을 수 있는데, 그때 꾸미면 눈앞에서 이상현상이 바뀐다 — 고치려던 것보다
    /// 더 나쁜 단서가 된다.
    /// </summary>
    private System.Collections.IEnumerator RedressAfterDoorCloses(GridCell cell)
    {
        yield return new WaitForSeconds(redressDelay);

        if (cell == null || GridManager.Instance == null) yield break;
        if (GridManager.Instance.CenterCell == cell) yield break;   // 되돌아왔다 — 건드리지 않는다

        Dress(cell, false);   // '그것'은 앞쪽 칸 몫이다(OnGridCellRecycled 참고)
    }

    private void OnGridCellRecycled(GridCell cell)
    {
        if (cell == null) return;

        bool isAheadOfPlayer = cell.Coord == GridManager.Instance.LastRecycle.to;
        Dress(cell, isAheadOfPlayer);
    }

    /// <summary>
    /// 목표 달성 순간.
    ///
    /// 이 프로젝트는 완성된 게임이 아니라 <b>격자 재활용 기능의 데모</b>라
    /// 클리어 화면·엔딩을 의도적으로 두지 않는다. 진행도 UI가 CLEAR로 바뀌는 것이
    /// 도달 표시이고, 그 뒤로도 격자는 계속 돈다 — 시연을 끊지 않기 위해서다.
    /// </summary>
    private void OnClear()
    {
        Debug.Log("[GameManager] 클리어! 🎉 — 진행도 UI가 CLEAR로 바뀐다 (데모라 엔딩은 없다)");
    }
}
