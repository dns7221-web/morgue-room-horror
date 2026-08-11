using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 모듈러 조각(벽·바닥·천장·소품)의 <b>실제 크기</b>를 재는 에디터 전용 자.
/// 메뉴: <c>Tools ▸ 조각 자 (Module Ruler)</c>
///
/// ── 왜 필요한가 ────────────────────────────────────────
/// 3x3 격자의 칸 크기는 결국 <b>조각 하나의 폭 × 조각 수</b>로 정해진다.
/// 그런데 인스펙터는 Scale(0.01 같은 값)만 보여줄 뿐 "이게 몇 미터짜리인가"는
/// 알려주지 않는다. 그 숫자를 안 재고 눈대중으로 지나간 결과가 격자 ②번 버그
/// (칸이 자기 몫보다 커서 이웃 칸을 관통)였다.
///
/// 이 자는 <see cref="GridDebugPanel"/>과 역할이 짝을 이룬다 —
/// 여기서 <b>만들기 전에</b> 재고, 표시판이 <b>만든 뒤에</b> 검사한다.
///
/// ── 어떻게 재는가 ──────────────────────────────────────
/// 선택한 것의 <b>사본을 원점·무회전으로 잠깐 세워</b> 두고 잰 뒤 지운다.
/// 프리팹 에셋은 인스턴스화하지 않으면 렌더러 경계를 알 수 없고, 씬 오브젝트를
/// 그 자리에서 재면 <b>회전한 만큼 AABB가 부풀어</b> 실제보다 크게 나오기
/// 때문이다. 사본은 <see cref="HideFlags.HideAndDontSave"/>라 씬에 저장되지 않는다.
/// </summary>
public class ModuleRuler : EditorWindow
{
    /// <summary>이 에셋 킷의 기본 단위(m). Morgue Room PBR은 바닥 조각이 4x4·4x16·8x8·8x16이라 4m로 본다.</summary>
    private const float DefaultUnit = 4f;

    [SerializeField] private float unitSize = DefaultUnit;
    [SerializeField] private float tolerance = 0.05f;
    [SerializeField] private int piecesPerSide = 5;
    [SerializeField] private DefaultAsset batchFolder;

    private readonly List<Measurement> results = new();
    private Vector2 scroll;
    private GUIStyle wrapped;

    /// <summary>줄바꿈되는 라벨 스타일. 창을 좁혀도 문장이 잘려나가지 않는다.</summary>
    private GUIStyle Wrapped => wrapped ??= new GUIStyle(EditorStyles.label) { wordWrap = true };

    private struct Measurement
    {
        public string name;
        public bool hasRenderer;
        public Bounds render;      // 겉모양(보이는 크기)
        public bool hasCollider;
        public Bounds collide;     // 부딪히는 크기
        public int rendererCount;
    }

    [MenuItem("Tools/조각 자 (Module Ruler)")]
    private static void Open()
    {
        var window = GetWindow<ModuleRuler>("조각 자");
        window.minSize = new Vector2(440f, 320f);
        window.MeasureSelection();
    }

    private void OnSelectionChange()
    {
        MeasureSelection();
        Repaint();
    }

    // ─────────────────────────────────────────────────────────────
    // 재기
    // ─────────────────────────────────────────────────────────────

    private void MeasureSelection()
    {
        results.Clear();

        foreach (var obj in Selection.objects)
        {
            if (obj is not GameObject source) continue;   // 머티리얼·텍스처 등은 잴 것이 없다
            results.Add(Measure(source));
        }
    }

    /// <summary>사본을 원점·무회전으로 세워 재고 지운다.</summary>
    private static Measurement Measure(GameObject source)
    {
        var m = new Measurement { name = source.name };

        // Instantiate는 localScale을 그대로 복사한다 — 0.01 같은 임포트 스케일이
        // 살아 있어야 "씬에 끌어다 놓으면 몇 미터인가"가 나온다. 위치·회전만 초기화한다.
        GameObject probe = Instantiate(source);
        probe.hideFlags = HideFlags.HideAndDontSave;
        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        probe.SetActive(true);   // 꺼진 채로 고른 오브젝트도 경계가 잡히도록

        try
        {
            // 꺼져 있는 렌더러까지 포함(true) — GridDebugPanel의 크기 검사와 같은 기준으로
            // 재야 두 값이 어긋나지 않는다.
            var renderers = probe.GetComponentsInChildren<Renderer>(true);
            m.rendererCount = renderers.Length;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!m.hasRenderer) { m.render = renderers[i].bounds; m.hasRenderer = true; }
                else m.render.Encapsulate(renderers[i].bounds);
            }

            // 콜라이더를 따로 재는 이유: 벽을 세울 때 필요한 것은 보이는 두께가 아니라
            // 부딪히는 두께다. 소켓 면을 넘지 않게(격자 규칙 5) 잡으려면 이쪽이 기준이다.
            var colliders = probe.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger) continue;     // 트리거는 부피가 아니다
                if (!m.hasCollider) { m.collide = colliders[i].bounds; m.hasCollider = true; }
                else m.collide.Encapsulate(colliders[i].bounds);
            }
        }
        finally
        {
            DestroyImmediate(probe);
        }

        return m;
    }

    // ─────────────────────────────────────────────────────────────
    // 표시
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        DrawSettings();
        DrawBatch();
        EditorGUILayout.Space(4f);

        if (results.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "프로젝트 창이나 하이어라키에서 조각을 고르면 여기에 크기가 뜹니다.\n" +
                "여러 개를 한꺼번에 골라도 됩니다.",
                MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var m in results) DrawMeasurement(m);
        EditorGUILayout.EndScrollView();
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("기준", EditorStyles.boldLabel);

        unitSize = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("기본 단위 (m)", "이 킷의 모듈 단위. 각 축이 이 값의 배수인지 검사한다."),
            unitSize));

        tolerance = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("허용 오차 (m)", "배수 판정에서 이만큼까지는 딱 떨어진 것으로 본다."),
            tolerance));

        piecesPerSide = Mathf.Max(1, EditorGUILayout.IntField(
            new GUIContent("한 변 조각 수", "칸 한 변을 몇 조각으로 채울지. 홀수여야 가운데 조각이 정중앙에 온다(격자 규칙 2)."),
            piecesPerSide));

        float side = unitSize * piecesPerSide;
        string parity = piecesPerSide % 2 == 1
            ? "홀수 — 가운데 조각이 정중앙, 마주 보는 문이 저절로 같은 축에 선다"
            : "짝수 — 중앙이 조각과 조각 사이라 문을 정중앙에 못 놓는다";

        EditorGUILayout.HelpBox(
            $"한 변 {piecesPerSide}조각 × {unitSize:0.##}m = {side:0.##}m\n" +
            $"→ 소켓 간격도 {side:0.##}m가 되어야 하고, GridDebugPanel의 '간격'이 이 값으로 떠야 한다.\n" +
            $"{parity}",
            piecesPerSide % 2 == 1 ? MessageType.None : MessageType.Warning);
    }

    /// <summary>
    /// 폴더 하나를 통째로 재서 <b>파일로 뱉는다.</b>
    ///
    /// 소품처럼 개수가 수십 개인 것을 창에서 하나씩 눌러 확인하는 것은 사람이 할 일이
    /// 아니다. 한 번에 재서 표로 떨궈 두면, 배치 계획을 세울 때 그 파일 하나만 보면 된다.
    /// </summary>
    private void DrawBatch()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("폴더째 재기", EditorStyles.boldLabel);

        batchFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            new GUIContent("폴더", "이 폴더 아래 프리팹을 전부 재서 프로젝트 폴더에 표로 저장한다."),
            batchFolder, typeof(DefaultAsset), false);

        using (new EditorGUI.DisabledScope(batchFolder == null))
        {
            if (GUILayout.Button("전부 재서 파일로 저장")) MeasureFolder();
        }
    }

    private void MeasureFolder()
    {
        string folderPath = AssetDatabase.GetAssetPath(batchFolder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError("[조각 자] 폴더를 골라야 합니다.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("이름\t폭X\t높이Y\t깊이Z\t중심X\t중심Y\t중심Z\t콜라이더\t경로");

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                if (EditorUtility.DisplayCancelableProgressBar(
                        "조각 자", $"{prefab.name} ({i + 1}/{guids.Length})", (i + 1f) / guids.Length))
                    break;

                var m = Measure(prefab);
                if (!m.hasRenderer)
                {
                    sb.AppendLine($"{prefab.name}\t-\t-\t-\t-\t-\t-\t-\t{path}");
                    continue;
                }

                Vector3 s = m.render.size, c = m.render.center;
                sb.AppendLine(
                    $"{prefab.name}\t{s.x:0.000}\t{s.y:0.000}\t{s.z:0.000}\t" +
                    $"{c.x:0.000}\t{c.y:0.000}\t{c.z:0.000}\t" +
                    $"{(m.hasCollider ? "O" : "X")}\t{path}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // Assets 밖(프로젝트 루트)에 쓴다 — 안에 쓰면 유니티가 임포트하려 들고,
        // 이 파일은 게임 에셋이 아니라 사람이 읽을 표다.
        string outPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "..", "ModuleRuler_Report.tsv"));
        System.IO.File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);

        Debug.Log($"[조각 자] 프리팹 {guids.Length}개를 재서 저장했습니다:\n{outPath}");
        EditorUtility.RevealInFinder(outPath);
    }

    private void DrawMeasurement(Measurement m)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(m.name, EditorStyles.boldLabel);

        if (!m.hasRenderer)
        {
            EditorGUILayout.LabelField("렌더러가 없어 잴 것이 없습니다.");
            EditorGUILayout.EndVertical();
            return;
        }

        Vector3 size = m.render.size;

        EditorGUILayout.LabelField("겉모양", $"X {size.x:0.000}   Y {size.y:0.000}   Z {size.z:0.000}   (렌더러 {m.rendererCount}개)");
        EditorGUILayout.LabelField("배수", MultipleLine(size));

        // 피벗은 축마다 한 줄씩 쓴다 — 한 줄에 몰아넣으면 창이 좁을 때 Z가 잘려
        // 나가는데, 하필 <b>Z 중심이 벽면을 맞추는 데 필요한 그 값</b>이다.
        EditorGUILayout.LabelField("피벗 X", PivotOf(m.render.center.x, m.render.size.x));
        EditorGUILayout.LabelField("피벗 Y", PivotOf(m.render.center.y, m.render.size.y));
        EditorGUILayout.LabelField("피벗 Z", PivotOf(m.render.center.z, m.render.size.z));

        if (m.hasCollider)
        {
            Vector3 c = m.collide.size;
            EditorGUILayout.LabelField("콜라이더", $"X {c.x:0.000}   Y {c.y:0.000}   Z {c.z:0.000}");
        }
        else
        {
            EditorGUILayout.LabelField("콜라이더", "없음 — 벽으로 쓰면 통과한다");
        }

        // GUILayout.Label을 쓴다 — EditorGUILayout.LabelField는 높이를 한 줄로 고정해서
        // wordWrap을 켜도 두 번째 줄이 잘려나간다.
        GUILayout.Label(ThicknessLine(m), Wrapped);

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2f);
    }

    /// <summary>각 축이 기본 단위의 몇 배인지. 딱 떨어져야 이웃 조각과 이가 맞는다.</summary>
    private string MultipleLine(Vector3 size)
    {
        return $"X {Multiple(size.x)}   Y {Multiple(size.y)}   Z {Multiple(size.z)}";
    }

    private string Multiple(float value)
    {
        float ratio = value / unitSize;
        float nearest = Mathf.Round(ratio);
        bool clean = Mathf.Abs(nearest * unitSize - value) <= tolerance;

        return clean ? $"{nearest:0.##}칸 ✓" : $"{ratio:0.00}칸 ✗";
    }

    /// <summary>
    /// 원점이 조각의 어디에 있는가. <b>배치 좌표가 여기서 갈린다</b> —
    /// 중앙 피벗이면 4m 조각을 4m 간격으로 놓으면 되지만, 끝 피벗이면
    /// 첫 조각부터 반 칸(2m) 밀어야 한다.
    ///
    /// 중심 오프셋은 <b>분류와 상관없이 언제나 숫자로도</b> 보여준다.
    /// 얇은 조각(벽 0.018m)은 "중앙이냐 끝이냐"가 무의미하지만, 그 조각의 중심이
    /// 원점에서 얼마나 떨어졌는지는 <b>두꺼운 문 조각과 벽면을 맞출 때 반드시 필요한 값</b>이다.
    /// 분류만 내놓고 숫자를 숨기면 정작 필요한 순간에 못 쓴다.
    /// </summary>
    private string PivotOf(float center, float size)
    {
        string offset = $"(중심 {center:+0.000;-0.000;0.000})";

        if (size <= tolerance) return $"납작함 {offset}";
        if (Mathf.Abs(center) <= tolerance) return $"중앙 {offset}";
        if (Mathf.Abs(center - size * 0.5f) <= tolerance) return $"원점이 낮은 쪽 끝 {offset}";
        if (Mathf.Abs(center + size * 0.5f) <= tolerance) return $"원점이 높은 쪽 끝 {offset}";

        return $"어중간 {offset}";
    }

    /// <summary>
    /// 가장 얇은 축이 곧 벽 두께다. 격자에서는 <b>벽 바깥면이 칸 경계와 일치</b>해야 하므로
    /// (안 그러면 소켓 간격이 두께의 두 배만큼 늘어난다) 이 값만큼 안쪽으로 먹여 세워야 한다.
    /// </summary>
    private string ThicknessLine(Measurement m)
    {
        Vector3 s = m.hasCollider ? m.collide.size : m.render.size;
        string source = m.hasCollider ? "콜라이더" : "겉모양";

        float min = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
        string axis = Mathf.Approximately(min, s.x) ? "X" : Mathf.Approximately(min, s.y) ? "Y" : "Z";

        return $"{axis}축 {min:0.000}m ({source} 기준) — 벽 바깥면을 칸 경계에 맞추고 이만큼 안쪽으로 먹인다";
    }
}
