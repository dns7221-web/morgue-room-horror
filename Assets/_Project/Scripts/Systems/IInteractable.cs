/// <summary>
/// 플레이어가 상호작용할 수 있는 모든 대상의 공통 규칙(계약).
///
/// 문·서랍·스위치 등이 이 인터페이스를 구현하면, PlayerInteractor는
/// 대상이 "무엇인지" 몰라도 <see cref="Interact"/>만 호출하면 된다.
/// → 폴리모피즘/디커플링. 새 상호작용 물체를 추가해도 플레이어 코드는 안 바뀐다.
/// </summary>
public interface IInteractable
{
    /// <summary>조준한 상태에서 상호작용 키를 눌렀을 때 실행할 동작.</summary>
    void Interact();

    /// <summary>
    /// 조준 중일 때 UI(조준점 등)에 띄울 안내 문구. 예: "문 열기 (E)".
    /// 표시할 필요가 없으면 null을 반환한다(예: 이미 열린 문).
    /// </summary>
    string Prompt { get; }
}
