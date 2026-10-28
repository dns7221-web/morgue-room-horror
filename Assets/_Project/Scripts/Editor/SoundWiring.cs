using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 소리 파일을 씬에 연결하는 도구 — <c>Tools ▸ 소리 연결 (Sound Wiring)</c>. 추가 생성 (2026-10-28).
///
/// 손으로 하면 오브젝트 여섯 개를 돌며 AudioSource를 붙이고, 클립 15개를 칸마다 끌어다 놓고,
/// (수정 2026-10-28: 문 소리 2개가 늘어 다섯 개·13개 → 여섯 개·15개)
/// 루프 · 2D/3D · 자동 재생을 하나하나 맞춰야 한다. 하나라도 빠지면 조용히 무음이 되는 종류라
/// (예: 배경음 Play On Awake를 안 켜면 아무 오류 없이 안 들린다) 도구로 한 번에 맞춘다.
///
/// ── 무엇을 어디에 ──────────────────────────────────────
///  • 배경음   → 씬 루트 "BGM" 오브젝트의 AudioSource (루프 · 자동 재생 · 2D). <b>코드 없이</b> 엔진 기능만으로 돈다.
///               칸이 아니라 씬 루트에 두는 이유: 칸마다 두면 재활용으로 칸이 옮겨질 때 소리 위치가 튀어 들킨다.
///               긴 파일이라 가져오기 설정을 Streaming으로 바꾼다(통째로 메모리에 풀지 않게).
///  • 경고음   → GameManager의 AudioSource (2D) → <c>stalkerWarningSound</c> 칸에 연결
///  • 깜놀 소리 → ScareFlash의 AudioSource (2D) → <c>scareSound</c> 칸에 연결 (오답 · 잡힘 공용)
///  • 발소리 · 숨소리 → 플레이어의 <see cref="PlayerAudio"/> (AudioSource 두 개, 2D)
///  • 쫓아오는 소리 → 추격자의 <see cref="StalkerAudio"/> (AudioSource 하나, 3D)
///  • 문 소리   → 씬 루트 "DoorAudio" 오브젝트의 <see cref="DoorAudio"/> (클립 두 개만. 소리는 문 자리에서 3D로 낸다)
///               추가 생성 (2026-10-28). 배경음처럼 칸이 아니라 씬 루트에 둔다 — 문짝 36개는 칸 프리팹 안이라 손대지 않는다.
///
/// 클립은 <c>Assets/_Project/Audio</c> 아래에서 <b>파일 이름</b>으로 찾는다 — 하위 폴더로 옮겨도 그대로 찾는다.
/// 여러 번 눌러도 결과가 같다(멱등) — 이미 붙은 부품은 새로 만들지 않고 다시 맞추기만 한다.
/// 볼륨은 <b>처음 붙일 때만</b> 정한다 — 인스펙터에서 맞춰 둔 볼륨을 다시 누를 때마다 덮어쓰지 않게.
/// 한 번의 실행 취소(Ctrl+Z)로 전부 되돌린다. 끝나면 씬을 저장(Ctrl+S)해야 남는다.
/// </summary>
internal static class SoundWiring
{
    private const string AudioFolder = "Assets/_Project/Audio";

    [MenuItem("Tools/소리 연결 (Sound Wiring)")]
    private static void Wire()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[소리 연결] 플레이 중에는 못 한다 — 플레이를 멈추고 다시 눌러 주세요.");
            return;
        }

        Undo.SetCurrentGroupName("소리 연결");
        int undoGroup = Undo.GetCurrentGroup();
        var report = new List<string>();

        WireBgm(report);
        WireWarning(report);
        WireScare(report);
        WirePlayer(report);
        WireStalker(report);
        WireDoors(report);   // 추가 생성 (2026-10-28)

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[소리 연결] 끝 — 씬을 저장(Ctrl+S)해야 남습니다.\n  · " + string.Join("\n  · ", report));
    }

    // ── 배경음 ──────────────────────────────────────────────

    private static void WireBgm(List<string> report)
    {
        var clip = FindClip("BGM_HorrorTrailer_Loop", report);
        if (clip == null) return;

        // 씬 루트의 "BGM"을 찾고, 없으면 만든다
        GameObject go = null;
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "BGM") go = root;
        if (go == null)
        {
            go = new GameObject("BGM");
            Undo.RegisterCreatedObjectUndo(go, "소리 연결");
        }

        var source = GetOrAdd(go, out bool added);
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = true;      // 이게 꺼져 있으면 아무 오류 없이 무음이다
        source.spatialBlend = 0f;       // 2D — 어디서 듣든 같게
        if (added) source.volume = 0.5f;

        // 2분 가까운 파일 — 통째로 풀어 두지 않고 재생하면서 읽는다
        if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) is AudioImporter importer &&
            importer.defaultSampleSettings.loadType != AudioClipLoadType.Streaming)
        {
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            report.Add("배경음 가져오기 설정 → Streaming");
        }

        report.Add($"배경음 → 씬 루트 'BGM' (볼륨 {source.volume:F2})");
    }

    // ── 추격자 등장 경고음 ──────────────────────────────────

    private static void WireWarning(List<string> report)
    {
        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null) { report.Add("⚠ 경고음 — 씬에 GameManager가 없어 건너뜀"); return; }

        var clip = FindClip("SFX_Stalker_Warning", report);
        if (clip == null) return;

        // 이미 연결된 소스가 있으면 그걸 다시 맞춘다 — 누를 때마다 AudioSource가 늘어나지 않게
        var so = new SerializedObject(gm);
        var field = so.FindProperty("stalkerWarningSound");
        var source = field.objectReferenceValue as AudioSource;
        bool added = false;
        if (source == null) { source = Undo.AddComponent<AudioSource>(gm.gameObject); added = true; }
        else Undo.RecordObject(source, "소리 연결");

        source.clip = clip;
        source.loop = false;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        if (added) source.volume = 0.9f;

        field.objectReferenceValue = source;
        so.ApplyModifiedProperties();
        report.Add($"경고음 → {gm.name} (볼륨 {source.volume:F2})");
    }

    // ── 깜놀 소리 (오답 · 잡힘) ─────────────────────────────

    // 추가 생성 (2026-10-28) — 번쩍임(ScareFlash)과 같은 오브젝트에 붙인다. 번쩍임이 소리를 직접 내므로
    // 오답·잡힘 두 곳에 따로 연결할 필요가 없다(ScareFlash 주석 참고).
    private static void WireScare(List<string> report)
    {
        var flash = Object.FindAnyObjectByType<ScareFlash>(FindObjectsInactive.Include);
        if (flash == null) { report.Add("⚠ 깜놀 소리 — 씬에 ScareFlash가 없어 건너뜀"); return; }

        var clip = FindClip("SFX_Scare", report);
        if (clip == null) return;

        var so = new SerializedObject(flash);
        var field = so.FindProperty("scareSound");
        var source = field.objectReferenceValue as AudioSource;
        bool added = false;
        if (source == null) { source = Undo.AddComponent<AudioSource>(flash.gameObject); added = true; }
        else Undo.RecordObject(source, "소리 연결");

        source.clip = clip;
        source.loop = false;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        if (added) source.volume = 1f;

        field.objectReferenceValue = source;
        so.ApplyModifiedProperties();
        report.Add($"깜놀 소리 → {flash.name} (볼륨 {source.volume:F2}, 오답 · 잡힘 공용)");
    }

    // ── 플레이어: 발소리 · 탈진 숨소리 ──────────────────────

    private static void WirePlayer(List<string> report)
    {
        var player = Object.FindAnyObjectByType<FirstPersonController>();
        if (player == null) { report.Add("⚠ 발소리 — 씬에 FirstPersonController가 없어 건너뜀"); return; }

        var audio = player.GetComponent<PlayerAudio>();
        if (audio == null) audio = Undo.AddComponent<PlayerAudio>(player.gameObject);

        var so = new SerializedObject(audio);
        var footSource = EnsureSource(so.FindProperty("footstepSource"), player.gameObject, 0.6f);
        var breathSource = EnsureSource(so.FindProperty("breathSource"), player.gameObject, 1f);

        // 발소리용과 숨소리용이 같은 소스를 가리키면 숨소리 높낮이가 출렁인다(PlayerAudio 주석) — 갈라 둔다
        if (footSource == breathSource)
        {
            breathSource = Undo.AddComponent<AudioSource>(player.gameObject);
            Setup2D(breathSource);
            so.FindProperty("breathSource").objectReferenceValue = breathSource;
        }

        SetClips(so.FindProperty("walkSteps"), "SFX_Footstep_Walk_", report);
        SetClips(so.FindProperty("runSteps"), "SFX_Footstep_Run_", report);
        so.FindProperty("exhaustedBreath").objectReferenceValue = FindClip("SFX_Player_Breath_Exhausted", report);
        so.ApplyModifiedProperties();

        report.Add($"발소리 · 숨소리 → {player.name} (걷기 {so.FindProperty("walkSteps").arraySize}개, " +
                   $"대쉬 {so.FindProperty("runSteps").arraySize}개)");
    }

    // ── 추격자: 쫓아오는 소리 ────────────────────────────────

    private static void WireStalker(List<string> report)
    {
        var stalker = Object.FindAnyObjectByType<Stalker>(FindObjectsInactive.Include);
        if (stalker == null) { report.Add("⚠ 쫓아오는 소리 — 씬에 Stalker가 없어 건너뜀"); return; }

        var clip = FindClip("SFX_Stalker_Drag_Loop", report);
        if (clip == null) return;

        // StalkerAudio는 AudioSource를 요구(RequireComponent)하므로 붙이면 같이 붙는다
        if (stalker.GetComponent<StalkerAudio>() == null) Undo.AddComponent<StalkerAudio>(stalker.gameObject);
        var source = stalker.GetComponent<AudioSource>();
        Undo.RecordObject(source, "소리 연결");

        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;                       // 3D — 등 뒤 어느 쪽에서 오는지 들려야 한다
        source.rolloffMode = AudioRolloffMode.Linear;   // 칸 하나(약 20m) 밖에서는 안 들리게
        source.minDistance = 2f;
        source.maxDistance = 20f;
        source.volume = 0f;                             // 실제 볼륨은 StalkerAudio가 움직임에 따라 올린다

        report.Add($"쫓아오는 소리 → {stalker.name} (3D, 20m까지)");
    }

    // ── 문: 여는 소리 · 닫히는 소리 ─────────────────────────

    // 추가 생성 (2026-10-28) — 씬 루트의 "DoorAudio"를 찾고, 없으면 만든다(배경음과 같은 방식).
    // AudioSource는 붙이지 않는다 — DoorAudio가 소리를 낼 때마다 문 자리에 임시 소리를 만든다(DoorAudio 주석).
    private static void WireDoors(List<string> report)
    {
        var openClip = FindClip("SFX_Door_Open", report);
        var shutClip = FindClip("SFX_Door_Shut", report);
        if (openClip == null && shutClip == null) return;

        GameObject go = null;
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "DoorAudio") go = root;
        if (go == null)
        {
            go = new GameObject("DoorAudio");
            Undo.RegisterCreatedObjectUndo(go, "소리 연결");
        }

        var audio = go.GetComponent<DoorAudio>();
        if (audio == null) audio = Undo.AddComponent<DoorAudio>(go);

        // 못 찾은 쪽은 비워 둔다 — 그 소리만 빠지고 나머지는 그대로 돈다
        var so = new SerializedObject(audio);
        so.FindProperty("openSound").objectReferenceValue = openClip;
        so.FindProperty("shutSound").objectReferenceValue = shutClip;
        so.ApplyModifiedProperties();

        report.Add($"문 소리 → 씬 루트 'DoorAudio' (여는 소리 {(openClip != null ? "있음" : "없음")}, " +
                   $"닫히는 소리 {(shutClip != null ? "있음" : "없음")})");
    }

    // ── 도우미 ──────────────────────────────────────────────

    /// <summary>오디오 폴더(하위 폴더 포함)에서 파일 이름이 정확히 같은 클립을 찾는다.</summary>
    private static AudioClip FindClip(string fileName, List<string> report)
    {
        foreach (var guid in AssetDatabase.FindAssets($"{fileName} t:AudioClip", new[] { AudioFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == fileName)
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        report.Add($"⚠ {AudioFolder}에서 '{fileName}'을 못 찾음 — 이 소리는 빠진다");
        return null;
    }

    /// <summary>이름이 prefix01, prefix02 … 인 클립을 번호 순서대로 배열 칸에 채운다.</summary>
    private static void SetClips(SerializedProperty array, string prefix, List<string> report)
    {
        var clips = new List<AudioClip>();
        for (int i = 1; i <= 9; i++)
        {
            string name = $"{prefix}{i:00}";
            if (AssetDatabase.FindAssets($"{name} t:AudioClip", new[] { AudioFolder }).Length == 0) break;
            var clip = FindClip(name, report);
            if (clip != null) clips.Add(clip);
        }
        if (clips.Count == 0) report.Add($"⚠ '{prefix}01'부터 시작하는 발소리가 없음");

        array.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }

    /// <summary>필드에 연결된 AudioSource를 쓰고, 비어 있으면 새로 붙여 연결한다. 볼륨은 처음 붙일 때만.</summary>
    private static AudioSource EnsureSource(SerializedProperty field, GameObject owner, float volume)
    {
        var source = field.objectReferenceValue as AudioSource;
        if (source == null)
        {
            source = Undo.AddComponent<AudioSource>(owner);
            source.volume = volume;
            field.objectReferenceValue = source;
        }
        else Undo.RecordObject(source, "소리 연결");

        Setup2D(source);
        return source;
    }

    private static void Setup2D(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
    }

    /// <summary>오브젝트의 첫 AudioSource를 쓰고, 없으면 붙인다.</summary>
    private static AudioSource GetOrAdd(GameObject go, out bool added)
    {
        var source = go.GetComponent<AudioSource>();
        added = source == null;
        if (added) source = Undo.AddComponent<AudioSource>(go);
        else Undo.RecordObject(source, "소리 연결");
        return source;
    }
}
