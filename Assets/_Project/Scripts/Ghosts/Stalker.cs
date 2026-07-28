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
/// 남으므로 맞지 않는다. 애초에 이 추격자는 <b>경로탐색이 아니라 시야 판정</b>이
/// 핵심이라 NavMesh가 필요 없다. 모듈 자식으로 두면 건물을 따라 함께 움직인다.
///
/// ── 속도 설정의 의미 ──────────────────────────────────
/// moveSpeed는 <b>플레이어보다 빨라야</b> 한다(기본 2.5 vs 3.2). 느리면 앞만 보고
/// 걸어도 절대 안 잡혀 긴장이 생기지 않는다. 빠르게 두면 이런 딜레마가 생긴다.
///   • 앞만 보고 걷는다  → 빠르지만 등 뒤에서 계속 좁혀온다
///   • 돌아본다          → 멈춰 세우지만 내 발도 느려진다
///
/// 배치: 방 모듈 프리팹 자식. 콜라이더는 두지 않는다(차폐 검사에 자기 몸이 걸린다).
/// </summary>
public class Stalker : MonoBehaviour
{
    [Header("몸")]
    [Tooltip("켜고 끌 몸(자식 오브젝트 권장). 비우면 이 오브젝트의 렌더러만 껐다 켠다.")]
    [SerializeField] private GameObject body;
    [Tooltip("화면 안에 있는지 판정할 렌더러들.")]
    [SerializeField] private Renderer[] visibilityRenderers;
    [Tooltip("벽에 가렸는지 검사할 표본점들(머리·어깨·중심 등). 많을수록 정확하다.")]
    [SerializeField] private Transform[] samplePoints;

    [Header("등장")]
    [Tooltip("등장 위치. 비우면 이 오브젝트의 처음 자리에서 나타난다.")]
    [SerializeField] private Transform spawnPoint;

    [Header("행동")]
    [Tooltip("다가오는 속도(m/s). 플레이어(기본 2.5)보다 빨라야 긴장이 생긴다.")]
    [SerializeField] private float moveSpeed = 3.2f;
    [Tooltip("이 거리 안에 들어오면 잡힌 것으로 친다(m).")]
    [SerializeField] private float catchDistance = 1.3f;
    [Tooltip("시야를 가로막는 것으로 칠 레이어(벽·문 등). 자기 자신은 빼야 한다.")]
    [SerializeField] private LayerMask occluders = 1;   // Default

    private Camera eye;
    private Transform player;
    private Vector3 startLocalPosition;
    private Quaternion startLocalRotation;
    private bool active;

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
            return $"활성 · 위치({p.x:F1}, <b>y {p.y:F1}</b>, {p.z:F1}) · 거리 {dist} · 렌더러 {on}/{total} 켜짐";
        }
    }

    private void Awake()
    {
        // 등장 자리를 로컬로 기억한다 — 건물이 슬롯을 옮겨도 유효하도록.
        // (월드로 저장하면 건물 이동 후 옛 자리에 나타난다)
        Transform origin = spawnPoint != null ? spawnPoint : transform;
        startLocalPosition = origin.localPosition;
        startLocalRotation = origin.localRotation;

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

    /// <summary>등장시킨다. 등장 자리로 돌려놓고 몸을 켠다.</summary>
    public void Appear()
    {
        transform.localPosition = startLocalPosition;
        transform.localRotation = startLocalRotation;

        SetBodyVisible(true);
        active = true;

        Debug.Log($"[Stalker] 등장 — 월드 위치 {transform.position}, {DebugState}", this);
    }

    /// <summary>물러가게 한다 (방이 바뀔 때 등).</summary>
    public void Vanish()
    {
        active = false;
        SetBodyVisible(false);
        transform.localPosition = startLocalPosition;
        transform.localRotation = startLocalRotation;
    }

    private void Update()
    {
        if (!active) return;
        if (!EnsureReferences()) return;

        // 문을 닫고 피신했으면 다가오지 못한다.
        // 사라지지는 않는다 — 이번 방의 <b>이상현상</b>이므로 판정이 끝날 때까지 그대로 있어야 한다.
        // 문이 열려 있으면 피신으로 치지 않으므로 그대로 들어와 잡는다.
        if (GameManager.Instance != null && GameManager.Instance.PlayerIsSheltered) return;

        // 보이는 동안에는 아무것도 하지 않는다 — 위치도 회전도 그대로.
        if (IsSeenByPlayer()) return;

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
            transform.position += direction * moveSpeed * Time.deltaTime;
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
        if (!onScreen) return false;

        // ② 차폐 — 표본점 중 하나라도 시선이 닿으면 '보인다'로 친다.
        //
        // 중심점 하나만 검사하면, 몸 절반이 문틈으로 빤히 보이는데도 중심이
        // 가렸다는 이유로 움직여버려 바로 들통난다. 그래서 여러 점을 검사한다.
        Vector3 from = eye.transform.position;
        foreach (var p in samplePoints)
        {
            if (p == null) continue;
            if (!Physics.Linecast(from, p.position, occluders, QueryTriggerInteraction.Ignore))
                return true;
        }
        return false;
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
