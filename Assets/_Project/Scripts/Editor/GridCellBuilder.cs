using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 3x3 격자의 칸(<see cref="GridCell"/>) 하나를 좌표 계산까지 해서 통째로 조립하는 도구.
/// 메뉴: <c>Tools ▸ 칸 조립기 (Grid Cell Builder)</c>
///
/// ── 왜 툴로 만드나 ────────────────────────────────────
/// 칸 하나에 들어가는 오브젝트가 50개가 넘는데(바닥5·천장5·벽20·섬16·소켓4·트리거4),
/// 좌표가 조금만 어긋나도 증상이 원인과 전혀 닮지 않게 나타난다 — 허공에 뜬 문짝,
/// 천장을 뚫은 조명, 서로를 관통하는 벽. 격자 ②번 버그가 그것이었다.
/// <b>손으로 찍을 수 있는 좌표를 손으로 찍지 않는 것</b>이 그 사고를 없애는 방법이다.
///
/// ── 어디까지 하고 어디서 멈추나 ────────────────────────
/// 문짝은 <b>이 툴이 만들지 않는다.</b> <c>wall_doubledoor_piece01</c>은 벽+문틀
/// 단일 메시라 문짝이 없고, 문짝(<c>door_01</c>)의 경첩 자리는 계산으로 뽑을 수
/// 없다. 그래서 <b>문간 프리팹 하나만 손으로 만들고</b>(문틀 + 문짝 2 + Door 2),
/// 툴은 그것을 네 면에 배치하고 <see cref="GridCell"/>에 배선하는 일만 한다.
/// 반복은 툴이, 판단은 사람이 — 추격자를 씬 구조로 푼 것과 같은 선 긋기다.
///
/// 문간 프리팹이 비어 있으면 <b>맨 문틀</b>을 놓는다. 문 없이도 간격·크기 검증
/// (<see cref="GridDebugPanel"/>)은 돌아가므로, 배치부터 확인하고 문은 나중에 붙여도 된다.
/// </summary>
public class GridCellBuilder : EditorWindow
{
    private const string WallPath = "Assets/ThirdParty/Morgue Room PBR/Prefabs/walls/";
    private const string FloorPath = "Assets/ThirdParty/Morgue Room PBR/Prefabs/floor/";
    private const string CeilingPath = "Assets/ThirdParty/Morgue Room PBR/Prefabs/ceiling/";

    // ── 치수 (조각 자로 실측한 값이 기본값) ──────────────
    [SerializeField] private float unitSize = 4f;          // 벽 조각 폭 = 바닥 조각 기본 단위
    [SerializeField] private int piecesPerSide = 5;        // 홀수여야 가운데 조각이 정중앙
    [SerializeField] private float wallHeight = 3f;        // 벽 조각 높이 = 천장 y
    [SerializeField] private float wallThickness = 0.018f; // wall_piece01 두께
    // 벽을 칸 경계에서 이만큼 안쪽에 세운다. 기준은 벽 판(0.018)이 아니라 <b>문틀 장식</b>이다 —
    // doubledoor_frame01은 두께 0.358로 벽면 앞뒤로 0.179씩 튀어나오므로, 그보다 적게 들이면
    // 문틀이 칸 경계를 넘어 이웃 칸의 문틀과 같은 공간을 차지한다. 0.2면 경계 앞에서 멈추고
    // 이웃 칸 문틀과 등만 맞댄다.
    [SerializeField] private float doorHalfDepth = 0.2f;
    [SerializeField] private float islandPieceWidth = 1f;  // wall_piece_drawer_01 폭
    [SerializeField] private int islandPiecesPerSide = 4;  // 섬 한 면 조각 수 → 섬 한 변 4m
    // 앞면이 어느 쪽인지는 조각마다 다르게 만들어져 있다. 얇은 판은 뒤에서 보면
    // 투명하고, 두꺼운 문틀은 어느 쪽에서도 보이므로 <b>문틀만 남고 벽면이 사라진</b>
    // 것처럼 보인다. 그래서 일반 벽과 문 조각의 토글을 따로 둔다.
    [SerializeField] private bool flipWallFacing;          // 일반 벽 + 섬
    [SerializeField] private bool flipDoorwayFacing;       // 문간(문틀)

    // ── 조각 프리팹 ───────────────────────────────────
    [SerializeField] private GameObject floor16x8, floor16x4, floor4x4;
    [SerializeField] private GameObject ceiling16x8, ceiling16x4, ceiling4x4;
    [SerializeField] private GameObject wallPiece, doorFramePiece, islandPiece;
    [SerializeField] private GameObject doorwayPrefab;     // 손으로 확인한 문간 통짜. 있으면 이것만 놓는다
    [SerializeField] private GameObject doorTrimPiece;     // 문틀 장식(doubledoor_frame01) — 구멍에 끼운다
    [SerializeField] private GameObject doorLeafPrefab;    // 문짝(door_01) — 좌우 2짝으로 세운다
    [SerializeField] private float doorOpenAngle = 90f;    // 열림 각도. 좌우가 부호를 반대로 갖는다

    // HorrorZip(3안)의 작동하는 이중문에서 실측한 값. 렌더러 경계로 역산하면 에셋의
    // 피벗 규약에 따라 어긋나므로, 이미 맞는 것으로 확인된 숫자를 그대로 쓴다.
    [SerializeField] private float hingeSpacing = 1.882f;  // 두 문짝 경첩 사이 거리
    [SerializeField] private float leafHeight;             // 문짝 바닥 높이
    [SerializeField] private float leafDepthGap = 0.05f;   // 두 짝의 앞뒤 어긋남

    // 칸은 회전 없이 평행이동만 하므로, 문짝을 북·동에만 달면 <b>경계마다 문이 한 짝</b>이 된다.
    // 사방에 다 달면 이웃 칸도 자기 문을 같은 경계에 갖고 있어 문을 두 번 열어야 한다.
    [SerializeField] private bool leavesOnNorthEastOnly = true;

    // ── 천장 조명 ─────────────────────────────────────
    [SerializeField] private GameObject ceilingLightPrefab;
    [SerializeField] private int lightGrid = 3;            // 한 줄 개수. 3이면 9자리 중 섬 자리를 빼고 8개
    [SerializeField] private float lightDrop;              // 천장에서 아래로 더 내리는 양
    [SerializeField] private float lightRotationY;         // 기구가 Z로 1.5m 길어서 방향이 보인다
    [SerializeField] private bool disableLightShadows = true;
    [SerializeField] private float lightIntensityScale = 1f;

    // ── 소품 ─────────────────────────────────────────
    [SerializeField] private bool placeProps = true;
    [SerializeField] private string propFolder = "Assets/ThirdParty/Morgue Room PBR/Prefabs/props";

    // ── 이상현상 ──────────────────────────────────────
    [SerializeField] private GameObject corpsePrefab;
    [SerializeField] private bool buildAnomalies = true;

    private const float TableTop = 1.145f;   // autopsy_table 상판
    private const float DrawerTop = 0.833f;  // corpse_drawer 윗면

    // ── 키패드 (판정 지점) ────────────────────────────
    [SerializeField] private GameObject keypadPrefab;
    [SerializeField] private float keypadOffset = 2.5f;    // 문 중심에서 옆으로. 음수면 반대쪽
    [SerializeField] private float keypadHeight = 1.2f;
    [SerializeField] private float keypadDepth;            // 벽면에서 방 안쪽으로 띄우는 양
    [SerializeField] private bool flipKeypadFacing;
    [SerializeField] private bool addJudgmentPanel = true;

    [SerializeField] private bool addProgressDisplay = true;
    [SerializeField] private float progressDisplayHeight = 0.5f;   // 키패드 위로 띄우는 높이
    [SerializeField] private TMP_FontAsset progressFont;

    private Vector2 scroll;
    private GUIStyle wrapped;
    private GUIStyle Wrapped => wrapped ??= new GUIStyle(EditorStyles.label) { wordWrap = true };

    private float Side => unitSize * piecesPerSide;
    private float Half => Side * 0.5f;
    private float IslandHalf => islandPieceWidth * islandPiecesPerSide * 0.5f;

    [MenuItem("Tools/칸 조립기 (Grid Cell Builder)")]
    private static void Open()
    {
        var window = GetWindow<GridCellBuilder>("칸 조립기");
        window.minSize = new Vector2(460f, 520f);
        window.LoadDefaults();
    }

    /// <summary>에셋 경로가 정해져 있으므로 프리팹을 미리 물려둔다 — 매번 12개를 끌어다 놓지 않도록.</summary>
    private void LoadDefaults()
    {
        floor16x8 ??= Load(FloorPath + "floor_ceiling_8x16.prefab");
        floor16x4 ??= Load(FloorPath + "floor_ceiling_4x16.prefab");
        floor4x4 ??= Load(FloorPath + "floor_ceiling_4x4.prefab");
        ceiling16x8 ??= Load(CeilingPath + "floor_ceiling_8x16.prefab");
        ceiling16x4 ??= Load(CeilingPath + "floor_ceiling_4x16.prefab");
        ceiling4x4 ??= Load(CeilingPath + "floor_ceiling_4x4.prefab");
        wallPiece ??= Load(WallPath + "wall_piece01.prefab");
        // wall_doubledoor_piece01은 문간이 <b>두 개</b> 뚫린 조각이라 한 칸의 한 면에는 맞지 않는다.
        // 문간 하나짜리인 single을 기본으로 둔다.
        doorFramePiece ??= Load(WallPath + "wall_doubledoor_single.prefab");
        islandPiece ??= Load(WallPath + "wall_piece_drawer_01.prefab");
        doorTrimPiece ??= Load(WallPath + "doubledoor_frame01.prefab");
        doorLeafPrefab ??= Load(WallPath + "door_01.prefab");
        keypadPrefab ??= Load("Assets/ThirdParty/Keypad/Prefabs/Keypad.prefab");
        ceilingLightPrefab ??= Load("Assets/ThirdParty/Morgue Room PBR/Prefabs/props/ceiling_light.prefab");
        corpsePrefab ??= Load("Assets/_Project/Prefabs/corpse_in_a_bag.prefab");
        progressFont ??= AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
    }

    private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    // ─────────────────────────────────────────────────────────────
    // 창
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("치수", EditorStyles.boldLabel);
        unitSize = EditorGUILayout.FloatField(new GUIContent("조각 폭 (m)", "벽 조각 하나의 폭. 바닥 격자의 기본 단위이기도 하다."), unitSize);
        piecesPerSide = EditorGUILayout.IntField(new GUIContent("한 변 조각 수", "홀수여야 가운데 조각이 정중앙에 와서 마주 보는 문이 같은 축에 선다."), piecesPerSide);
        wallHeight = EditorGUILayout.FloatField(new GUIContent("벽 높이 (m)", "천장을 올릴 높이."), wallHeight);
        wallThickness = EditorGUILayout.FloatField(new GUIContent("일반 벽 두께 (m)", "조각 자의 '두께' 값."), wallThickness);
        doorHalfDepth = EditorGUILayout.FloatField(
            new GUIContent("벽 안쪽 들이기 (m)",
                "칸 경계에서 벽을 이만큼 안쪽에 세운다. 0에 가까우면 이웃 칸 벽과 같은 평면에 서서 면이 깜빡인다. " +
                "얇은 벽(0.018)에는 0.05 권장, 두꺼운 문틀 조각을 쓰면 그 두께의 절반 이상."),
            doorHalfDepth);
        flipWallFacing = EditorGUILayout.Toggle(new GUIContent("일반 벽·섬 뒤집기", "벽면이 투명하게 보이면 켜고 다시 만든다."), flipWallFacing);
        flipDoorwayFacing = EditorGUILayout.Toggle(new GUIContent("문간 뒤집기", "문틀만 남고 그 주변 벽면이 뚫려 보이면 켜고 다시 만든다. 일반 벽과 앞면이 반대로 만들어져 있다."), flipDoorwayFacing);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("섬 (은폐층 + 이상현상 무대)", EditorStyles.boldLabel);
        islandPieceWidth = EditorGUILayout.FloatField(new GUIContent("섬 조각 폭 (m)", "wall_piece_drawer_01 실측 폭."), islandPieceWidth);
        islandPiecesPerSide = EditorGUILayout.IntField(new GUIContent("섬 한 면 조각 수", "한 면 길이 = 조각 폭 × 이 값. 마주 보는 문을 가릴 만큼 넓어야 한다."), islandPiecesPerSide);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("조각 프리팹", EditorStyles.boldLabel);
        floor16x8 = Field("바닥 16x8", floor16x8);
        floor16x4 = Field("바닥 16x4", floor16x4);
        floor4x4 = Field("바닥 4x4", floor4x4);
        ceiling16x8 = Field("천장 16x8", ceiling16x8);
        ceiling16x4 = Field("천장 16x4", ceiling16x4);
        ceiling4x4 = Field("천장 4x4", ceiling4x4);
        wallPiece = Field("일반 벽", wallPiece);
        islandPiece = Field("섬 조각", islandPiece);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("문간", EditorStyles.boldLabel);

        doorwayPrefab = Field("문간 통짜 (있으면 이것만)", doorwayPrefab);
        EditorGUILayout.HelpBox(
            doorwayPrefab != null
                ? "문간 통짜를 네 면에 그대로 놓습니다. 아래 문틀·문짝 설정은 무시됩니다."
                : "비어 있으면 아래 문틀 + 문짝으로 조립합니다. 결과가 이상하면 '문간 하나만 만들기'로 눈으로 고친 뒤 프리팹으로 저장해 여기 꽂으세요.",
            MessageType.None);

        using (new EditorGUI.DisabledScope(doorwayPrefab != null))
        {
            doorFramePiece = Field("벽 조각 (문 구멍)", doorFramePiece);
            doorTrimPiece = Field("└ 문틀 장식", doorTrimPiece);
            doorLeafPrefab = Field("└ 문짝 (좌우 2짝)", doorLeafPrefab);
            hingeSpacing = EditorGUILayout.FloatField(
                new GUIContent("경첩 간격 (m)", "두 문짝 경첩 사이 거리. 기본값 1.882는 3안 이중문에서 실측한 값."), hingeSpacing);
            leafHeight = EditorGUILayout.FloatField(new GUIContent("문짝 높이 (m)", "문짝이 바닥에 안 닿거나 파묻히면 조절한다."), leafHeight);
            leafDepthGap = EditorGUILayout.FloatField(new GUIContent("두 짝 앞뒤 간격 (m)", "두 짝이 서로 파고들면 늘린다."), leafDepthGap);
            doorOpenAngle = EditorGUILayout.FloatField(
                new GUIContent("문 열림 각도", "좌우 문짝이 이 각도를 부호만 반대로 갖는다. 반대로 열리면 음수."), doorOpenAngle);
            leavesOnNorthEastOnly = EditorGUILayout.Toggle(
                new GUIContent("문짝은 북·동에만",
                    "켜면 남·서쪽은 문틀만 세운다. 그 경계의 문은 이웃 칸이 들고 있으므로 경계마다 문이 한 짝이 되고, " +
                    "문을 두 번 여는 일이 없어진다. 끄면 사방에 다 단다(경계마다 두 짝)."),
                leavesOnNorthEastOnly);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("문간 하나만 만들기 (원점에)")) BuildDoorwayTemplate();
            if (GUILayout.Button("선택한 것의 문짝 다시 배치")) RelayoutDoorLeaves();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "경첩 간격을 고친 뒤 '다시 배치'를 누르면 칸을 다시 만들지 않고 문짝만 옮깁니다. " +
                "칸을 통째로 골라도 되고(네 면 한꺼번에), 문간 하나만 골라도 됩니다.",
                MessageType.None);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("소품", EditorStyles.boldLabel);
        placeProps = EditorGUILayout.Toggle(new GUIContent("소품 놓기", $"손으로 정한 배치도({Props.Length}자리)대로 놓는다. 자리는 스크립트의 Props 표에서 고친다."), placeProps);
        propFolder = EditorGUILayout.TextField(new GUIContent("소품 폴더", "이 폴더에서 이름으로 프리팹을 찾는다."), propFolder);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("이상현상", EditorStyles.boldLabel);
        buildAnomalies = EditorGUILayout.Toggle(new GUIContent("이상현상 만들기", "시체 3구 + AnomalyManager + 소실·증가·이동 3종을 배선한다."), buildAnomalies);
        corpsePrefab = Field("시체 프리팹", corpsePrefab);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("천장 조명", EditorStyles.boldLabel);
        ceilingLightPrefab = Field("조명 프리팹", ceilingLightPrefab);
        lightGrid = EditorGUILayout.IntField(new GUIContent("한 줄 개수", "N을 넣으면 N×N으로 깔고, 섬에 박히는 자리는 건너뛴다."), lightGrid);
        lightDrop = EditorGUILayout.FloatField(new GUIContent("천장에서 내리기 (m)", "기구 원점이 윗면이라 0이면 천장에 딱 붙는다."), lightDrop);
        lightRotationY = EditorGUILayout.FloatField(new GUIContent("기구 방향 (Y°)", "기구가 Z축으로 1.5m 길다. 90을 넣으면 가로로 눕는다."), lightRotationY);
        lightIntensityScale = EditorGUILayout.FloatField(new GUIContent("밝기 배수", "프리팹 기본 밝기 0.82에 곱한다. 어두우면 올린다."), lightIntensityScale);
        disableLightShadows = EditorGUILayout.Toggle(new GUIContent("그림자 끄기 (성능)", "9칸이 동시에 켜져 있어 조명이 9배로 늘어난다. 포인트 라이트 그림자는 가장 비싸다."), disableLightShadows);

        if (ceilingLightPrefab != null && lightGrid > 0)
        {
            int total = CountLightSlots();
            EditorGUILayout.HelpBox(
                $"칸당 {total}개 → 9칸이 전부 켜지면 씬에 <b>{total * 9}개</b>.\n" +
                (disableLightShadows ? "그림자를 꺼서 놓습니다." : "⚠ 그림자를 켠 채로 놓습니다 — 포인트 라이트 그림자는 큐브맵 6면이라 개수만큼 그대로 비용입니다."),
                disableLightShadows ? MessageType.None : MessageType.Warning);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("키패드 (판정 지점)", EditorStyles.boldLabel);
        keypadPrefab = Field("키패드 프리팹", keypadPrefab);
        keypadOffset = EditorGUILayout.FloatField(new GUIContent("문 옆 거리 (m)", "문 중심에서 옆으로 얼마나. 음수면 반대쪽 벽에 붙는다."), keypadOffset);
        keypadHeight = EditorGUILayout.FloatField(new GUIContent("높이 (m)", "바닥에서 키패드 원점까지."), keypadHeight);
        keypadDepth = EditorGUILayout.FloatField(new GUIContent("벽에서 띄우기 (m)", "양수면 방 안쪽으로 나온다. 벽에 파묻히면 늘린다."), keypadDepth);
        flipKeypadFacing = EditorGUILayout.Toggle(new GUIContent("키패드 뒤집기", "키패드가 벽을 보고 있으면 켠다."), flipKeypadFacing);
        addJudgmentPanel = EditorGUILayout.Toggle(new GUIContent("JudgmentPanel 붙이기", "E로 판정 UI를 여는 컴포넌트. GameManager가 없어도 안전하게 가만있는다."), addJudgmentPanel);

        addProgressDisplay = EditorGUILayout.Toggle(
            new GUIContent("진행도 표시 붙이기", "키패드 위에 '1 / 8' 월드 스페이스 텍스트를 심는다. 4면 전부."),
            addProgressDisplay);
        using (new EditorGUI.DisabledScope(!addProgressDisplay))
            progressDisplayHeight = EditorGUILayout.FloatField(new GUIContent("표시 높이 (m)", "키패드 위로 띄우는 양."), progressDisplayHeight);

        if (GUILayout.Button("선택한 칸에 진행도 표시만 추가 (다시 안 지음)"))
            AddProgressDisplaysToSelection();
        EditorGUILayout.HelpBox(
            "칸 만들기는 매번 새 오브젝트를 처음부터 짓는다 — 이미 손으로 다듬은 칸(GridCell_v3 등)엔 그걸 " +
            "다시 누르지 말고, 그 칸을 선택한 뒤 이 버튼만 누르세요. 이미 있는 JudgmentPanel(키패드) 4곳을 찾아 " +
            "진행도 표시만 얹고, 나머지는 하나도 안 건드립니다.",
            MessageType.Info);

        DrawSummary();

        EditorGUILayout.Space(6f);
        using (new EditorGUI.DisabledScope(!CanBuild()))
        {
            if (GUILayout.Button("칸 만들기", GUILayout.Height(30f))) Build();
        }

        EditorGUILayout.EndScrollView();
    }

    private static GameObject Field(string label, GameObject value) =>
        (GameObject)EditorGUILayout.ObjectField(label, value, typeof(GameObject), false);

    private void DrawSummary()
    {
        EditorGUILayout.Space(4f);

        if (piecesPerSide % 2 == 0)
            EditorGUILayout.HelpBox("한 변 조각 수가 짝수입니다 — 중앙이 조각과 조각 사이라 문을 정중앙에 못 놓습니다.", MessageType.Error);

        if (Side % unitSize > 0.001f)
            EditorGUILayout.HelpBox("한 변이 조각 폭의 배수가 아닙니다.", MessageType.Error);

        string doorNote = doorLeafPrefab != null
            ? "네 면에 문틀을 놓고, 그 안에 문짝 2짝을 세워 Door까지 붙입니다 (문 8짝)."
            : "문짝이 비어 있어 문틀만 놓습니다 — 문 0짝이라 옆 칸으로 못 넘어갑니다(간격·크기 검증은 됩니다).";

        EditorGUILayout.HelpBox(
            $"한 변 {Side:0.##}m ({piecesPerSide}조각 × {unitSize:0.##}m)\n" +
            $"소켓 간격도 {Side:0.##}m가 되고, GridDebugPanel의 '간격'이 이 값으로 떠야 합니다.\n" +
            $"섬 한 변 {IslandHalf * 2f:0.##}m — 마주 보는 두 문 사이 시선을 막습니다.\n\n" +
            doorNote,
            doorLeafPrefab != null ? MessageType.Info : MessageType.Warning);
    }

    private bool CanBuild() =>
        piecesPerSide % 2 == 1 && piecesPerSide > 0 && unitSize > 0f &&
        floor16x8 != null && floor16x4 != null && floor4x4 != null &&
        wallPiece != null && doorFramePiece != null;

    // ─────────────────────────────────────────────────────────────
    // 조립
    // ─────────────────────────────────────────────────────────────

    /// <summary>N(0) · E(1) · S(2) · W(3) — GridCell.CardinalDirection과 순서를 맞춘다.</summary>
    private static readonly Vector3[] Outward =
    {
        Vector3.forward, Vector3.right, Vector3.back, Vector3.left,
    };

    private static readonly string[] DirNames = { "N", "E", "S", "W" };

    private void Build()
    {
        // 50개 넘는 오브젝트가 한 번의 Ctrl+Z로 통째로 사라지게 묶는다 —
        // 되돌리기가 안 되면 값을 바꿔가며 다시 만들어 볼 수가 없다.
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("칸 만들기");

        var root = new GameObject("GridCell_v2");
        Undo.RegisterCreatedObjectUndo(root, "칸 만들기");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var cell = root.AddComponent<GridCell>();

        BuildFloor(root.transform);
        BuildCeiling(root.transform);

        var sockets = new RoomSocket[4];
        var doors = new List<Door>[4];
        BuildWalls(root.transform, sockets, doors);

        BuildIsland(root.transform);
        BuildLights(root.transform);
        BuildProps(root.transform);
        BuildAnomalies(root.transform);
        BuildStalkerPoint(root.transform);
        BuildTriggers(root.transform);
        Transform spawn = BuildSpawnPoint(root.transform);

        WireCell(cell, sockets, doors, spawn);

        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        int doorCount = 0;
        foreach (var list in doors) doorCount += list.Count;
        Debug.Log($"[칸 조립기] GridCell_v2 완성 — 한 변 {Side:0.##}m · 문 {doorCount}짝. " +
                  "프리팹으로 저장한 뒤 GridManager의 Cell Prefab에 꽂으세요.", root);
    }

    // ── 바닥·천장 ────────────────────────────────────────

    private void BuildFloor(Transform parent)
    {
        var group = Group(parent, "Floor");
        Tile(group, floor16x8, floor16x4, floor4x4, 0f);
    }

    private void BuildCeiling(Transform parent)
    {
        if (ceiling16x8 == null) return;   // 천장은 없어도 격자 검증에는 지장이 없다
        var group = Group(parent, "Ceiling");
        Tile(group, ceiling16x8, ceiling16x4, ceiling4x4, wallHeight);
    }

    /// <summary>
    /// 한 변을 <c>piecesPerSide</c>칸짜리 격자로 보고 <b>큰 조각부터 욕심껏</b> 채운다.
    ///
    /// 조각 목록을 손으로 배치표에 적어두지 않는 이유: 한 변 조각 수를 바꾸는 순간
    /// 표가 통째로 무효가 되기 때문이다. 채우는 규칙만 적어두면 크기가 바뀌어도 따라온다.
    /// (실측이 정확한 4x4·4x16·8x16만 쓴다 — 8x8은 8.023 x 8.073으로 어긋나 있다)
    /// </summary>
    private void Tile(Transform parent, GameObject p16x8, GameObject p16x4, GameObject p4x4, float y)
    {
        int n = piecesPerSide;
        var taken = new bool[n, n];

        // (가로칸, 세로칸, 프리팹, 회전) — 넓은 것부터.
        var shapes = new List<(int w, int h, GameObject prefab, float rotY)>
        {
            (4, 2, p16x8, 0f), (2, 4, p16x8, 90f),
            (4, 1, p16x4, 0f), (1, 4, p16x4, 90f),
            (1, 1, p4x4, 0f),
        };

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                if (taken[x, z]) continue;

                foreach (var s in shapes)
                {
                    if (s.prefab == null || !Fits(taken, x, z, s.w, s.h, n)) continue;

                    for (int dx = 0; dx < s.w; dx++)
                        for (int dz = 0; dz < s.h; dz++)
                            taken[x + dx, z + dz] = true;

                    var pos = new Vector3(
                        -Half + (x + s.w * 0.5f) * unitSize,
                        y,
                        -Half + (z + s.h * 0.5f) * unitSize);

                    Place(s.prefab, parent, pos, Quaternion.Euler(0f, s.rotY, 0f), $"{s.prefab.name}_{x}_{z}");
                    break;
                }
            }
        }
    }

    private static bool Fits(bool[,] taken, int x, int z, int w, int h, int n)
    {
        if (x + w > n || z + h > n) return false;

        for (int dx = 0; dx < w; dx++)
            for (int dz = 0; dz < h; dz++)
                if (taken[x + dx, z + dz]) return false;

        return true;
    }

    // ── 벽 + 문간 ────────────────────────────────────────

    /// <summary>
    /// 네 면에 <c>piecesPerSide</c>조각씩 세우고 <b>가운데 한 장만 문간</b>으로 바꾼다.
    ///
    /// 문 원점은 경계에서 <c>doorHalfDepth</c>만큼 안쪽이다 — 경계에 딱 걸치게 두면
    /// 문틀이 이웃 칸으로 반쯤 밀고 들어가고, <b>이웃 칸의 문틀과 같은 공간을 차지한다</b>
    /// (칸마다 사방에 자기 문을 갖기 때문). 일반 벽은 두께의 절반을 더 보정해
    /// 문 조각의 벽면과 같은 평면에 세운다.
    /// </summary>
    private void BuildWalls(Transform parent, RoomSocket[] sockets, List<Door>[] doors)
    {
        var group = Group(parent, "Walls");
        int mid = piecesPerSide / 2;

        for (int d = 0; d < 4; d++)
        {
            doors[d] = new List<Door>();

            Vector3 outward = Outward[d];
            Vector3 tangent = new(outward.z, 0f, -outward.x);   // 벽을 따라가는 축

            // 둘 다 '방 안쪽을 본다'가 목표지만, 앞면이 어느 쪽인지가 조각마다 달라
            // 뒤집기를 따로 받는다.
            Quaternion wallFacing = FacingRotation(-outward, flipWallFacing);
            Quaternion doorFacing = FacingRotation(-outward, flipDoorwayFacing);

            var side = Group(group, DirNames[d]);

            float doorDist = Half - doorHalfDepth;
            float wallDist = doorDist + wallThickness * 0.5f;   // 벽면을 문 조각 중심면에 맞춘다

            for (int i = 0; i < piecesPerSide; i++)
            {
                float along = (i - mid) * unitSize;             // 가운데 조각이 0
                bool isDoor = i == mid;

                Vector3 pos = tangent * along + outward * (isDoor ? doorDist : wallDist);

                if (!isDoor)
                {
                    Place(wallPiece, side, pos, wallFacing, $"Wall_{DirNames[d]}_{i}");
                    continue;
                }

                // 문간 통짜가 지정돼 있으면 그것만 놓는다 — 눈으로 확인한 것이 계산보다 낫다.
                GameObject doorway;
                if (doorwayPrefab != null)
                {
                    doorway = Place(doorwayPrefab, side, pos, doorFacing, $"Doorway_{DirNames[d]}");
                    if (d == 0) ValidateDoorway(doorway);   // 네 면에 같은 경고를 네 번 띄우지 않는다
                }
                else
                {
                    doorway = Place(doorFramePiece, side, pos, doorFacing, $"Doorway_{DirNames[d]}");

                    // 남·서쪽은 문틀만 세운다 — 그 경계의 문은 이웃 칸이 들고 있다.
                    bool ownsDoor = !leavesOnNorthEastOnly || d == 0 || d == 1;   // N · E
                    if (ownsDoor) BuildDoorLeaves(doorway.transform, DirNames[d]);
                    else if (doorTrimPiece != null)
                        Place(doorTrimPiece, doorway.transform, Vector3.zero, Quaternion.identity, doorTrimPiece.name);
                }

                // 문짝이 몇 개든(이중문이면 2개) 전부 이 방향 소속으로 등록한다.
                doors[d].AddRange(doorway.GetComponentsInChildren<Door>(true));
            }

            BuildKeypad(side, DirNames[d], outward, tangent, wallDist);
            sockets[d] = BuildSocket(parent, d, outward);
        }
    }

    /// <summary>앞면이 어느 쪽인지는 에셋 나름이라, 뒤집혀 보이면 토글로 돌린다.</summary>
    private static Quaternion FacingRotation(Vector3 forward, bool flip) =>
        Quaternion.LookRotation(flip ? -forward : forward, Vector3.up);

    /// <summary>
    /// 문틀 안에 문짝 2짝을 세우고 <see cref="Door"/>를 붙인다.
    ///
    /// 문짝 폭을 손으로 입력받지 않고 <b>프리팹을 재서 쓴다</b> — 에셋을 바꾸면
    /// 숫자도 같이 바뀌어야 하는데, 그 갱신을 사람이 기억하는 구조는 언젠가 어긋난다.
    ///
    /// 좌우는 <c>openAngle</c>의 <b>부호</b>로만 갈린다. 같은 부호를 주면 두 짝이
    /// 같은 방향으로 열려 서로를 관통하는데, 3안 복도 이중문에서 실제로 겪은 버그다.
    /// </summary>
    private void BuildDoorLeaves(Transform frame, string dirName)
    {
        // 문틀 장식은 벽 조각의 구멍에 그대로 끼운다 — 세 조각이 전부 X 중앙 / Y 밑면 /
        // Z 중앙 기준이라 같은 원점에 겹쳐 놓으면 맞물린다. 이 트림이 문짝 폭(0.941×2)과
        // 구멍 폭(2.000)의 차이 0.118을 메우는 부분이라, 빼면 양옆이 휑하게 뜬다.
        if (doorTrimPiece != null)
            Place(doorTrimPiece, frame, Vector3.zero, Quaternion.identity, doorTrimPiece.name);

        if (doorLeafPrefab == null) return;

        int interactable = LayerMask.NameToLayer("Interactable");

        for (int i = 0; i < 2; i++)
        {
            bool right = i == 1;
            float sign = right ? 1f : -1f;

            // 경첩(=문짝 원점)을 실측 간격만큼 좌우로 벌리고, 한 짝만 180° 돌려 세운다.
            // 폭을 재서 역산하지 않는다 — 피벗이 어느 가장자리에 있느냐에 따라 답이
            // 뒤집히는데, 그건 에셋마다 다르고 눈으로 보기 전에는 알 수 없다.
            var local = new Vector3(sign * hingeSpacing * 0.5f, leafHeight, sign * leafDepthGap * 0.5f);
            Quaternion rot = right ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

            var leaf = (GameObject)PrefabUtility.InstantiatePrefab(doorLeafPrefab, frame);
            Undo.RegisterCreatedObjectUndo(leaf, "칸 만들기");
            leaf.transform.SetLocalPositionAndRotation(local, rot);
            leaf.name = $"Door_{dirName}_{(right ? "R" : "L")}";

            // 조준 필터가 레이어로 걸러내므로(PlayerInteractor), 여기서 맞춰두지 않으면
            // 문이 서 있어도 E가 안 먹는다.
            if (interactable >= 0) SetLayerRecursively(leaf, interactable);

            var door = leaf.AddComponent<Door>();
            var so = new SerializedObject(door);
            so.FindProperty("openAngle").floatValue = right ? -doorOpenAngle : doorOpenAngle;
            so.ApplyModifiedProperties();
        }

        if (interactable < 0)
            Debug.LogWarning("[칸 조립기] 'Interactable' 레이어가 없어 문짝 레이어를 못 맞췄습니다 — E 상호작용이 안 걸립니다.");
    }

    /// <summary>
    /// 문 옆 벽면에 키패드를 붙인다 — 네 면 전부.
    ///
    /// 3안에서는 판정 지점이 한 곳뿐이었지만(키패드 방), 격자에서는 <b>어느 문으로
    /// 나가든 그 앞에서 판정할 수 있어야</b> 한다. 판정하러 특정 방향으로 되돌아가야
    /// 한다면 그건 격자가 아니라 다시 1D 동선이 된다.
    ///
    /// <see cref="JudgmentPanel"/>은 <c>GameManager</c>·<c>JudgmentUI</c>가 없으면
    /// 조용히 아무것도 하지 않으므로, 격자만 켜둔 지금 붙여도 안전하다.
    /// </summary>
    private void BuildKeypad(Transform side, string dirName, Vector3 outward, Vector3 tangent, float wallDist)
    {
        if (keypadPrefab == null) return;

        Vector3 pos = tangent * keypadOffset
                    + outward * (wallDist - keypadDepth)
                    + Vector3.up * keypadHeight;

        var keypad = Place(keypadPrefab, side, pos, FacingRotation(-outward, flipKeypadFacing), $"Keypad_{dirName}");

        // 조준은 레이어로 걸러진다(PlayerInteractor). 여기서 안 맞추면 E가 안 먹는다.
        int interactable = LayerMask.NameToLayer("Interactable");
        if (interactable >= 0) SetLayerRecursively(keypad, interactable);

        if (keypad.GetComponentInChildren<Collider>(true) == null)
        {
            Debug.LogWarning(
                $"[칸 조립기] {keypadPrefab.name}에 콜라이더가 없어 조준되지 않습니다 — 키패드에 Collider를 붙여야 합니다.",
                keypad);
        }

        if (addJudgmentPanel && keypad.GetComponentInChildren<JudgmentPanel>(true) == null)
            Undo.AddComponent<JudgmentPanel>(keypad);

        if (addProgressDisplay)
            BuildProgressDisplay(side, dirName, pos + Vector3.up * progressDisplayHeight, keypad.transform.rotation);
    }

    /// <summary>
    /// <b>이미 있는 칸</b>에서 키패드(JudgmentPanel)를 전부 찾아 진행도 표시만 얹는다 —
    /// 칸을 통째로 다시 짓지 않는다. 문짝을 손으로 눈으로 맞췄던 "문간 하나만 만들기"와
    /// 같은 이유다: 이미 다듬어놓은 칸(예: GridCell_v3)에 새 기능 하나만 추가하고 싶을 때,
    /// '칸 만들기'를 다시 누르면 그 프리팹은 그대로 두고 <b>엉뚱한 새 오브젝트</b>가
    /// 하나 더 생길 뿐이다 — 이 버튼은 선택한 대상 안쪽만 고친다.
    /// </summary>
    private void AddProgressDisplaysToSelection()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("[칸 조립기] 칸이나 키패드를 먼저 선택하세요.");
            return;
        }

        var panels = selected.GetComponentsInChildren<JudgmentPanel>(true);
        if (panels.Length == 0)
        {
            Debug.LogWarning($"[칸 조립기] {selected.name} 아래에 JudgmentPanel(키패드)이 없습니다.", selected);
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("진행도 표시 추가");

        int added = 0, skipped = 0;
        foreach (var panel in panels)
        {
            var keypad = panel.gameObject;

            // 이미 붙어 있으면 건너뛴다 — 버튼을 두 번 눌러도 중복으로 안 쌓인다.
            if (keypad.GetComponentInChildren<ProgressUI>(true) != null) { skipped++; continue; }

            var parent = keypad.transform.parent != null ? keypad.transform.parent : keypad.transform;
            Vector3 pos = keypad.transform.localPosition + Vector3.up * progressDisplayHeight;

            BuildProgressDisplay(parent, keypad.name, pos, keypad.transform.localRotation);
            added++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[칸 조립기] 진행도 표시 {added}개를 추가했습니다" +
                  (skipped > 0 ? $" (이미 있어서 건너뛴 것 {skipped}개)." : "."), selected);
    }

    /// <summary>
    /// 키패드 위에 "1 / 8" 월드 스페이스 텍스트를 심는다.
    ///
    /// 3안은 건물이 통째로 움직여서 <c>FollowActiveRoom</c>으로 UI가 매 프레임
    /// 따라다녀야 했지만, 격자는 <b>키패드가 각 칸에 고정으로 박혀 있어 그럴 필요가
    /// 없다.</b> 재활용으로 칸이 옮겨갈 때 이 텍스트도 부모(칸)를 따라 그냥 같이
    /// 움직인다 — 별도 컴포넌트 없이 Transform 부모-자식 관계만으로 해결된다.
    ///
    /// <see cref="ProgressUI"/> 자체는 매 프레임 GameManager.Progress만 읽는
    /// 위치 무관 컴포넌트라, 네 면에 몇 개를 심든 전부 같은 값을 보여준다.
    /// 사방 어디서 판정하든 자기 바로 위에서 진행도가 보이는 게 목표라 4개 다 심는다.
    ///
    /// 크기·글자 크기는 3안 씬의 기존 ProgressUI를 그대로 실측해 가져온 값이다
    /// (Canvas sizeDelta 1x1, 텍스트 sizeDelta 1.6x0.6, 폰트 크기 0.2).
    /// </summary>
    private void BuildProgressDisplay(Transform parent, string dirName, Vector3 position, Quaternion rotation)
    {
        var canvasGO = new GameObject($"ProgressUI_{dirName}", typeof(RectTransform), typeof(Canvas));
        Undo.RegisterCreatedObjectUndo(canvasGO, "칸 만들기");
        canvasGO.transform.SetParent(parent, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var canvasRect = canvasGO.GetComponent<RectTransform>();
        // 앵커·피벗을 명시하지 않으면 스크립트로 갓 만든 RectTransform은 기본값(0,0 anchor)을
        // 쓰는데, 부모-자식 간 기준점이 서로 어긋나 글자가 엉뚱한 자리로 밀리거나 찌그러진
        // 크기로 계산된다 — 3안 원본 Canvas를 실측한 값(가운데 정렬, 1x1)을 그대로 준다.
        //
        // ⚠ 위치를 세팅하기 <b>전에</b> 앵커부터 정해야 한다. SetPointAnchor는 anchoredPosition을
        // 0으로 초기화하는데, 이게 실제 위치 지정보다 나중에 실행되면 방금 준 위치를 도로 지운다.
        SetPointAnchor(canvasRect, new Vector2(1f, 1f));
        canvasGO.transform.SetLocalPositionAndRotation(position, rotation);

        var textGO = new GameObject("Text (TMP)", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(textGO, "칸 만들기");
        textGO.transform.SetParent(canvasGO.transform, false);
        var textRect = textGO.GetComponent<RectTransform>();
        SetPointAnchor(textRect, new Vector2(1.6f, 0.6f));

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        if (progressFont != null) tmp.font = progressFont;   // 지정 안 하면 TMP 기본 폰트 설정에 기대야 하는데, 그게 안 잡혀 있으면 글자가 통째로 안 보인다
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 0.2f;
        tmp.text = "1 / 8";
        tmp.raycastTarget = false;   // 순수 표시용 — 클릭/조준을 가로챌 이유가 없다

        textGO.AddComponent<ProgressUI>();   // 비워두면 같은 오브젝트의 TMP를 자동으로 찾는다
    }

    /// <summary>
    /// RectTransform을 "가운데 한 점" 앵커로 고정한다(anchorMin=anchorMax=pivot=0.5,0.5).
    /// 이러면 로컬 위치가 곧 화면상 중심 좌표가 되어, 부모-자식 간 기준점이 서로
    /// 어긋나 글자가 안 보이거나 잘리는 사고를 피할 수 있다.
    /// </summary>
    private static void SetPointAnchor(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    /// <summary>
    /// 문간 하나만 씬 원점에 만든다 — <b>칸 50개를 다시 만들지 않고</b> 문짝 자리만
    /// 눈으로 맞추기 위한 것이다.
    ///
    /// 문짝 배치는 에셋의 피벗 규약에 달려 있어 계산으로는 확신할 수 없다. 여기서
    /// 한 번 맞춰 프리팹으로 저장하고 <b>문간 통짜</b> 칸에 꽂으면, 그 다음부터는
    /// 조립기가 확인된 것을 복제하기만 한다. 판단은 사람이, 반복은 툴이.
    /// </summary>
    private void BuildDoorwayTemplate()
    {
        if (doorFramePiece == null) return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("문간 만들기");

        // 값을 고쳐가며 눌러보는 버튼이라, 누를 때마다 원점에 쌓이면 곧 뭐가 뭔지 모르게 된다.
        var previous = GameObject.Find("Doorway");
        if (previous != null) Undo.DestroyObjectImmediate(previous);

        var root = new GameObject("Doorway");
        Undo.RegisterCreatedObjectUndo(root, "문간 만들기");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Place(doorFramePiece, root.transform, Vector3.zero, Quaternion.identity, doorFramePiece.name);
        BuildDoorLeaves(root.transform, DirNames[0]);

        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log("[칸 조립기] 문간 하나를 원점에 만들었습니다. 문짝 자리를 손으로 맞춘 뒤 " +
                  "프리팹으로 저장하고 '문간 통짜' 칸에 꽂으세요.", root);
    }

    /// <summary>
    /// 이미 놓인 문짝을 지금 설정대로 <b>다시 앉힌다</b> — 칸을 새로 만들지 않고.
    ///
    /// 경첩 간격 같은 값은 숫자를 보고 정할 수 없고 <b>눈으로 맞춰야</b> 하는데,
    /// 한 번 볼 때마다 오브젝트 50개를 다시 만들면 시도 자체를 안 하게 된다.
    /// 고치는 비용이 낮아야 제대로 맞춘다.
    /// </summary>
    private void RelayoutDoorLeaves()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("[칸 조립기] 칸이나 문간을 먼저 고르세요.");
            return;
        }

        var doors = selected.GetComponentsInChildren<Door>(true);
        if (doors.Length == 0)
        {
            Debug.LogWarning($"[칸 조립기] {selected.name} 아래에 Door가 없습니다.", selected);
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("문짝 다시 배치");

        // 같은 부모(문틀) 아래 있는 문짝들이 한 쌍이다. 계층 순서가 곧 좌 → 우 순서다.
        var pairs = new Dictionary<Transform, List<Door>>();
        foreach (var door in doors)
        {
            var frame = door.transform.parent;
            if (frame == null) continue;
            if (!pairs.TryGetValue(frame, out var list)) pairs[frame] = list = new List<Door>();
            list.Add(door);
        }

        int moved = 0;
        foreach (var pair in pairs)
        {
            for (int i = 0; i < pair.Value.Count && i < 2; i++)
            {
                var door = pair.Value[i];
                bool right = i == 1;
                float sign = right ? 1f : -1f;

                Undo.RecordObject(door.transform, "문짝 다시 배치");
                door.transform.SetLocalPositionAndRotation(
                    new Vector3(sign * hingeSpacing * 0.5f, leafHeight, sign * leafDepthGap * 0.5f),
                    right ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);

                Undo.RecordObject(door, "문짝 다시 배치");
                var so = new SerializedObject(door);
                so.FindProperty("openAngle").floatValue = right ? -doorOpenAngle : doorOpenAngle;
                so.ApplyModifiedProperties();

                moved++;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[칸 조립기] 문짝 {moved}짝을 다시 앉혔습니다 — 경첩 간격 {hingeSpacing:0.000}m.", selected);
    }

    /// <summary>
    /// 손으로 만든 문간 통짜를 그대로 놓을 때, <b>조용히 안 되는 것</b>들을 잡아준다.
    ///
    /// 문이 서 있는데 E가 안 먹거나, 매 루프 열린 채로 남거나 하는 사고는 전부
    /// 실행해 보고 한참 뒤에야 눈치채는 종류다. 놓는 김에 확인하고 말해준다.
    /// </summary>
    private static void ValidateDoorway(GameObject doorway)
    {
        var doors = doorway.GetComponentsInChildren<Door>(true);

        if (doors.Length == 0)
        {
            Debug.LogWarning(
                $"[칸 조립기] 문간 통짜에 Door 컴포넌트가 없습니다 — 문 0짝이라 GridManager가 문을 닫지 못하고, " +
                "재활용 순간이 시야에 그대로 노출됩니다.", doorway);
            return;
        }

        int interactable = LayerMask.NameToLayer("Interactable");
        foreach (var door in doors)
        {
            if (interactable >= 0 && door.gameObject.layer != interactable)
            {
                Debug.LogWarning(
                    $"[칸 조립기] {door.name}이 'Interactable' 레이어가 아닙니다 — 문이 서 있어도 E가 안 먹습니다.",
                    door);
            }

            if (door.GetComponentInChildren<Collider>(true) == null)
                Debug.LogWarning($"[칸 조립기] {door.name}에 콜라이더가 없어 조준되지 않습니다.", door);

            if (door.IsOpen)
                Debug.LogWarning($"[칸 조립기] {door.name}이 열린 자세로 저장돼 있습니다 — Door는 첫 자세를 '닫힘'으로 굳힙니다.", door);
        }

        Debug.Log($"[칸 조립기] 문간 통짜 확인 — 한 면당 문 {doors.Length}짝.", doorway);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }

    // ── 섬 ─────────────────────────────────────────────

    /// <summary>
    /// 가운데 정사각 섬. <b>한 물건이 셋을 겸한다</b> — 마주 보는 문 사이 시선을 끊는
    /// 은폐층, <c>SlideAnomaly</c>("서랍 전부 열림")가 설 무대, 그리고 키패드를 박을 판정 지점.
    /// </summary>
    private void BuildIsland(Transform parent)
    {
        if (islandPiece == null || islandPiecesPerSide <= 0) return;

        var group = Group(parent, "Island");
        float half = IslandHalf;

        for (int d = 0; d < 4; d++)
        {
            Vector3 outward = Outward[d];
            Vector3 tangent = new(outward.z, 0f, -outward.x);
            Quaternion facing = FacingRotation(outward, flipWallFacing);   // 섬은 바깥(방 쪽)을 본다

            var side = Group(group, DirNames[d]);

            for (int i = 0; i < islandPiecesPerSide; i++)
            {
                // 조각 중심이 면을 고르게 나눠 갖도록: -half + (i + 0.5) * 폭
                float along = -half + (i + 0.5f) * islandPieceWidth;
                Vector3 pos = tangent * along + outward * (half - wallThickness);

                Place(islandPiece, side, pos, facing, $"Island_{DirNames[d]}_{i}");
            }
        }

        BuildDrawerBank(group);
    }

    /// <summary>
    /// 섬의 남쪽 면에 <b>열리는 시체 서랍</b>을 박는다 — 3안 영안실 벽을 그대로 옮긴 것.
    ///
    /// 원본(HorrorZip)의 구성을 실측해 그대로 쓴다:
    ///  • 정적인 <c>wall_piece_drawer_01</c> 4장(1m씩)이 서랍 <b>문짝 무늬 벽</b>을 이루고
    ///  • 그 앞에 <c>corpse_drawer</c>를 <b>4열 × 3단</b>으로 박아 실제로 열리게 한다
    ///  • 단 높이 0.300 / 1.133 / 1.966 — 간격 0.833은 서랍 자체 높이와 정확히 같다
    ///
    /// 자립형 서랍을 벽에 늘어놓는 것보다 이 편이 낫다. 섬은 어차피 서랍 벽으로 세우므로
    /// <b>이미 있는 무늬에 실물을 맞춰 끼우는 것</b>이고, 한 면에 12짝이 일제히 열려야
    /// "서랍 전부 열림"이 이상현상으로 읽힌다.
    /// </summary>
    private void BuildDrawerBank(Transform parent)
    {
        var prefab = Load($"{propFolder}/corpse_drawer.prefab");
        if (prefab == null) return;

        var group = Group(parent, "Drawers");

        // 남쪽 면을 고른 이유: 시작 자리(남문과 섬 사이)에서 정면으로 보인다.
        // 서랍은 앞으로 열려야 하므로 문짝이 방을 향하게 180° 돌려 세운다.
        var rotation = Quaternion.Euler(0f, 180f, 0f);

        // corpse_drawer는 원점이 +X 모서리라, 180° 돌리면 원점에서 +X로 1m를 차지한다.
        // 열 시작점을 −IslandHalf에 두면 네 열이 섬 폭을 정확히 메운다.
        for (int col = 0; col < islandPiecesPerSide; col++)
        {
            float x = -IslandHalf + col * islandPieceWidth;

            for (int row = 0; row < 3; row++)
            {
                float y = 0.300f + row * 0.833f;   // 원본 실측값

                // 문짝(원점 쪽 얕은 끝)이 섬 표면에 오도록 살짝 안으로. 몸통은 섬 속으로 들어간다.
                Place(prefab, group, new Vector3(x, y, -IslandHalf + 0.054f), rotation,
                      $"corpse_drawer_{col}_{row}");
            }
        }
    }

    // ── 소품 ─────────────────────────────────────────────

    /// <summary>
    /// 소품 하나의 자리. <b>y를 직접 적지 않고 "바닥면이 놓일 높이"만 적는다</b> —
    /// 이 킷은 피벗 규약이 두 갈래(밑면 / 한가운데)로 섞여 있어서, y를 손으로 적으면
    /// 어떤 것은 바닥에 반쯤 파묻힌다(autopsy_table이 그렇다). 실제 y는 재서 계산한다.
    /// </summary>
    /// <summary>벽에 붙일 면. None이면 X·Z를 적은 그대로 쓴다.</summary>
    private enum Wall { None = -1, N = 0, E = 1, S = 2, W = 3 }

    private readonly struct PropSpec
    {
        public readonly string Name;
        public readonly float X, Z, RotY, SurfaceY;
        public readonly Wall Against;

        public PropSpec(string name, float x, float z, float rotY, float surfaceY, Wall against = Wall.None)
        {
            Name = name; X = x; Z = z; RotY = rotY; SurfaceY = surfaceY; Against = against;
        }
    }

    /// <summary>
    /// 20x20 칸 한 채의 소품 배치도.
    ///
    /// 흩뿌리는 알고리즘이 아니라 <b>손으로 정한 배치</b>를 표로 들고 있다. 이 게임은
    /// 관찰이 전부라 방이 또렷하고 외워져야 하는데, 무작위 배치는 매번 달라 외울 수가
    /// 없고 기계적으로 보인다. 칸 프리팹은 하나를 9번 쓰므로 배치도 하나면 충분하다.
    ///
    /// 비워둔 자리 — 문 앞(문폭 1.882), 진입 트리거(문에서 2m), 섬(±2), 키패드(문 옆 2.5m).
    /// </summary>
    private static readonly PropSpec[] Props =
    {
        // ── 벽에 붙이는 것 (Wall을 지정하면 벽에 닿는 좌표는 재서 정한다) ──
        // 북벽 — metal_sink는 5.4m라 한 면의 4분의 1을 먹는다.
        new("metal_sink",         -5.5f,    0f, 180f, 0f,     Wall.N),
        new("steel_worktop",       4.5f,    0f, 180f, 0f,     Wall.N),
        new("cabinet",             7.8f,    0f, 180f, 1.5f,   Wall.N),

        // 동벽
        new("organ_scale",           0f, -5.5f, 270f, 0f,     Wall.E),
        new("ceramic_sink",          0f,  5.5f, 270f, 0f,     Wall.E),
        new("shelf",                 0f,  7.8f, 270f, 1.6f,   Wall.E),
        new("photo_frame",           0f, -8.0f, 270f, 1.5f,   Wall.E),
        new("glove_dispenser",       0f,  2.6f, 270f, 1.4f,   Wall.E),

        // 남벽 — 시체 서랍은 여기 늘어놓지 않는다. 섬에 박는다(BuildDrawerBank 참고).
        new("writing_board",       4.0f,    0f,   0f, 1.0f,   Wall.S),
        new("light_switch",        1.7f,    0f,   0f, 1.3f,   Wall.S),

        // 서벽 — 책상 구역
        new("table",                 0f,  4.5f,   0f, 0f,     Wall.W),
        new("trash_can",             0f, -2.5f,   0f, 0f,     Wall.W),
        new("desk_chair",         -7.4f,  4.5f, 270f, 0f),   // 책상을 바라보게

        // ── 바닥에 서는 것 ──
        // 해부 구역 — 이상현상 무대. 서로 다른 사분면에 두어 방을 비대칭으로 만든다.
        new("autopsy_table",       5.5f,  5.5f,   0f, 0f),
        new("instrument_trolley",  7.5f,  4.0f,   0f, 0f),
        new("autopsy_table",      -5.5f, -5.5f,  90f, 0f),
        new("instrument_trolley", -7.5f, -4.0f,  90f, 0f),
        // 세 번째 해부대는 <b>늘 비어 있다.</b> "시체 증가"가 여기에 눕는다 —
        // 늘 비어 있던 자리라야 한 구 늘어난 것이 눈에 걸린다.
        new("autopsy_table",       5.5f, -5.5f,   0f, 0f),

        // ── 얹는 것 ──
        // 해부대 상판은 비워 둔다 — 거기엔 시체가 눕는다. 도구는 카트와 작업대로 보낸다.
        new("instrument_tray",     7.5f,  4.0f,   0f, 0.971f),   // 카트①
        new("brain_jar",          -7.5f, -4.0f,   0f, 0.971f),   // 카트②

        // 작업대 상판 1.354. 북벽에 등을 붙이면 z는 대략 9.3 언저리다.
        new("glass_jar",           3.9f,  9.3f,   0f, 1.354f),
        new("brain",               4.4f,  9.3f,   0f, 1.354f),
        new("scissors",            4.6f, 9.15f,  10f, 1.354f),
        new("scalp",               4.9f, 9.35f,   0f, 1.354f),
        new("bonesaw",            5.15f,  9.3f,  20f, 1.354f),

        // 책상 상판 1.047. 서벽에 등을 붙이면 x는 대략 −9.2 언저리다.
        new("desk_lamp",          -9.4f,  5.4f,   0f, 1.047f),
        new("notebook",           -9.1f,  4.5f,  15f, 1.047f),
        new("pencil_holder",      -9.4f,  4.9f,   0f, 1.047f),
        new("pencil",            -9.05f,  4.1f,  40f, 1.047f),
    };

    private void BuildProps(Transform parent)
    {
        if (!placeProps) return;

        var group = Group(parent, "Props");
        var cache = new Dictionary<string, GameObject>();
        int placed = 0;
        string missing = "";

        foreach (var spec in Props)
        {
            if (!cache.TryGetValue(spec.Name, out var prefab))
            {
                prefab = Load($"{propFolder}/{spec.Name}.prefab");
                cache[spec.Name] = prefab;
            }

            if (prefab == null)
            {
                if (!missing.Contains(spec.Name)) missing += $" {spec.Name}";
                continue;
            }

            // 피벗 규약이 섞여 있어 y는 재서 정한다 — 바닥면이 SurfaceY에 오도록.
            bool measured = TryMeasureLocal(prefab, out var b);
            float y = spec.SurfaceY - (measured ? b.min.y : 0f);

            var pos = new Vector3(spec.X, y, spec.Z);
            if (spec.Against != Wall.None && measured) pos = SnapToWall(pos, b, spec);

            Place(prefab, group, pos, Quaternion.Euler(0f, spec.RotY, 0f), $"{spec.Name}_{placed}");
            placed++;
        }

        if (missing.Length > 0)
            Debug.LogWarning($"[칸 조립기] 소품 폴더에서 못 찾은 것:{missing}", this);

        Debug.Log($"[칸 조립기] 소품 {placed}개를 놓았습니다.", parent);
    }

    // ── 이상현상 ─────────────────────────────────────────

    /// <summary>
    /// 시체 3구와 이상현상 3종을 세워 배선한다.
    ///
    /// 시체는 해부대 두 대에 한 구씩 <b>보이게</b>, 서랍 위에 한 구를 <b>꺼둔 채</b> 놓는다.
    /// 꺼둔 한 구가 "시체 증가"의 재료다 — 없던 것이 생기는 것처럼 보이려면 미리 만들어
    /// 두고 숨기는 수밖에 없다.
    ///
    /// "서랍 전부 열림"(<see cref="SlideAnomaly"/>)은 여기서 만들지 않는다. 그 스크립트는
    /// 대상을 <c>GetComponentInParent&lt;RoomModule&gt;()</c> 아래에서 찾는데 격자 칸은
    /// <see cref="GridCell"/>이라 <b>탐색 범위가 자기 자신으로 쪼그라들어 0개를 찾는다.</b>
    /// 조용히 아무 일도 안 일어나므로, 고치기 전에 붙여두면 원인을 찾기 어렵다.
    /// </summary>
    private void BuildAnomalies(Transform parent)
    {
        if (!buildAnomalies || corpsePrefab == null) return;

        var corpses = Group(parent, "Corpses");

        // 해부대 두 대 위 — 소품 배치도의 autopsy_table 자리와 회전을 맞춘다.
        var normalA = PlaceCorpse(corpses, "Corpse_A", new Vector3(5.5f, 0f, 5.5f), 0f, TableTop);
        var normalB = PlaceCorpse(corpses, "Corpse_B", new Vector3(-5.5f, 0f, -5.5f), 90f, TableTop);

        // 늘 비어 있는 세 번째 해부대 위 — 평소엔 꺼둔다. 이것이 나타나면 "한 구 늘었다".
        var extra = PlaceCorpse(corpses, "Corpse_Extra", new Vector3(5.5f, 0f, -5.5f), 0f, TableTop);
        if (extra != null) extra.SetActive(false);

        // "시체 이동"이 옮겨갈 자세. 같은 해부대 위에서 비스듬히 밀려난 모습.
        var pose = new GameObject("AnomalyPose_Move");
        Undo.RegisterCreatedObjectUndo(pose, "칸 만들기");
        pose.transform.SetParent(corpses, false);
        pose.transform.SetLocalPositionAndRotation(
            new Vector3(-6.0f, normalB != null ? normalB.transform.localPosition.y : TableTop, -4.6f),
            Quaternion.Euler(0f, 55f, 0f));

        var manager = Group(parent, "Anomalies");
        Undo.AddComponent<AnomalyManager>(manager.gameObject);

        AddToggleAnomaly(manager, "시체 소실", null, normalA);
        AddToggleAnomaly(manager, "시체 증가", extra, null);
        AddTransformAnomaly(manager, "시체 이동", normalB, pose.transform);
        AddSlideAnomaly(manager, "서랍 전부 열림");

        Debug.Log("[칸 조립기] 이상현상 4종을 배선했습니다 (소실 · 증가 · 이동 · 서랍).", manager);
    }

    private GameObject PlaceCorpse(Transform parent, string name, Vector3 pos, float rotY, float surfaceY)
    {
        float y = surfaceY;
        if (TryMeasureLocal(corpsePrefab, out var b)) y -= b.min.y;

        return Place(corpsePrefab, parent, new Vector3(pos.x, y, pos.z), Quaternion.Euler(0f, rotY, 0f), name);
    }

    private static void AddToggleAnomaly(Transform parent, string label, GameObject show, GameObject hide)
    {
        var go = new GameObject(label);
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetParent(parent, false);

        var anomaly = Undo.AddComponent<ToggleAnomaly>(go);
        var so = new SerializedObject(anomaly);
        so.FindProperty("anomalyName").stringValue = label;
        SetObjectArray(so.FindProperty("showOnActivate"), show);
        SetObjectArray(so.FindProperty("hideOnActivate"), hide);
        so.ApplyModifiedProperties();
    }

    private static void AddTransformAnomaly(Transform parent, string label, GameObject target, Transform pose)
    {
        var go = new GameObject(label);
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetParent(parent, false);

        var anomaly = Undo.AddComponent<TransformAnomaly>(go);
        var so = new SerializedObject(anomaly);
        so.FindProperty("anomalyName").stringValue = label;
        so.FindProperty("target").objectReferenceValue = target != null ? target.transform : null;
        so.FindProperty("anomalyPose").objectReferenceValue = pose;
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// "서랍 전부 열림". 대상을 지정하지 않고 <b>이름으로 자동 수집</b>하게 둔다 —
    /// 서랍을 늘리거나 줄여도 배열을 다시 채울 필요가 없다.
    ///
    /// 수집 범위는 <see cref="SlideAnomaly"/>가 부모에서 <see cref="GridCell"/>을 찾아
    /// 정하므로, 이 컴포넌트는 반드시 <b>칸 안쪽</b>에 있어야 한다. 밖에 두면
    /// 범위가 자기 자신으로 좁아져 0개를 찾고 조용히 아무 일도 안 일어난다.
    /// </summary>
    private static void AddSlideAnomaly(Transform parent, string label)
    {
        var go = new GameObject(label);
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetParent(parent, false);

        var anomaly = Undo.AddComponent<SlideAnomaly>(go);
        var so = new SerializedObject(anomaly);
        so.FindProperty("anomalyName").stringValue = label;
        so.ApplyModifiedProperties();
    }

    private static void SetObjectArray(SerializedProperty property, Object value)
    {
        property.arraySize = value != null ? 1 : 0;
        if (value != null) property.GetArrayElementAtIndex(0).objectReferenceValue = value;
    }

    /// <summary>
    /// 소품의 <b>등이 벽 안쪽 면에 닿도록</b> 벽 방향 좌표를 다시 잡는다.
    ///
    /// 벽에 붙는 좌표를 표에 손으로 적으면 반드시 뜨거나 파묻힌다 — 소품마다 두께도
    /// 다르고 피벗도 제각각이라, 벽 위치 하나만 바뀌어도 스무 줄을 다시 재야 한다.
    /// 벽면은 하나뿐이니 <b>어느 벽인지만 적고 나머지는 재서 맞춘다.</b>
    /// </summary>
    private Vector3 SnapToWall(Vector3 pos, Bounds local, PropSpec spec)
    {
        Vector3 n = Outward[(int)spec.Against];
        Bounds rotated = RotateBounds(local, Quaternion.Euler(0f, spec.RotY, 0f));

        // 벽 안쪽 면 = 칸 경계에서 '벽 안쪽 들이기'만큼 들어온 자리.
        float wallInner = Half - doorHalfDepth;

        // (놓을 자리 + 회전된 중심)·n + 벽 방향 반지름 == 벽면
        float target = wallInner
                     - Vector3.Dot(rotated.center, n)
                     - Mathf.Abs(Vector3.Dot(rotated.extents, n));

        return pos - n * Vector3.Dot(pos, n) + n * target;
    }

    /// <summary>회전시킨 경계의 축정렬 범위. 모서리 8개를 돌려 다시 감싼다.</summary>
    private static Bounds RotateBounds(Bounds b, Quaternion rot)
    {
        Vector3 e = b.extents;
        var result = new Bounds(rot * b.center, Vector3.zero);

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);

            result.Encapsulate(rot * (b.center + corner));
        }

        return result;
    }

    /// <summary>프리팹을 원점·무회전으로 잠깐 세워 렌더러 경계를 잰다 (조각 자와 같은 방식).</summary>
    private static bool TryMeasureLocal(GameObject prefab, out Bounds bounds)
    {
        bounds = default;

        var probe = Instantiate(prefab);
        probe.hideFlags = HideFlags.HideAndDontSave;
        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        probe.SetActive(true);

        bool any = false;
        try
        {
            foreach (var r in probe.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }
        finally
        {
            DestroyImmediate(probe);
        }

        return any;
    }

    // ── 천장 조명 ────────────────────────────────────────

    /// <summary>조명이 실제로 몇 개 놓이는지 (섬에 박히는 자리를 뺀 수). 창에 미리 보여주려고 따로 센다.</summary>
    private int CountLightSlots()
    {
        int count = 0;
        ForEachLightSlot((_, _) => count++);
        return count;
    }

    /// <summary>
    /// 천장에 조명을 N×N으로 깐다.
    ///
    /// <b>섬 자리는 건너뛴다.</b> 섬은 <c>wall_piece_drawer_01</c>(높이 3m)로 세우므로
    /// 바닥부터 천장까지 꽉 찬 덩어리다 — 그 위 천장에 조명을 달면 기구가 섬 속에 파묻혀
    /// 빛도 안 나오고 보이지도 않는다. 한 줄 개수가 홀수면 정중앙 자리가 정확히 거기다.
    ///
    /// <b>그림자는 기본으로 끈다.</b> 3안은 건물이 한 채만 켜져 있었지만 격자는 9칸이
    /// 동시에 살아 있어 조명 수가 그대로 9배가 된다. 포인트 라이트 그림자는 큐브맵 6면을
    /// 굽는 가장 비싼 종류라, 개수가 곱해지는 구조에서는 켜둘 수 없다.
    /// </summary>
    private void BuildLights(Transform parent)
    {
        if (ceilingLightPrefab == null || lightGrid <= 0) return;

        var group = Group(parent, "Lights");
        var rotation = Quaternion.Euler(0f, lightRotationY, 0f);
        float y = wallHeight - lightDrop;

        ForEachLightSlot((x, z) =>
        {
            var go = Place(ceilingLightPrefab, group, new Vector3(x, y, z), rotation,
                           $"{ceilingLightPrefab.name}_{x:0.#}_{z:0.#}");

            foreach (var light in go.GetComponentsInChildren<Light>(true))
            {
                if (disableLightShadows) light.shadows = LightShadows.None;
                if (!Mathf.Approximately(lightIntensityScale, 1f)) light.intensity *= lightIntensityScale;
            }
        });
    }

    /// <summary>조명이 갈 자리를 훑는다 — 세는 쪽과 놓는 쪽이 같은 규칙을 쓰도록 한곳에 둔다.</summary>
    private void ForEachLightSlot(System.Action<float, float> action)
    {
        if (lightGrid <= 0) return;

        float step = Side / lightGrid;

        for (int i = 0; i < lightGrid; i++)
        {
            for (int j = 0; j < lightGrid; j++)
            {
                float x = -Half + (i + 0.5f) * step;
                float z = -Half + (j + 0.5f) * step;

                // 섬은 천장까지 닿는 덩어리라 그 위에는 달 수 없다.
                if (Mathf.Abs(x) < IslandHalf && Mathf.Abs(z) < IslandHalf) continue;

                action(x, z);
            }
        }
    }

    // ── 소켓 · 트리거 · 스폰 ──────────────────────────────

    /// <summary>
    /// 소켓은 <b>벽 바깥면(칸 경계)</b>에 둔다. 여기 간격이 그대로
    /// <see cref="GridManager"/>의 칸 간격이 되므로, 이 값이 한 변과 달라지면
    /// 칸 사이에 틈이 생기거나 겹친다.
    /// </summary>
    private RoomSocket BuildSocket(Transform parent, int d, Vector3 outward)
    {
        var group = Group(parent, "Sockets");
        var go = new GameObject($"Socket_{DirNames[d]}");
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetParent(group, false);
        go.transform.SetPositionAndRotation(outward * Half + Vector3.up * (wallHeight * 0.5f),
                                            Quaternion.LookRotation(outward, Vector3.up));
        return go.AddComponent<RoomSocket>();
    }

    /// <summary>
    /// 문 안쪽 진입 판정 볼륨. 로컬 +Z가 <b>방 안쪽</b>을 향해야
    /// <see cref="CellBoundaryTrigger"/>가 "빠져나갔다 / 되돌아갔다"를 구분한다.
    /// </summary>
    private void BuildTriggers(Transform parent)
    {
        var group = Group(parent, "Triggers");
        float depth = unitSize * 0.5f;

        for (int d = 0; d < 4; d++)
        {
            Vector3 outward = Outward[d];

            var go = new GameObject($"Trigger_{DirNames[d]}");
            Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(
                outward * (Half - doorHalfDepth - depth * 0.5f),
                Quaternion.LookRotation(-outward, Vector3.up));   // +Z = 방 안쪽

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(unitSize, wallHeight, depth);
            box.center = new Vector3(0f, wallHeight * 0.5f, 0f);

            go.AddComponent<CellBoundaryTrigger>();
        }
    }

    /// <summary>
    /// 추격자가 설 자리. 누적 오답 3회 때 <b>그것이 그 방의 이상현상</b>이 되므로,
    /// 방에 들어서면 보이되 곧장 코앞은 아닌 자리에 둔다 — 섬 너머 대각선 구석.
    /// 추격자 자체는 씬 단일 존재라(5안 1단계) 여기에는 자리 표시만 둔다.
    /// </summary>
    private void BuildStalkerPoint(Transform parent)
    {
        var go = new GameObject("StalkerPoint");
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(
            new Vector3(-7.5f, 0f, 7.5f),
            Quaternion.LookRotation(new Vector3(1f, 0f, -1f).normalized, Vector3.up));
    }

    /// <summary>게임을 켤 때 플레이어가 설 자리. 남문과 섬 사이에 두고 섬을 바라보게 한다.</summary>
    private Transform BuildSpawnPoint(Transform parent)
    {
        var go = new GameObject("SpawnPoint");
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(
            new Vector3(0f, 0f, -(Half + IslandHalf) * 0.5f),
            Quaternion.LookRotation(Vector3.forward, Vector3.up));
        return go.transform;
    }

    // ── 배선 ───────────────────────────────────────────

    /// <summary>
    /// <see cref="GridCell"/>의 <c>sides</c>·<c>spawnPoint</c>는 private 직렬화 필드라
    /// <see cref="SerializedObject"/>로 채운다. 연결을 손으로 하면 <b>방향을 잘못 적어 넣는
    /// 사고</b>가 나는데, 그건 한참 걷고 나서야 이상하다고 느끼는 종류다.
    /// </summary>
    private static void WireCell(GridCell cell, RoomSocket[] sockets, List<Door>[] doors, Transform spawn)
    {
        var so = new SerializedObject(cell);

        var sides = so.FindProperty("sides");
        sides.arraySize = 4;

        for (int d = 0; d < 4; d++)
        {
            var element = sides.GetArrayElementAtIndex(d);
            element.FindPropertyRelative("direction").enumValueIndex = d;   // N·E·S·W 순서 일치
            element.FindPropertyRelative("socket").objectReferenceValue = sockets[d];

            var doorArray = element.FindPropertyRelative("doors");
            doorArray.arraySize = doors[d].Count;
            for (int i = 0; i < doors[d].Count; i++)
                doorArray.GetArrayElementAtIndex(i).objectReferenceValue = doors[d][i];
        }

        so.FindProperty("spawnPoint").objectReferenceValue = spawn;
        so.ApplyModifiedProperties();
    }

    // ── 잡일 ───────────────────────────────────────────

    private static Transform Group(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// <summary>
    /// 프리팹 링크를 유지한 채 놓는다 — 나중에 원본 에셋이 바뀌면 따라온다.
    ///
    /// 회전은 <b>덮어쓰지 않고 프리팹에 구워진 자세 위에 얹는다.</b> 천장 프리팹은
    /// 바닥과 같은 메시를 X축으로 180° 뒤집어 쓰는데(원본에 그 회전이 저장돼 있다),
    /// 통째로 덮어쓰면 천장이 위를 보고 뒤집혀 <b>아래에서는 안 보인다</b>.
    /// 위치는 반대로 덮어쓰는 게 맞다 — 원본 좌표는 예시 씬에서 놓였던 자리일 뿐이다.
    /// </summary>
    private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, string name)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "칸 만들기");
        go.transform.SetLocalPositionAndRotation(position, rotation * prefab.transform.localRotation);
        go.name = name;
        return go;
    }
}
