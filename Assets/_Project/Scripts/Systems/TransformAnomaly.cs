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
    private Vector3 normalPosition;
    private Quaternion normalRotation;

    private void Awake()
    {
        if (target != null)
        {
            normalPosition = target.position;
            normalRotation = target.rotation;
        }
    }

    public override void Activate()
    {
        if (target == null || anomalyPose == null) return;
        target.SetPositionAndRotation(anomalyPose.position, anomalyPose.rotation);
    }

    public override void Deactivate()
    {
        if (target == null) return;
        target.SetPositionAndRotation(normalPosition, normalRotation);
    }
}
