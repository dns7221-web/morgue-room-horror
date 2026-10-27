using UnityEditor;
using UnityEngine;

/// <summary>
/// 칸의 구조물 레이어를 <b>역할별로</b> 칠하는 도구 — <c>Tools ▸ 칸 레이어 칠하기</c>.
///
/// ── 왜 필요했나 (겪은 버그) ───────────────────────────
/// <c>SightBlocker</c> 레이어는 추격자 문제 ③("쳐다봐도 안 멈춘다")을 고칠 때 만들어
/// 3안 건물(<c>HorrorZip</c>)의 벽·문에 <b>손으로</b> 넣은 것이다. <c>GridCell_v3</c>는 그 뒤에
/// <c>GridCellBuilder</c>가 조립했고 <b>도구가 레이어를 안 넣었다</b> — 사람이 손으로 하던
/// 일을 도구로 옮기면서 딸려오지 않았다. 그 결과:
/// <list type="bullet">
/// <item><see cref="CoverageAudit"/>이 막을 것을 못 찾아 40m 너머 문간까지 "보였다"로 오보</item>
/// <item><c>Stalker.IsSeenByPlayer</c>도 같은 마스크를 쓴다 — 아무것도 안 가리면 <b>항상 "보임"</b>이라
///       추격자가 벽 뒤에 있어도 영영 얼어붙는다</item>
/// </list>
///
/// ── 한 오브젝트는 레이어를 하나만 가진다 ─────────────
/// 처음엔 벽·문·섬을 통째로 <c>SightBlocker</c>로 칠했는데, <b>문짝과 키패드까지 같이 칠해져
/// E로 상호작용이 안 됐다.</b> <see cref="PlayerInteractor"/>가 <c>Interactable</c> 레이어만
/// 레이캐스트하기 때문이다.
///
/// 그래서 <b>역할로 가른다</b> — 조상 중에 <see cref="IInteractable"/>이 있으면
/// <c>Interactable</c>, 아니면 <c>SightBlocker</c>. 판정 기준을 <see cref="PlayerInteractor"/>가
/// 실제로 쓰는 <c>GetComponentInParent&lt;IInteractable&gt;()</c>와 <b>똑같이</b> 맞춰서,
/// "집을 수 있는 것은 반드시 Interactable"이 저절로 성립한다.
///
/// <b>그래서 문은 Interactable이다.</b> 문도 시야를 막아야 하므로,
/// <see cref="CoverageAudit"/>와 <c>Stalker</c>의 <c>Occluders</c> 마스크에
/// <c>SightBlocker</c>와 <c>Interactable</c>을 <b>둘 다</b> 넣어야 한다.
///
/// 여러 번 눌러도 결과가 같다(멱등) — 잘못 칠했으면 다시 누르면 제자리로 온다.
/// </summary>
internal static class CellLayerPainter
{
    private const string BlockerLayerName = "SightBlocker";
    private const string InteractableLayerName = "Interactable";

    /// <summary>
    /// 칠할 그룹 — 칸 루트 바로 아래 이 이름을 가진 자식의 <b>자손 전부</b>.
    ///
    /// <c>Walls</c> 안에 N/E/S/W가 있고 그 안에 벽·문틀·문짝이 들어 있다.
    /// <c>Island</c> 안에는 섬의 네 면과 <c>DrawerBox</c>·<c>Drawers</c>, 그리고 키패드가 있다 —
    /// 섬은 이 격자의 <b>은폐층</b>이라 시야를 막는 것이 본래 역할이므로 통째로 훑는다.
    ///
    /// <c>Floor</c>·<c>Ceiling</c>은 뺀다(가로로 보는 시선에 쓸모가 없다).
    /// <c>Props</c>·<c>Corpses</c>·<c>Lights</c>는 <b>절대 넣지 않는다</b> — 소품이 시야를 막으면
    /// 추격자가 의자 뒤에서 안 멈추는 그 사고가 재발한다.
    ///
    /// 이 구분은 추격자 <b>몸이 무엇에 막히는지</b>도 정한다. Stalker의 CharacterController가
    /// <c>Default</c>를 Exclude Layers로 빼 두어서, 여기서 칠한 벽·섬에는 막히고 안 칠한
    /// 소품은 통과한다. 소품을 칠하면 시야와 몸이 함께 어긋난다.
    /// </summary>
    private static readonly string[] StructureGroups = { "Walls", "Island" };

    [MenuItem("Tools/칸 레이어 칠하기/벽은 SightBlocker · 집는 것은 Interactable")]
    private static void Paint()
    {
        int blocker = LayerMask.NameToLayer(BlockerLayerName);
        int interactable = LayerMask.NameToLayer(InteractableLayerName);
        if (blocker < 0 || interactable < 0)
        {
            Debug.LogError($"[칸 레이어] '{BlockerLayerName}' 또는 '{InteractableLayerName}' 레이어가 없습니다 — " +
                           "Project Settings ▸ Tags and Layers에서 먼저 만들 것.");
            return;
        }

        var selected = Selection.activeObject as GameObject;
        if (selected == null)
        {
            Debug.LogError("[칸 레이어] 칸을 하나 고르고 실행하세요 (Project 창의 GridCell 프리팹 또는 씬의 칸).");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(selected);
        bool isPrefabAsset = !string.IsNullOrEmpty(assetPath);

        // 프리팹 에셋은 바로 고치면 안 된다 — 유니티가 권하는 대로 사본을 열어 고치고 저장한다.
        // 이렇게 열면 안에 중첩된 프리팹(문짝·키패드)의 자식까지 전부 보인다.
        GameObject root = isPrefabAsset ? PrefabUtility.LoadPrefabContents(assetPath) : selected;

        int blockers = 0, interactables = 0;
        foreach (string groupName in StructureGroups)
        {
            Transform group = root.transform.Find(groupName);
            if (group == null) continue;

            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
            {
                // PlayerInteractor가 쓰는 것과 똑같은 판정 — 그래야 "집을 수 있는 것은
                // 반드시 Interactable"이 어긋날 수가 없다.
                bool canInteract = t.GetComponentInParent<IInteractable>() != null;
                int want = canInteract ? interactable : blocker;

                if (canInteract) interactables++;
                else blockers++;

                if (t.gameObject.layer != want) t.gameObject.layer = want;
            }
        }

        if (isPrefabAsset)
        {
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            PrefabUtility.UnloadPrefabContents(root);
        }
        else
        {
            EditorUtility.SetDirty(selected);
        }

        Debug.Log($"[칸 레이어] {selected.name} — SightBlocker {blockers}개 · Interactable {interactables}개. " +
                  $"(대상 그룹: {string.Join(", ", StructureGroups)})\n" +
                  "문은 Interactable이므로, CoverageAudit·Stalker의 Occluders에 SightBlocker와 Interactable을 둘 다 넣을 것.");
    }
}
