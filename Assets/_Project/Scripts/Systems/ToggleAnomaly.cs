using UnityEngine;

/// <summary>
/// 오브젝트를 켜고/꺼서 만드는 이상현상 (가장 범용적인 변형).
///
///  • 시체 소실: hideOnActivate에 시체 등록 → 발동 시 사라짐
///  • 시체 증가: showOnActivate에 숨겨둔 여분 시체 등록 → 발동 시 나타남
///  • 조합도 가능 (하나 사라지고 다른 게 나타나는 등)
///
/// Deactivate()가 반대로 되돌리므로 방은 항상 정상 상태로 복원된다.
/// </summary>
public class ToggleAnomaly : Anomaly
{
    [Tooltip("발동 시 '켤' 오브젝트들 (평소엔 꺼둠). 예: 여분 시체")]
    [SerializeField] private GameObject[] showOnActivate;

    [Tooltip("발동 시 '끌' 오브젝트들 (평소엔 켜둠). 예: 사라질 시체")]
    [SerializeField] private GameObject[] hideOnActivate;

    public override void Activate()
    {
        foreach (var go in showOnActivate) if (go != null) go.SetActive(true);
        foreach (var go in hideOnActivate) if (go != null) go.SetActive(false);
    }

    public override void Deactivate()
    {
        foreach (var go in showOnActivate) if (go != null) go.SetActive(false);
        foreach (var go in hideOnActivate) if (go != null) go.SetActive(true);
    }
}
