using UnityEditor;
using UnityEngine;

/// <summary>
/// 칸의 <b>벽·문·섬</b>에 <c>SightBlocker</c> 레이어를 칠하는 도구 — <c>Tools ▸ 칸 레이어 칠하기</c>.
///
/// ── 왜 필요했나 (겪은 버그) ───────────────────────────
/// <c>SightBlocker</c> 레이어는 추격자 문제 ③("쳐다봐도 안 멈춘다")을 고칠 때 만든 것이고,
/// 그때 3안 건물(<c>HorrorZip</c>)의 벽·문에 <b>손으로</b> 넣었다. 그런데 <c>GridCell_v3</c>는
/// 그 뒤에 <c>GridCellBuilder</c>가 조립했고, <b>도구가 레이어를 안 넣었다.</b>
/// 사람이 손으로 하던 일을 도구로 옮기면서 딸려오지 않은 것이다.
///
/// 그래서 칸 안에 SightBlocker가 <b>하나도 없었다</b>(48개 전부 Default). 그 결과:
/// <list type="bullet">
/// <item><see cref="CoverageAudit"/>이 막을 것을 못 찾아 <b>전부 "보였다"</b>로 오보 — 40m 너머
///       다른 칸의 문간까지 "뻥 뚫림"으로 읽었다</item>
/// <item><c>Stalker.IsSeenByPlayer</c>도 같은 마스크를 쓴다. 아무것도 안 가리면 <b>항상 "보임"</b>이라
///       추격자가 벽 뒤에 있어도 영영 얼어붙는다</item>
/// </list>
/// 둘 다 "코드는 맞는데 재료가 비어 있던" 경우다.
///
/// ── 무엇을 칠하고 무엇을 안 칠하나 ───────────────────
/// README의 레이어 규칙 그대로다 — <b>벽·문만.</b> 소품(의자·세면대)·시체·조명을 넣으면
/// 그 뒤에서 추격자가 안 멈춘다. 섬은 칠한다: 그것이 이 격자의 <b>은폐층</b>이라
/// 시야를 막는 것이 본래 역할이다.
///
/// 되돌리려면 <see cref="ResetToDefault"/>(같은 메뉴)로 Default로 되돌린다.
/// </summary>
internal static class CellLayerPainter
{
    private const string LayerName = "SightBlocker";

    /// <summary>
    /// 칠할 그룹 — 칸 루트 바로 아래 이 이름을 가진 자식의 <b>자손 전부</b>.
    ///
    /// <c>Walls</c> 안에 N/E/S/W가 있고 그 안에 벽·문틀·문짝이 들어 있다.
    /// <c>Island</c> 안에는 섬의 네 면과 <c>DrawerBox</c>·<c>Drawers</c>가 있다 —
    /// 섬은 이 격자의 <b>은폐층</b>이라 시야를 막는 것이 본래 역할이므로 통째로 칠한다.
    ///
    /// <c>Floor</c>·<c>Ceiling</c>은 뺀다 — 가로로 보는 시선에 쓸모가 없는데 넣으면
    /// 칠하는 대상만 늘어난다. <c>Props</c>·<c>Corpses</c>·<c>Lights</c>는 <b>절대 넣지 않는다</b>
    /// (소품이 시야를 막으면 추격자가 의자 뒤에서 안 멈춘다 — 겪은 버그 ③).
    /// </summary>
    private static readonly string[] BlockerGroups = { "Walls", "Island" };

    [MenuItem("Tools/칸 레이어 칠하기/벽·문·섬을 SightBlocker로")]
    private static void Paint() => Apply(LayerMask.NameToLayer(LayerName), "SightBlocker로 칠함");

    [MenuItem("Tools/칸 레이어 칠하기/되돌리기 (Default로)")]
    private static void ResetToDefault() => Apply(0, "Default로 되돌림");

    /// <summary>선택한 칸(프리팹 에셋 또는 씬 오브젝트)의 벽·문·섬 레이어를 바꾼다.</summary>
    private static void Apply(int layer, string what)
    {
        if (layer < 0)
        {
            Debug.LogError($"[칸 레이어] '{LayerName}' 레이어가 없습니다 — Project Settings ▸ Tags and Layers에서 먼저 만들 것.");
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
        GameObject root = isPrefabAsset ? PrefabUtility.LoadPrefabContents(assetPath) : selected;

        int painted = PaintGroups(root, layer);

        if (isPrefabAsset)
        {
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            PrefabUtility.UnloadPrefabContents(root);
        }
        else
        {
            EditorUtility.SetDirty(selected);
        }

        Debug.Log($"[칸 레이어] {selected.name} — 오브젝트 {painted}개를 {what}. " +
                  $"(대상 그룹: {string.Join(", ", BlockerGroups)})");
    }

    /// <summary>
    /// 지정 그룹들의 자손 전부에 레이어를 칠하고 칠한 개수를 돌려준다.
    ///
    /// 그룹 <b>이름</b>으로 고르는 이유: 벽이든 문틀이든 문짝이든 전부 그 그룹 안에 들어 있고,
    /// 소품·시체·조명은 다른 그룹(<c>Props</c>·<c>Corpses</c>·<c>Lights</c>)에 있어
    /// 이름만으로 정확히 갈린다. 콜라이더 유무로 고르면 소품까지 딸려와 추격자가
    /// 의자 뒤에서 안 멈추는 그 사고가 재발한다.
    /// </summary>
    private static int PaintGroups(GameObject root, int layer)
    {
        int count = 0;
        foreach (string groupName in BlockerGroups)
        {
            Transform group = root.transform.Find(groupName);
            if (group == null) continue;

            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer == layer) continue;
                t.gameObject.layer = layer;
                count++;
            }
        }
        return count;
    }
}
