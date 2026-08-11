using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 여러 오브젝트를 <b>일제히 같은 만큼 열어젖히는</b> 이상현상.
/// 대표 용도: 영안실 시체 서랍 문이 <b>전부 열려 있다</b>.
///
/// ── ToggleAnomaly / TransformAnomaly와 뭐가 다른가 ────
///  • ToggleAnomaly    : 켜고 끄기 (있다 / 없다)
///  • TransformAnomaly : <b>하나</b>를 지정한 자세로 옮기기
///  • SlideAnomaly     : <b>여럿</b>을 각자 자세 기준으로 똑같이 열기
///
/// 문마다 '열린 자세' 기준점을 만들면 11개를 일일이 배치해야 한다. 하지만 열리는
/// 동작은 결국 <b>경첩을 축으로 도는 것</b> 하나뿐이라, 기준점 대신 닫힘 상태
/// 기준의 <b>회전·이동 차이</b>만 받아 전부에 똑같이 적용한다.
///
/// ── 왜 회전만으로는 부족한가 ──────────────────────────
/// 문은 자기 원점이 아니라 <b>경첩(가장자리)</b>을 축으로 돈다. 그래서 회전과 함께
/// 위치도 조금 따라 움직인다. 두 값을 같이 넣어야 실제로 열린 모습이 나온다.
///
/// ── 손이 덜 가게 만든 것 ──────────────────────────────
///  • targets를 비워두면 이름으로 <b>자동 수집</b> (서랍 11개를 끌어다 놓지 않아도 된다)
///  • 컴포넌트 우클릭 <b>미리보기</b>로 Play 없이 씬 뷰에서 바로 확인
/// </summary>
public class SlideAnomaly : Anomaly
{
    [Header("대상")]
    [Tooltip("밀어낼 오브젝트들. <b>비워두면</b> 아래 이름으로 자동으로 찾는다.")]
    [SerializeField] private Transform[] targets;

    // 주의: 서랍 '전체'(corpse_drawer 1…11)가 아니라 <b>문짝만</b> 잡아야 한다.
    // "corpse_drawer"로 걸면 부모(서랍 통짜)와 자식(문짝)이 <b>둘 다</b> 걸려
    // 서랍이 통째로 밀려난다. 문짝 이름을 정확히 지정할 것.
    [Tooltip("자동 수집할 이름 (부분 일치, 대소문자 무시). 서랍은 문짝만 움직여야 한다.")]
    [SerializeField] private string autoFindName = "corpse_drawer_door";

    [Header("얼마나 열리나 (닫힘 상태 기준 '차이'를 넣는다)")]
    [Tooltip("경첩 회전(도). 시체 서랍 문은 Y축 -42.52가 완전히 열린 값.")]
    [SerializeField] private Vector3 rotationOffset = new Vector3(0f, -42.52f, 0f);

    [Tooltip("회전에 딸려오는 위치 이동(m). 문이 자기 원점이 아니라 경첩을 축으로 돌기 때문에 필요하다.")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0.0176f, 0f, -0.1028f);

    [Tooltip("서랍마다 나온 정도를 들쭉날쭉하게 (0~1). 0이면 전부 똑같이 나온다.")]
    [SerializeField, Range(0f, 1f)] private float variation = 0.15f;

    // 정상 상태(원래 자리). TransformAnomaly와 같은 이유로 반드시 '로컬' 좌표다 —
    // 월드로 저장하면 건물이 좌우 슬롯으로 옮겨간 뒤 복원할 때 옛 자리로 튕겨나간다.
    // 에디터 미리보기가 재컴파일을 넘겨도 원위치를 잃지 않도록 직렬화해 둔다.
    [SerializeField, HideInInspector] private Vector3[] normalLocalPositions;
    [SerializeField, HideInInspector] private Quaternion[] normalLocalRotations;
    [SerializeField, HideInInspector] private bool previewOpen;

    /// <summary>
    /// 원본 자세가 대상 수와 <b>둘 다</b> 맞아떨어지는지.
    /// 위치 배열만 보고 회전 배열을 빠뜨리면 그 자리에서 NullReference가 난다.
    /// </summary>
    private bool NormalsReady =>
        targets != null &&
        normalLocalPositions != null && normalLocalPositions.Length == targets.Length &&
        normalLocalRotations != null && normalLocalRotations.Length == targets.Length;

    private void Awake()
    {
        CollectTargets();
        CacheNormals(force: true);   // 갓 생성된 모듈은 언제나 '정상' 상태다
    }

    public override void Activate()
    {
        if (!NormalsReady) CacheNormals();
        if (!NormalsReady) return;

        for (int i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (t == null) continue;

            // 전부 자로 잰 듯 똑같이 열리면 오히려 인공적으로 보인다.
            float amount = 1f - Random.Range(0f, variation);

            Quaternion normalRot = normalLocalRotations[i];

            // 위치 이동은 '닫힌 자세 기준'으로 돌려서 적용한다.
            // 서랍이 어느 벽에 붙어 있든 각자 올바른 방향으로 열린다.
            Vector3 delta = normalRot * (positionOffset * amount);

            // 원래 자세에서 매번 다시 계산하므로 여러 번 발동해도 누적되지 않는다.
            t.localPosition = normalLocalPositions[i] + delta;
            t.localRotation = normalRot * Quaternion.Euler(rotationOffset * amount);
        }
    }

    public override void Deactivate()
    {
        // 되돌릴 원본이 없으면 아무것도 하지 않는다.
        // 여기서 새로 뜨면 '열린 자세'를 정상으로 굳혀버릴 수 있어 더 위험하다.
        if (!NormalsReady) return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            targets[i].localPosition = normalLocalPositions[i];
            targets[i].localRotation = normalLocalRotations[i];
        }
    }

    /// <summary>
    /// targets가 비어 있으면 이름으로 자동 수집한다.
    ///
    /// 탐색 범위를 <b>이 방 안</b>으로 한정하는 게 핵심이다. transform.root에서 찾으면
    /// 풀에 함께 매달린 <b>다른 방의 서랍까지</b> 긁어와 엉뚱한 방이 열린다.
    ///
    /// 방을 대표하는 것이 3안은 <see cref="RoomModule"/>, 3x3 격자는 <see cref="GridCell"/>로
    /// 서로 다르다. 둘 다 보지 않으면 격자에서는 <b>범위가 자기 자신으로 쪼그라들어 0개를
    /// 찾고</b>, 그런데도 조용히 넘어가서 "이 이상현상만 안 나온다"로 나타난다.
    /// </summary>
    private void CollectTargets()
    {
        if (targets != null && targets.Length > 0) return;
        if (string.IsNullOrEmpty(autoFindName)) return;

        Transform root = FindRoomRoot();

        var found = new List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.IndexOf(autoFindName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                found.Add(t);

        targets = found.ToArray();
    }

    /// <summary>이 이상현상이 속한 '방'의 루트. 못 찾으면 자기 자신(범위를 넓히는 쪽으로 틀리지 않는다).</summary>
    private Transform FindRoomRoot()
    {
        var module = GetComponentInParent<RoomModule>();
        if (module != null) return module.transform;

        var cell = GetComponentInParent<GridCell>();
        if (cell != null) return cell.transform;

        return transform;
    }

    /// <summary>
    /// 지금 자세를 '정상'으로 떠 둔다.
    /// 미리보기로 <b>열어둔 상태</b>에서는 절대 뜨지 않는다 — 열린 자세가 정상으로 굳어버린다.
    /// </summary>
    private void CacheNormals(bool force = false)
    {
        if (targets == null) return;
        if (previewOpen) return;
        if (!force && NormalsReady) return;

        normalLocalPositions = new Vector3[targets.Length];
        normalLocalRotations = new Quaternion[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) { normalLocalRotations[i] = Quaternion.identity; continue; }
            normalLocalPositions[i] = targets[i].localPosition;
            normalLocalRotations[i] = targets[i].localRotation;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 이름 조건(autoFindName)을 바꾼 뒤 누른다. 기존 배열을 <b>비우고</b> 다시 모은다.
    /// targets가 차 있으면 자동 수집이 건너뛰므로, 조건만 고쳐서는 반영되지 않기 때문.
    /// </summary>
    [ContextMenu("대상 다시 찾기 (이름 조건 바꾼 뒤)")]
    private void RecollectTargets()
    {
        PreviewClose();   // 열어둔 상태면 먼저 원위치로

        targets = null;
        normalLocalPositions = null;
        normalLocalRotations = null;
        CollectTargets();
        CacheNormals();

        UnityEditor.EditorUtility.SetDirty(this);
        int n = targets != null ? targets.Length : 0;
        Debug.Log($"[SlideAnomaly] '{autoFindName}' 조건으로 {n}개를 찾았습니다.", this);
    }

    [ContextMenu("미리보기 ▶ 서랍 열기")]
    private void PreviewOpen()
    {
        CollectTargets();
        if (targets == null || targets.Length == 0)
        {
            Debug.LogWarning($"[SlideAnomaly] '{autoFindName}' 이름으로 찾은 대상이 없습니다.", this);
            return;
        }
        if (previewOpen) return;   // 이미 열어둔 상태를 '정상'으로 착각하지 않게

        UnityEditor.Undo.RecordObjects(targets, "SlideAnomaly 미리보기");
        CacheNormals();
        Activate();
        previewOpen = true;

        Debug.Log($"[SlideAnomaly] 대상 {targets.Length}개를 열었습니다. 확인 후 '되돌리기'를 누르세요.", this);
    }

    [ContextMenu("미리보기 ▶ 되돌리기")]
    private void PreviewClose()
    {
        if (!previewOpen) return;

        if (targets != null) UnityEditor.Undo.RecordObjects(targets, "SlideAnomaly 되돌리기");
        Deactivate();
        previewOpen = false;
    }
#endif
}
