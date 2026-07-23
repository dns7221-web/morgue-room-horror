using UnityEngine;

/// <summary>
/// 이상현상 관리자. 등록된 이상현상 목록에서 랜덤으로 하나를 골라 발동시키고,
/// 방이 리셋될 때 전부 정상 상태로 복원한다.
///
/// GameManager와의 관계:
///  GameManager가 "이번 방은 이상현상 있음/없음"만 정해서 SetAnomaly(bool)를 호출
///  → 실제로 '무엇을 어떻게' 바꿀지는 전부 여기와 개별 Anomaly가 담당 (관심사 분리).
/// </summary>
public class AnomalyManager : MonoBehaviour
{
    [Tooltip("사용할 이상현상들. 씬의 Anomaly 컴포넌트들을 등록.")]
    [SerializeField] private Anomaly[] anomalies;

    // 현재 발동 중인 이상현상 (없으면 null).
    private Anomaly current;

    /// <summary>현재 발동 중인 이상현상 이름 (디버그/로그용). 없으면 "없음".</summary>
    public string CurrentName => current != null ? current.AnomalyName : "없음";

    private void Start()
    {
        // 시작 시 전부 정상 상태로 정리 (씬에서 실수로 켜져 있어도 복원).
        DeactivateAll();
    }

    /// <summary>
    /// 방 세팅. hasAnomaly가 true면 목록에서 랜덤으로 하나 발동,
    /// false면 전부 꺼서 완전한 정상 방을 만든다.
    /// </summary>
    public void SetAnomaly(bool hasAnomaly)
    {
        // 항상 이전 상태부터 완전 복원 (이전 이상현상이 남는 버그 방지).
        DeactivateAll();

        if (!hasAnomaly || anomalies == null || anomalies.Length == 0)
            return;

        current = anomalies[Random.Range(0, anomalies.Length)];
        current.Activate();
        Debug.Log($"[AnomalyManager] 발동: {current.AnomalyName}");
    }

    /// <summary>모든 이상현상을 정상 상태로 복원.</summary>
    private void DeactivateAll()
    {
        current = null;
        if (anomalies == null) return;
        foreach (var a in anomalies)
            if (a != null) a.Deactivate();
    }
}
