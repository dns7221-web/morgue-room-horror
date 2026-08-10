using UnityEngine;

/// <summary>
/// <b>볼 때는 멈추고, 안 볼 때만 다가오는</b> 추격자.
/// 방에 하나 더 있던 그 시체가 일어나 따라온다는 설정.
///
/// ── 규칙 ──────────────────────────────────────────────
///  • 플레이어에게 <b>보이는 동안</b>  → 완전히 얼어붙는다 (회전조차 하지 않는다)
///  • <b>시선을 벗어나면</b>          → 곧장 다가온다
///  • 일정 거리 안에 들어오면         → 잡힘 (GameManager에 알린다)
///
/// '완전히'가 핵심이다. 미세하게라도 움직이거나 고개를 돌리면 "AI가 돌고 있네"가
/// 되어버린다. 미동도 없어야 <b>내가 안 볼 때만 움직인다</b>는 확신이 생긴다.
///
/// ── 왜 NavMesh를 안 쓰나 ──────────────────────────────
/// 이 게임의 건물은 런타임에 좌우 슬롯을 오간다. 미리 구운 NavMesh는 제자리에
/// 남으므로 맞지 않는다. NavMeshSurface로 두 슬롯을 함께 구우면 <b>가능하기는
/// 하다</b> — 다만 이 기능에서 증명하려는 것은 경로탐색이 아니라 <b>시야 판정</b>이라
/// 의도적으로 범위 밖에 뒀다. 못 하는 게 아니라 여기에 깊이를 쓰지 않기로 한 것이다.
///
/// 대신 <b>충돌 응답까지는</b> 처리한다(CharacterController). 장애물을 그냥 통과해
/// 버리면 '보고 멈춘다'는 규칙 자체가 우스워지기 때문이다. 막히면 돌아가지는
/// 않고 표면을 타고 미끄러진다 — 딱 거기까지가 이 기능의 선이다.
///
/// ── 속도 설정의 의미 ──────────────────────────────────
/// moveSpeed는 <b>플레이어보다 빨라야</b> 한다(기본 2.5 vs 3.2). 느리면 앞만 보고
/// 걸어도 절대 안 잡혀 긴장이 생기지 않는다. 빠르게 두면 이런 딜레마가 생긴다.
///   • 앞만 보고 걷는다  → 빠르지만 등 뒤에서 계속 좁혀온다
///   • 돌아본다          → 멈춰 세우지만 내 발도 느려진다
///
/// ── 배치 ──────────────────────────────────────────────
/// 씬 단일 존재(풀링되지 않음). 모듈이 몇 개 켜지든 그것은 한 마리여야 하므로,
/// 특정 방 모듈의 자식이 아니라 씬 루트에 고정으로 둔다. 어느 방에 세울지는
/// 매번 <see cref="Appear"/>에 그 방의 스폰 지점을 넘겨서 정한다.
///
/// <b>로직용 루트 / 아트용 자식</b>으로 나눈다.
///
///   Stalker        ← 이 스크립트. 무회전, 피벗은 발바닥. 회전을 덮어써도 안전
///   └── Model      ← 시체 메시. 세우는 보정(z 90°)을 여기가 들고 있는다
///       └── 표본점(머리·가슴·발)
///
/// 나누지 않으면 Update의 LookRotation이 세우는 보정을 매 프레임 지워 <b>시체가
/// 누운 채로 걸어온다.</b>
///
/// 레이어는 <b>occluders 마스크 밖</b>에 둬야 한다. 자기 캡슐이 마스크 안에 있으면
/// 차폐 검사에서 자기 몸에 걸려 영영 '안 보임'이 된다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Stalker : MonoBehaviour
{
    [Header("몸")]
    [Tooltip("켜고 끌 몸(자식 오브젝트 권장). 비우면 이 오브젝트의 렌더러만 껐다 켠다.")]
    [SerializeField] private GameObject body;
    [Tooltip("화면 안에 있는지 판정할 렌더러들.")]
    [SerializeField] private Renderer[] visibilityRenderers;
    [Tooltip("벽에 가렸는지 검사할 표본점들(머리·어깨·중심 등). 많을수록 정확하다.")]
    [SerializeField] private Transform[] samplePoints;

    [Header("행동")]
    [Tooltip("다가오는 속도(m/s). 플레이어(기본 2.5)보다 빨라야 긴장이 생긴다.")]
    [SerializeField] private float moveSpeed = 3.2f;
    [Tooltip("이 거리 안에 들어오면 잡힌 것으로 친다(m).")]
    [SerializeField] private float catchDistance = 1.3f;
    [Tooltip("시야를 가로막는 것으로 칠 레이어(벽·문 등). <b>Player와 추격자 자신은 반드시 빼야 한다.</b>")]
    [SerializeField] private LayerMask occluders = 1;   // Default

    [Header("디버그")]
    [Tooltip("시야 판정이 바뀔 때마다 콘솔에 한 줄 남긴다. 원인 추적이 끝나면 끄면 된다.")]
    [SerializeField] private bool logVisibility = true;

    private CharacterController controller;
    private Camera eye;
    private Transform player;
    private bool active;

    // [디버그] 마지막 프레임의 시야 판정 결과. '쳐다봤는데 안 멈춘다'가
    // 화면 밖 판정 탓인지, 무언가에 가려진 탓인지 눈으로 가르려고 남긴다.
    private bool lastOnScreen;
    private int lastClearSamples;
    private string lastBlocker;
    private string lastReportedState;

    /// <summary>지금 쫓아오고 있는지.</summary>
    public bool IsActive => active;

    /// <summary>[디버그] 왜 안 보이는지 추적하기 위한 현재 상태 한 줄.</summary>
    public string DebugState
    {
        get
        {
            if (!active) return "비활성";

            int on = 0, total = 0;
            foreach (var r in visibilityRenderers)
            {
                if (r == null) continue;
                total++;
                if (r.enabled && r.gameObject.activeInHierarchy) on++;
            }

            Vector3 p = transform.position;
            string dist = player != null ? $"{Vector3.Distance(p, player.position):F1}m" : "?";

            // 세워둔 자세가 유지되는지 — 이 에셋은 z가 90 근처여야 서 있다.
            Vector3 e = transform.eulerAngles;

            // 왜 안 멈추는지 한 줄로 가른다.
            string seen;
            if (!lastOnScreen) seen = "<color=#ffcc55>화면 밖</color>";
            else if (lastClearSamples > 0) seen = $"<color=#77dd77>보임 (뚫린 표본 {lastClearSamples})</color>";
            else seen = $"<color=#ff5555>화면엔 있으나 가림 — {lastBlocker ?? "?"}</color>";

            return $"활성 · 위치({p.x:F1}, <b>y {p.y:F1}</b>, {p.z:F1}) · 회전(x {e.x:F0}, y {e.y:F0}, <b>z {e.z:F0}</b>)\n" +
                   $"          거리 {dist} · 렌더러 {on}/{total} 켜짐 · {seen}";
        }
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        SetBodyVisible(false);
    }

    /// <summary>
    /// 몸을 보이거나 감춘다.
    ///
    /// ── 자기 자신은 SetActive로 끄면 안 된다 ──────────────
    /// Body를 따로 두지 않으면 body가 곧 이 오브젝트인데, 그걸 SetActive(false)
    /// 하면 <b>Update가 멈춘다.</b> 게다가 비활성 오브젝트는 Awake조차 돌지 않아
    /// (에디터에서 미리 꺼두면) 등장 자리를 기억하지 못하고 원점에 나타난다.
    /// 그래서 이 경우엔 오브젝트 대신 <b>렌더러만</b> 끈다.
    /// </summary>
    private void SetBodyVisible(bool visible)
    {
        if (body != null && body != gameObject)
        {
            body.SetActive(visible);
            return;
        }

        foreach (var r in visibilityRenderers)
            if (r != null) r.enabled = visible;
    }

    /// <summary>
    /// 등장시킨다. <paramref name="spawnPoint"/> 위치로 옮기고 몸을 켠다.
    ///
    /// 씬 단일 존재라 어느 방에 세울지 스스로는 모른다 — 호출하는 쪽(GameManager)이
    /// 그 방의 스폰 지점(RoomModule.StalkerSpawnPoint)을 매번 넘겨준다.
    /// </summary>
    public void Appear(Transform spawnPoint)
    {
        MoveToSpawn(spawnPoint);

        SetBodyVisible(true);
        active = true;
        lastReportedState = null;   // 새 등장이니 판정 로그를 처음부터 다시 남긴다

        Debug.Log($"[Stalker] 등장 — 월드 위치 {transform.position}, {DebugState}", this);
    }

    /// <summary>물러가게 한다 (방이 바뀔 때 등). 비활성 상태라 위치는 상관없어 그대로 둔다.</summary>
    public void Vanish()
    {
        active = false;
        SetBodyVisible(false);
    }

    /// <summary>
    /// 지정한 스폰 지점으로 옮긴다.
    ///
    /// ── 함정: 기준점의 '로컬' 좌표를 그대로 복사하면 안 된다 ──────
    /// spawnPoint와 이 오브젝트의 <b>부모가 다르면</b> 로컬 좌표계가 서로 달라,
    /// 복사한 값이 부모 오프셋만큼 어긋난 자리로 간다. 회전도 같이 덮인다.
    ///
    /// 그래서 <b>월드</b> 자세를 읽어 옮긴다 (TransformAnomaly와 같은 이유) — 이
    /// 오브젝트는 씬 루트에 고정이고 spawnPoint는 활성 모듈 안에 있어 둘의 부모가
    /// 다르므로, 로컬 좌표 복사는 애초에 성립하지 않는다.
    /// </summary>
    private void MoveToSpawn(Transform spawnPoint)
    {
        if (spawnPoint == null)
        {
            Debug.LogError("[Stalker] spawnPoint가 비어 있어 등장 위치를 정할 수 없습니다.", this);
            return;
        }

        // CharacterController는 자기 위치를 스스로 관리해서, 켜진 채로 transform을
        // 바꾸면 무시되거나 충돌로 튕긴다. 순간이동 순간에만 잠깐 끈다.
        // (PlayerTeleporter가 쓰는 것과 같은 처리 — 같은 문제라 같은 방식으로 푼다)
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        controller.enabled = true;
    }

    private void Update()
    {
        if (!active) return;
        if (!EnsureReferences()) return;

        // 판정은 <b>언제나</b> 돌려 둔다. 피신 중이라 안 움직이는 건지 보여서 멈춘
        // 건지를 디버그 표시판에서 구분하려면, 일찍 빠져나가기 전에 값을 갱신해야 한다.
        bool seen = IsSeenByPlayer();
        ReportVisibility();

        // 문을 닫고 피신했으면 다가오지 못한다.
        // 사라지지는 않는다 — 이번 방의 <b>이상현상</b>이므로 판정이 끝날 때까지 그대로 있어야 한다.
        // 문이 열려 있으면 피신으로 치지 않으므로 그대로 들어와 잡는다.
        if (GameManager.Instance != null && GameManager.Instance.PlayerIsSheltered) return;

        // 보이는 동안에는 아무것도 하지 않는다 — 위치도 회전도 그대로.
        if (seen) return;

        // 수평 거리만 본다. 높이 차 때문에 영영 못 잡는 일이 없도록.
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        if (distance <= catchDistance)
        {
            active = false;
            if (GameManager.Instance != null) GameManager.Instance.OnCaughtByStalker();
            return;
        }

        if (distance > 0.001f)
        {
            Vector3 direction = toPlayer / distance;

            // 좌표를 직접 더하지 않고 컨트롤러에 <b>요청</b>한다 — 가는 길에 뭐가 있는지
            // 엔진이 검사해서, 막히면 뚫는 대신 <b>표면을 타고 미끄러진다.</b>
            //
            // ── 여기까지만 하는 이유 ────────────────────────
            // 이건 <b>충돌 응답</b>이지 경로탐색이 아니다. 막히면 돌아가지 않고 밀린다.
            // 이 기능에서 증명하려는 것은 경로탐색이 아니라 <b>시야 판정</b>이라
            // NavMesh는 의도적으로 범위 밖에 뒀다. 다만 장애물을 통과해버리면
            // '보고 멈춘다'는 규칙 자체가 우스워지므로 충돌까지는 처리한다.
            controller.Move(direction * moveSpeed * Time.deltaTime);

            // 이 오브젝트는 <b>로직용 루트</b>라 회전을 통째로 덮어써도 안전하다.
            // 모델의 세우는 보정(이 시체 에셋은 z축 90°)은 자식 Model이 들고 있어야 한다 —
            // 루트에 직접 메시를 두면 여기서 그 보정이 지워져 누운 채로 걸어온다.
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    /// <summary>
    /// 플레이어 눈에 실제로 보이는지 <b>두 단계</b>로 판정한다.
    ///
    /// ① 화면 안에 있는가 — 카메라 절두체 검사
    /// ② 벽에 가리지 않았는가 — 표본점까지 시선이 뚫리는지 검사
    ///
    /// 둘은 다른 문제다. 벽 너머에 있어도 절두체 안에는 들어오기 때문에,
    /// ①만 보면 벽 뒤에서 멈춰 서서 영영 다가오지 못한다.
    /// </summary>
    private bool IsSeenByPlayer()
    {
        // ① 절두체 — 화면 밖이면 볼 수 없다.
        var planes = GeometryUtility.CalculateFrustumPlanes(eye);
        bool onScreen = false;
        foreach (var r in visibilityRenderers)
        {
            if (r == null) continue;
            if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) { onScreen = true; break; }
        }
        lastOnScreen = onScreen;
        lastClearSamples = 0;
        lastBlocker = null;
        if (!onScreen) return false;

        // ② 차폐 — 표본점 중 하나라도 시선이 닿으면 '보인다'로 친다.
        //
        // 중심점 하나만 검사하면, 몸 절반이 문틈으로 빤히 보이는데도 중심이
        // 가렸다는 이유로 움직여버려 바로 들통난다. 그래서 여러 점을 검사한다.
        //
        // 첫 성공에서 곧장 빠져나가지 않고 끝까지 세는 이유는 디버그용이다 —
        // '몇 개가 뚫렸나 / 무엇에 막혔나'를 알아야 안 멈추는 원인을 가릴 수 있다.
        // 표본점은 많아야 몇 개라 비용은 무시할 만하다.
        //
        // ⚠ occluders 마스크에서 <b>Player와 추격자 자신은 빼둬야 한다.</b> 시선의
        // 출발점인 카메라가 플레이어 몸 안에 있어, 마스크에 남겨두면 자기 몸이
        // 첫 히트로 잡혀 <b>정면으로 쳐다봐도 영영 '가려짐'</b>이 된다.
        Vector3 from = eye.transform.position;
        foreach (var p in samplePoints)
        {
            if (p == null) continue;

            if (Physics.Linecast(from, p.position, out RaycastHit hit, occluders, QueryTriggerInteraction.Ignore))
            {
                // 무엇에 막혔는지는 처음 하나만 남겨도 원인 찾기엔 충분하다.
                lastBlocker ??= hit.collider != null ? hit.collider.name : "?";
                continue;
            }

            lastClearSamples++;
        }
        return lastClearSamples > 0;
    }

    /// <summary>
    /// [디버그] 시야 판정이 <b>바뀔 때만</b> 콘솔에 한 줄 남긴다.
    ///
    /// 매 프레임 찍으면 콘솔이 막혀 정작 볼 것을 못 본다. 그렇다고 화면 표시판만
    /// 두면 '쳐다본 그 순간' 무엇이 막았는지가 로그에 안 남아 나중에 되짚을 수 없다.
    /// 그래서 상태가 바뀌는 지점만 남긴다 — 어느 거리에서 무엇에 가렸는지가 드러난다.
    /// </summary>
    private void ReportVisibility()
    {
        if (!logVisibility) return;

        string now;
        if (!lastOnScreen) now = "화면 밖 (절두체에서 탈락)";
        else if (lastClearSamples > 0) now = $"보임 — 뚫린 표본 {lastClearSamples}개";
        else now = $"화면엔 있으나 가림 — {lastBlocker ?? "?"}";

        if (now == lastReportedState) return;
        lastReportedState = now;

        float d = player != null ? Vector3.Distance(transform.position, player.position) : -1f;
        Debug.Log($"[Stalker] 시야 판정 → {now} · 거리 {d:F1}m · 내 위치 {transform.position}", this);
    }

    // 카메라·플레이어는 씬에서 한 번만 찾아 캐싱한다.
    private bool EnsureReferences()
    {
        if (eye == null) eye = Camera.main;
        if (player == null)
        {
            var teleporter = FindAnyObjectByType<PlayerTeleporter>();
            if (teleporter != null) player = teleporter.transform;
        }
        return eye != null && player != null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, catchDistance);
    }
}
