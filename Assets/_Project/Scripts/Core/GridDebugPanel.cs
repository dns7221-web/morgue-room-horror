using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 3x3 격자 전용 상태 표시판. <see cref="GridManager"/>와 같은 오브젝트에 붙인다.
///
/// ── 왜 DebugPanel과 따로 두나 ─────────────────────────
/// 기존 <see cref="DebugPanel"/>은 <b>GameManager와 한 몸</b>이라(같은 오브젝트에
/// 붙어 있고, <c>GameManager.Instance</c>가 null이면 통째로 침묵한다) 격자만 켜고
/// 3안을 꺼둔 지금은 아무것도 보여주지 못한다. 격자 작업은 <b>GameManager 없이도</b>
/// 검증할 수 있어야 단독으로 굴릴 수 있으므로, 격자 쪽 눈은 따로 만든다.
///
/// ── 무엇을 보여주는가 ────────────────────────────────
/// 이 격자의 사고는 대부분 <b>화면에 안 나타난다.</b> 재활용은 원래 안 보여야
/// 정상이고, 칸이 겹치는 것도 그 자리에 가봐야 알며, 진입 판정이 빠진 것도
/// 한참 걷고 나서야 이상하다고 느낀다. 그래서 "일어나야 할 일이 일어났는가"를
/// 값으로 뽑아 늘 띄워 둔다 — DebugPanel이 추격자 연결 누락을 잡아준 것과 같은 역할.
///
/// 특히 <b>칸 몫 대비 지오메트리 크기</b> 줄이 핵심이다. 칸이 자기 몫보다 크면
/// 이웃 칸을 침범해 벽이 서로를 관통하는데, 그 증상(허공에 뜬 문짝, 천장을 뚫은
/// 조명)은 원인과 전혀 닮지 않아 추적이 어렵다. 숫자로 보면 한 줄이다.
/// </summary>
[RequireComponent(typeof(GridManager))]
public class GridDebugPanel : MonoBehaviour
{
    private static readonly Vector2Int[] Directions =
    {
        new(0, 1), new(1, 0), new(0, -1), new(-1, 0),
    };

    private static readonly string[] DirectionNames = { "N", "E", "S", "W" };

    [Header("On / Off")]
    [Tooltip("화면 좌상단 격자 표시판을 보여준다.")]
    [SerializeField] private bool showOverlay = true;
    [Tooltip("표시판을 껐다 켜는 키.")]
    [SerializeField] private Key toggleKey = Key.F6;

    [Tooltip("지오메트리가 칸 몫을 이만큼 넘어가면 경고한다 (m). 벽 두께만큼은 봐준다.")]
    [SerializeField] private float sizeTolerance = 0.5f;

    private GridManager grid;
    private Transform player;
    private GUIStyle style;

    // 칸 루트 기준 지오메트리 범위. 칸은 전부 같은 프리팹을 평행이동한 것뿐이라
    // 한 번만 재면 나머지는 위치만 더하면 된다 (매 프레임 렌더러를 훑지 않는다).
    private Bounds localBounds;
    private bool localBoundsMeasured;

    // 몫을 넘겼을 때 <b>어느 오브젝트가</b> 제일 멀리 뻗었는지. 크기가 초과라는 것만
    // 알면 50개짜리 칸에서 범인을 손으로 찾아야 한다 — 이름 한 줄이면 바로 끝난다.
    private string reachName = "";

    private void Awake()
    {
        grid = GetComponent<GridManager>();
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame)
            showOverlay = !showOverlay;
    }

    private void OnGUI()
    {
        if (!showOverlay || grid == null) return;

        style ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal = { textColor = Color.white },
            richText = true,
            wordWrap = false,   // 끄지 않으면 아래 CalcSize가 줄바꿈을 잘못 세어 판이 어긋난다
        };

        string text =
            $"<b>[GRID]</b>  F6 표시 끄기\n" +
            SpacingLine() + "\n" +
            SizeLine() + "\n" +
            CenterLine() + "\n" +
            AnomalyLine() + "\n" +
            RecycleLine() + "\n" +
            WiringLine() + "\n" +
            MiniMap();

        var size = style.CalcSize(new GUIContent(text));
        GUI.Box(new Rect(8, 8, size.x + 24, size.y + 16), GUIContent.none);
        GUI.Label(new Rect(16, 12, size.x + 8, size.y + 8), text, style);
    }

    /// <summary>실측된 칸 간격. 0이면 소켓이 없어 재기에 실패한 것이라 전부 한 자리에 겹친다.</summary>
    private string SpacingLine()
    {
        Vector2 s = grid.Spacing;
        if (s.x <= 0f || s.y <= 0f)
            return $"간격  <color=#ff5555>실측 실패 ({s.x:F2} x {s.y:F2}) — 사방 소켓을 확인할 것</color>";

        return $"간격  X <b>{s.x:F2}</b>   Z <b>{s.y:F2}</b>   (칸 하나가 차지해도 되는 몫)";
    }

    /// <summary>
    /// 칸 지오메트리가 자기 몫 안에 들어오는지. <b>넘으면 이웃 칸을 침범한다</b> —
    /// 벽이 서로를 관통하고, 다른 방의 문짝이 이 방 바닥 한복판에 서 있게 된다.
    /// </summary>
    private string SizeLine()
    {
        if (!TryGetLocalBounds(out var b)) return "크기  <color=#ffcc55>칸이 아직 없다</color>";

        Vector2 allowed = grid.Spacing;
        if (allowed.x <= 0f || allowed.y <= 0f) return "크기  간격 실측 실패라 검사 불가";

        float overX = b.size.x - allowed.x;
        float overZ = b.size.z - allowed.y;
        bool bad = overX > sizeTolerance || overZ > sizeTolerance;

        string verdict = bad
            ? $"<color=#ff5555>초과 (X {overX:+0.0;-0.0}, Z {overZ:+0.0;-0.0}) — 이웃 칸을 침범한다</color>"
            : "<color=#77dd77>몫 안에 들어옴</color>";

        if (!bad) return $"크기  실제 <b>{b.size.x:F1} x {b.size.z:F1}</b>   {verdict}";

        // 어느 <b>쪽으로</b> 넘쳤는지가 핵심이다. 크기만 보면 '뭔가 크다'까지밖에 모르는데,
        // 범위를 찍으면 +Z로 6.8m 삐져나갔다처럼 방향이 나와 찾을 자리가 좁혀진다.
        string range =
            $"\n범위  X <b>{b.min.x:+0.0;-0.0}</b> ~ <b>{b.max.x:+0.0;-0.0}</b>" +
            $"   Z <b>{b.min.z:+0.0;-0.0}</b> ~ <b>{b.max.z:+0.0;-0.0}</b>" +
            $"   (몫은 ±{allowed.x * 0.5f:F1} / ±{allowed.y * 0.5f:F1})";

        string culprit = reachName.Length > 0 ? $"\n범인  {reachName}" : "";

        return $"크기  실제 <b>{b.size.x:F1} x {b.size.z:F1}</b>   {verdict}{range}{culprit}";
    }

    /// <summary>
    /// 격자가 아는 중앙과 플레이어가 <b>실제로 서 있는</b> 칸이 같은지.
    /// 어긋나면 문간 트리거(CellBoundaryTrigger)가 진입을 놓친 것이고,
    /// 그 뒤로는 재활용이 통째로 엉뚱한 행을 옮기게 된다.
    /// </summary>
    private string CenterLine()
    {
        Vector2Int center = grid.CenterCoord;
        string line = $"중앙  <b>({center.x}, {center.y})</b>   칸 {grid.Cells.Count}개";

        if (!TryGetPlayer(out var p)) return line + "   플레이어 <color=#ffcc55>못 찾음</color>";

        if (!TryFindPlayerCell(p.position, out var actual))
            return line + "   플레이어 <color=#ff5555>격자 밖에 있다</color>";

        string match = actual == center
            ? "<color=#77dd77>일치</color>"
            : $"<color=#ff5555>불일치 — 진입 판정을 놓쳤다</color>";

        return line + $"\n실제  플레이어가 선 칸 <b>({actual.x}, {actual.y})</b>   {match}";
    }

    /// <summary>
    /// 지금 선 칸의 정답 — 없음 / 있음(변형 이름) / 그것.
    ///
    /// 판정 로직은 HasAnomaly(있음·없음)만 보고 <b>무엇</b>인지는 몰라도 되지만,
    /// 개발 중엔 이 줄이 없으면 "이상현상이 안 보이는 게 연출 버그인지, 애초에
    /// 안 걸린 것인지"를 화면만 보고는 가를 수 없다. 정답을 미리 알려주는 치트지만
    /// 어차피 F6로 껐다 켤 수 있는 디버그 표시판이다.
    /// </summary>
    private string AnomalyLine()
    {
        var cell = grid.CenterCell;
        if (cell == null) return "이상현상  <color=#ffcc55>중앙 칸이 없다</color>";

        string label = cell.AnomalyLabel;
        string color = label switch
        {
            "없음" => "#77dd77",
            "그것" => "#ff5555",
            _ => "#ffcc55",   // 변형 이름 — 눈에 띄되 위험색은 아니게
        };

        return $"이상현상  <color={color}><b>{label}</b></color>";
    }

    /// <summary>마지막 재활용. 한 칸 넘어갈 때마다 갱신돼야 정상이다.</summary>
    private string RecycleLine()
    {
        var r = grid.LastRecycle;
        if (r.direction == Vector2Int.zero)
            return "재활용  <color=#ffcc55>아직 없음</color> — 옆 칸으로 넘어가면 갱신된다";

        string dir = NameOf(r.direction);
        return $"재활용  {dir} 방향   ({r.from.x}, {r.from.y}) → ({r.to.x}, {r.to.y})   {Time.time - r.time:F1}초 전";
    }

    /// <summary>
    /// 칸 프리팹의 연결 누락 검사. 소켓·문·스폰은 <b>비어 있어도 조용히 굴러가다가</b>
    /// 엉뚱한 곳에서 터지는 종류라, 있는지 없는지를 대놓고 띄운다.
    /// </summary>
    private string WiringLine()
    {
        var cell = grid.CenterCell;
        if (cell == null) return "연결  <color=#ffcc55>중앙 칸이 없다</color>";

        int sockets = 0, doors = 0;
        string missing = "";
        for (int i = 0; i < Directions.Length; i++)
        {
            if (cell.GetSocket(Directions[i]) != null) sockets++;
            else missing += DirectionNames[i];

            doors += cell.GetDoors(Directions[i]).Length;
        }

        string socketPart = sockets == 4
            ? "<color=#77dd77>소켓 4/4</color>"
            : $"<color=#ff5555>소켓 {sockets}/4 (없음: {missing})</color>";

        string doorPart = doors > 0
            ? $"문 {doors}짝"
            : "<color=#ff5555>문 0짝 — 넘어갈 수 없다</color>";

        string spawnPart = cell.SpawnPoint != null
            ? "스폰 O"
            : "<color=#ff5555>스폰 없음</color>";

        int stalkers = FindObjectsByType<Stalker>(FindObjectsSortMode.None).Length;
        string stalkerPart = stalkers == 1
            ? "<color=#77dd77>추격자 1마리</color>"
            : $"<color=#ff5555>추격자 {stalkers}마리</color>";

        return $"연결  {socketPart}   {doorPart}   {spawnPart}   {stalkerPart}";
    }

    /// <summary>
    /// 3x3을 좌표 그대로 그린다. 재활용이 "배열이 한 칸 굴러가는" 것이라,
    /// 숫자가 통째로 밀려나는 모습이 눈에 보여야 의도대로 도는지 알 수 있다.
    /// </summary>
    private string MiniMap()
    {
        Vector2Int c = grid.CenterCoord;
        string map = "";

        // 위(북 = y+1)가 화면 위로 오게 그린다.
        for (int y = 1; y >= -1; y--)
        {
            string row = "  ";
            for (int x = -1; x <= 1; x++)
            {
                var coord = new Vector2Int(c.x + x, c.y + y);
                bool exists = grid.Cells.ContainsKey(coord);
                string label = $"{coord.x},{coord.y}";

                if (!exists) row += $"<color=#ff5555>[ 없음 ]</color> ";
                else if (x == 0 && y == 0) row += $"<b>[{label,5}]</b> ";
                else row += $" {label,5}  ";
            }
            map += row + "\n";
        }

        return map.TrimEnd('\n');
    }

    private static string NameOf(Vector2Int dir)
    {
        for (int i = 0; i < Directions.Length; i++)
            if (Directions[i] == dir) return DirectionNames[i];
        return dir.ToString();
    }

    private bool TryGetPlayer(out Transform t)
    {
        if (player == null)
        {
            var teleporter = FindAnyObjectByType<PlayerTeleporter>();
            player = teleporter != null ? teleporter.transform : null;
        }

        t = player;
        return t != null;
    }

    /// <summary>플레이어를 품고 있는 칸을 실제 좌표로 찾는다 (격자가 아는 중앙과 대조용).</summary>
    private bool TryFindPlayerCell(Vector3 position, out Vector2Int coord)
    {
        coord = default;
        if (!TryGetLocalBounds(out var b)) return false;

        // 칸 루트가 지오메트리 중심이 아닐 수 있어(프리팹 원점이 구석에 있는 등)
        // 좌표를 나눗셈으로 역산하지 않고, 실제 범위에 들어오는 칸을 찾는다.
        foreach (var kv in grid.Cells)
        {
            if (kv.Value == null) continue;

            Vector3 min = b.min + kv.Value.transform.position;
            Vector3 max = b.max + kv.Value.transform.position;

            if (position.x >= min.x && position.x <= max.x &&
                position.z >= min.z && position.z <= max.z)
            {
                coord = kv.Key;
                return true;
            }
        }

        return false;
    }

    /// <summary>칸 하나의 지오메트리 범위를 칸 루트 기준으로 잰다 (최초 1회만).</summary>
    private bool TryGetLocalBounds(out Bounds bounds)
    {
        bounds = localBounds;
        if (localBoundsMeasured) return true;

        var cell = grid.CenterCell;
        if (cell == null) return false;

        var renderers = cell.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        var world = renderers[0].bounds;
        float farthest = 0f;

        for (int i = 0; i < renderers.Length; i++)
        {
            world.Encapsulate(renderers[i].bounds);

            // 칸 루트에서 가로로 제일 멀리 뻗은 렌더러를 기억해 둔다. 초과가 났을 때
            // 이 이름 하나가 곧 "어디를 고쳐야 하나"의 답이다.
            Vector3 lo = renderers[i].bounds.min - cell.transform.position;
            Vector3 hi = renderers[i].bounds.max - cell.transform.position;
            float reach = Mathf.Max(Mathf.Max(Mathf.Abs(lo.x), Mathf.Abs(hi.x)),
                                    Mathf.Max(Mathf.Abs(lo.z), Mathf.Abs(hi.z)));

            if (reach <= farthest) continue;

            farthest = reach;

            // 소속 그룹까지 붙인다 — 바닥과 천장은 <b>이름이 같은 프리팹</b>이라
            // ("floor_ceiling_4x16") 이름만으로는 어느 쪽인지 갈리지 않는다.
            var parent = renderers[i].transform.parent;
            reachName = parent != null
                ? $"{parent.name}/{renderers[i].name}   (칸 중심에서 {reach:F1}m)"
                : $"{renderers[i].name}   (칸 중심에서 {reach:F1}m)";
        }

        // 루트 기준으로 옮겨 담는다 — 칸은 전부 같은 프리팹의 평행이동이라 이 값이 공용이다.
        localBounds = new Bounds(world.center - cell.transform.position, world.size);
        localBoundsMeasured = true;

        bounds = localBounds;
        return true;
    }
}
