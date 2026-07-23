using UnityEngine;

/// <summary>
/// 모든 이상현상의 공통 베이스(추상 클래스).
///
/// 설계: "시스템은 1개, 변형은 N개".
/// AnomalyManager는 이 타입만 알면 되고, 각 변형(소실/이동/증가...)은
/// 이 클래스를 상속해 '나타나는 방법(Activate)'과 '되돌리는 방법(Deactivate)'만
/// 각자 구현한다. 새 이상현상을 추가해도 매니저 코드는 바뀌지 않는다.
/// (IInteractable과 같은 확장 철학 — 다형성)
/// </summary>
public abstract class Anomaly : MonoBehaviour
{
    [Tooltip("에디터/로그 식별용 이름. 예: 시체 소실")]
    [SerializeField] private string anomalyName = "이상현상";

    public string AnomalyName => anomalyName;

    /// <summary>이상현상을 발동시킨다 (방이 '이상 상태'가 됨).</summary>
    public abstract void Activate();

    /// <summary>이상현상을 되돌린다 (방이 '정상 상태'로 복원됨).</summary>
    public abstract void Deactivate();
}
