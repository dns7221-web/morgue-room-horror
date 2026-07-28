using UnityEngine;

/// <summary>
/// 오브젝트의 위치·회전을 바꾸는 이상현상 (예: 시체가 돌아누움/삐져나옴).
///
/// 발동 시 target을 anomalyPose(빈 오브젝트로 잡은 '이상 자세' 지점)로 옮기고,
/// 해제 시 원래 자리로 되돌린다. 원래 값은 Awake에서 자동 저장하므로
/// 씬에서 정상 상태 그대로 두면 된다.
/// </summary>
public class TransformAnomaly : Anomaly
{
    [Tooltip("움직일 대상 (예: 시체).")]
    [SerializeField] private Transform target;

    [Tooltip("이상 상태의 위치·회전을 나타내는 기준점. 빈 오브젝트를 원하는 자리에 두고 연결.")]
    [SerializeField] private Transform anomalyPose;

    // 정상 상태(원래 자리) 자동 저장용.
    // ── 함정: 저장은 반드시 '로컬' 좌표로 ────────────────
    // 월드 좌표(target.position)로 저장하면, LoopGroup 전체가 이동하는
    // 순간 target도 같이 움직이지만 저장된 값은 이동 전 좌표 그대로라
    // Deactivate() 시 시체가 옛 위치(방이 있던 자리)로 튕겨나간다.
    //
    // 저장(로컬)과 적용(Activate의 월드)이 서로 다른 이유:
    //  • 저장값은 Awake에서 한 번 떠서 <b>모듈이 옮겨다녀도</b> 계속 유효해야 한다 → 로컬
    //  • anomalyPose는 <b>읽는 시점에</b> 이미 제자리에 있는 살아있는 기준점이다 → 월드
    // 부모가 서로 달라도 안전하다.
    private Vector3 normalLocalPosition;
    private Quaternion normalLocalRotation;

    private void Awake()
    {
        if (target != null)
        {
            normalLocalPosition = target.localPosition;
            normalLocalRotation = target.localRotation;
        }
    }

    public override void Activate()
    {
        if (target == null || anomalyPose == null) return;

        // ── 여기는 '월드' 좌표를 쓴다 (저장은 로컬인데 왜 다른가) ──────────
        // 로컬 좌표를 그대로 복사하면 target과 anomalyPose의 <b>부모가 같아야만</b>
        // 맞다. 실제로는 시체가 corpseBG 자식이고 기준점은 모듈 루트 자식이라,
        // 로컬로 복사하면 corpseBG의 위치만큼 통째로 밀린 엉뚱한 자리로 날아간다.
        //
        // anomalyPose는 이 모듈 안에 있어 모듈과 함께 움직이고, 이 함수는 모듈이
        // 배치를 끝낸 뒤에 호출된다. 그래서 지금 읽는 월드 자세는 언제나 정답이다.
        target.SetPositionAndRotation(anomalyPose.position, anomalyPose.rotation);
    }

    public override void Deactivate()
    {
        if (target == null) return;
        target.localPosition = normalLocalPosition;
        target.localRotation = normalLocalRotation;
    }
}
