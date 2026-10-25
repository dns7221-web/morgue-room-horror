using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 은폐 검사기 — <b>"재활용이 안 보였다"를 사람 눈이 아니라 코드가 확인한다.</b>
///
/// ── 왜 필요한가 ───────────────────────────────────────
/// 이 격자의 핵심 주장은 "칸이 등 뒤에서 재배치되는데 화면엔 아무 일도 일어나지
/// 않는다"다. 그런데 그 주장의 근거는 오랫동안 <b>사람이 보고 "안 보이네" 한 것</b>이
/// 전부였다 — Scene 뷰와 Game 뷰를 나란히 띄워 눈으로 확인한 것.
///
/// 문제는 <b>안 보이는 사건은 놓쳐도 모른다</b>는 것이다. 한 프레임 삐져나온 것을
/// 눈으로 잡을 수는 없고, 나중에 칸 디자인을 바꾸거나(섬을 옮기거나, 문 위치를
/// 바꾸거나) 한 변을 키웠을 때 은폐가 깨져도 알 방법이 없다. 실제로 서랍이 벽을
/// 뚫은 것도 시연 영상을 프레임 단위로 돌려보다 찾았다.
///
/// 그래서 이 프로젝트가 이미 쓰고 있는 방법을 메인 기능 본체에 적용한다 —
/// <b>안 보이는 사고를 숫자로 드러낸다.</b> 추격자가 안 멈출 때 로그로 범인을
/// 잡았고, 서랍이 삐져나온 것은 GridDebugPanel의 크기 줄이 잡았다. 은폐도 같다.
///
/// ── 2단 검사 ──────────────────────────────────────────
/// ① <b>차폐 불변식</b> (결정적, 싸다) — 플레이어 칸에서 그 칸 쪽으로 나가는 첫
///    경계의 문이 닫혀 있으면 그 방향으로는 아무것도 볼 수 없다. 거리나 기하가
///    아니라 <b>차폐</b>로 증명되는 형태라 반박할 여지가 없다
///    (<see cref="GridManager.IsSealedToward"/>).
///
/// ② <b>시야 판정</b> (①이 깨졌을 때만) — 문이 하나라도 열려 있으면 실제로 보였는지
///    본다. <c>Stalker.IsSeenByPlayer()</c>와 <b>같은 2단 판정</b>을 쓴다:
///    카메라 절두체 검사 → 표본점까지 Linecast.
///
/// ── 표본점을 새로 만들지 않는다 ───────────────────────
/// 칸의 사방 <see cref="RoomSocket"/>을 그대로 쓴다. 칸 설계 규칙대로 소켓은
/// <b>문 정중앙·벽 바깥면</b>에 있어서 곧 문간이고, 칸이 보이려면 반드시 문간을
/// 지나야 하므로 정답 표본이다. 덕분에 프리팹·씬을 하나도 안 고치고 붙는다.
///
/// ── 비용 ──────────────────────────────────────────────
/// 매 프레임이 아니라 <b>재활용 순간에만</b> 돈다. 기본 3x3이면 칸 3개 × 소켓 4개 =
/// Linecast 12회, 한 칸 넘어갈 때 두 번(사라질 때·나타날 때). 무시할 만하다.
///
/// 배치: <see cref="GridManager"/>와 같은 오브젝트에 붙이고 Coverage Audit 슬롯에 꽂는다.
/// 비워 두면 검사만 안 돌고 격자는 그대로 굴러간다.
/// </summary>
public class CoverageAudit : MonoBehaviour
{
    /// <summary>검사 시점. 사라지는 것과 나타나는 것은 다른 사건이라 따로 센다.</summary>
    public enum Phase
    {
        /// <summary>옮기기 직전 — 그 자리에서 칸이 사라지는 것을 봤는가.</summary>
        Leaving,
        /// <summary>옮긴 직후 — 새 자리에 칸이 나타나는 것을 봤는가.</summary>
        Arriving,
    }

    [Header("시야 판정")]
    [Tooltip("시야를 가로막는 것으로 칠 레이어(벽·문). Stalker의 Occluders와 같은 값을 쓸 것 — 보통 SightBlocker 하나.")]
    [SerializeField] private LayerMask occluders = 1 << 7;   // SightBlocker

    [Tooltip("표본점을 소켓에서 칸 안쪽으로 이 거리만큼 들인다(m). 0이면 점이 문짝 표면에 딱 붙어 Linecast가 그 문을 못 잡는다.")]
    [SerializeField, Min(0.05f)] private float sampleInset = 0.5f;

    [Tooltip("표본점 높이 — 칸 바닥(루트 Y) 기준(m). 눈높이 근처로 둘 것. 바닥에 두면 바닥에 가려 늘 '안 보임'이 된다.")]
    [SerializeField] private float sampleHeight = 1.6f;

    [Header("보고")]
    [Tooltip("위반이 나면 콘솔에 한 줄 남긴다.")]
    [SerializeField] private bool logViolations = true;

    private Camera eye;

    /// <summary>지금까지 검사한 횟수 (재활용 1회당 2 — 사라질 때·나타날 때).</summary>
    public int CheckCount { get; private set; }

    /// <summary>은폐가 깨진 횟수. <b>0이어야 정상이다.</b></summary>
    public int ViolationCount { get; private set; }

    /// <summary>마지막 위반 한 줄 (표시판·시연 뷰가 읽어간다). 없으면 null.</summary>
    public string LastViolation { get; private set; }

    /// <summary>
    /// 옮겨지는 칸들이 플레이어에게 가려져 있었는지 검사한다.
    ///
    /// <paramref name="cells"/>의 <c>Coord</c>는 호출 시점의 좌표를 그대로 쓴다 —
    /// <see cref="Phase.Leaving"/>에서는 아직 옛 좌표, <see cref="Phase.Arriving"/>에서는
    /// 이미 새 좌표다. 그래서 두 시점이 같은 코드로 처리된다.
    /// </summary>
    public void Check(IReadOnlyList<GridCell> cells, Vector2Int playerCoord, Phase phase)
    {
        if (cells == null || cells.Count == 0) return;

        var grid = GridManager.Instance;
        if (grid == null) return;

        CheckCount++;

        foreach (var cell in cells)
        {
            if (cell == null) continue;

            // ① 차폐 불변식 — 그 방향으로 나가는 문이 닫혀 있으면 볼 방법이 없다.
            //    여기서 통과하면 시야 판정을 돌릴 필요조차 없다.
            if (grid.IsSealedToward(playerCoord, cell.Coord)) continue;

            // ② 문이 열려 있다 → 실제로 보였는지 확인한다.
            if (!TryFindVisibleSocket(cell, out string socketName, out float distance)) continue;

            ViolationCount++;
            LastViolation =
                $"{PhaseName(phase)} · 칸 ({cell.Coord.x}, {cell.Coord.y}) · " +
                $"{socketName} 문간이 {distance:F1}m에서 보였다";

            if (logViolations)
            {
                Debug.LogError(
                    $"[CoverageAudit] 은폐 위반 ({ViolationCount}회) — {LastViolation}\n" +
                    "플레이어 칸의 그 방향 문이 열린 채로 재활용이 일어났습니다. " +
                    "지나온 문을 닫는 시점(GridManager.OnPlayerEnteredCell) 또는 칸 설계(섬의 가림)를 확인할 것.",
                    this);
            }
        }
    }

    /// <summary>검사 수치를 처음으로 되돌린다 (시연 녹화를 다시 시작할 때 등).</summary>
    public void ResetCounters()
    {
        CheckCount = 0;
        ViolationCount = 0;
        LastViolation = null;
    }

    /// <summary>
    /// 이 칸의 문간(소켓) 중 <b>플레이어 눈에 실제로 닿는 것</b>이 있는지 찾는다.
    ///
    /// <c>Stalker.IsSeenByPlayer()</c>와 같은 2단 판정이다 — 절두체로 화면
    /// 안인지 먼저 거르고(화면 밖이면 아무리 뚫려 있어도 못 본다), 그다음 Linecast로
    /// 벽·문에 가렸는지 본다. 하나라도 닿으면 그 자리에서 끝낸다 (Stalker가 디버그
    /// 때문에 끝까지 세는 것과 달리, 여기선 "하나라도 보였다"가 곧 위반이다).
    /// </summary>
    private bool TryFindVisibleSocket(GridCell cell, out string socketName, out float distance)
    {
        socketName = null;
        distance = 0f;

        if (eye == null) eye = Camera.main;
        if (eye == null) return false;   // 카메라가 없으면 판정 자체가 성립하지 않는다

        var planes = GeometryUtility.CalculateFrustumPlanes(eye);
        Vector3 from = eye.transform.position;

        foreach (var dir in GridCellDirections)
        {
            var socket = cell.GetSocket(dir);
            if (socket == null) continue;

            Vector3 point = SamplePointOf(cell, socket);

            // 절두체 — 점 하나라 크기 없는 Bounds로 검사한다.
            if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(point, Vector3.zero))) continue;

            // 차폐 — 벽·문에 막히면 안 보이는 것이다.
            if (Physics.Linecast(from, point, occluders, QueryTriggerInteraction.Ignore)) continue;

            socketName = NameOf(dir);
            distance = Vector3.Distance(from, point);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 소켓을 표본점으로 바꾼다.
    ///
    /// ── 두 가지 보정이 둘 다 필요하다 ─────────────────────
    /// ① <b>안쪽으로 들인다</b> — 소켓은 벽 바깥면에 붙어 있어서, 그 자리를 그대로
    ///    쏘면 점이 문짝 표면과 같은 평면에 놓인다. 그러면 Linecast가 그 문을
    ///    잡을지 말지가 부동소수점 운에 달려, 닫힌 문 뒤인데도 "보였다"가 나온다.
    ///    소켓 forward는 칸 바깥을 향하므로(RoomSocket 규약) 그 반대로 밀면 칸 안쪽이다.
    ///
    /// ② <b>눈높이로 올린다</b> — 소켓이 바닥 높이에 있으면 점이 바닥 안이나 바로 위라
    ///    늘 바닥에 가려 "안 보임"이 된다. 검사기가 아무것도 못 잡게 되는 쪽이라
    ///    더 위험하다. 칸 루트 Y(=바닥, 칸 설계 규칙 1)에서 눈높이만큼 올린다.
    /// </summary>
    private Vector3 SamplePointOf(GridCell cell, RoomSocket socket)
    {
        Vector3 point = socket.transform.position - socket.transform.forward * sampleInset;
        point.y = cell.transform.position.y + sampleHeight;
        return point;
    }

    // 사방 방향. GridCell.CardinalDirection을 벡터로 바꿔 쓴다 —
    // 소켓을 꺼내는 GetSocket()이 벡터를 받기 때문.
    private static readonly Vector2Int[] GridCellDirections =
    {
        new(0, 1), new(1, 0), new(0, -1), new(-1, 0),
    };

    private static readonly string[] DirectionNames = { "북", "동", "남", "서" };

    private static string NameOf(Vector2Int dir)
    {
        for (int i = 0; i < GridCellDirections.Length; i++)
            if (GridCellDirections[i] == dir) return DirectionNames[i];
        return dir.ToString();
    }

    private static string PhaseName(Phase phase) =>
        phase == Phase.Leaving ? "사라지는 자리" : "나타나는 자리";
}
