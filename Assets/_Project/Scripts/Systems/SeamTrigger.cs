using UnityEngine;

/// <summary>
/// 복도 중간에 두는 트리거 볼륨 — 공간 반복의 <b>이음새</b>.
/// 플레이어가 지나가면 GameManager에 "다음 방으로 진행"을 요청한다.
///
/// ── 배치가 곧 연출이다 ─────────────────────────────────
/// 방문 바로 앞에 두면 나오자마자 암전돼서 티가 난다. 복도를 <b>한참 걷다가</b>
/// 자연스럽게 걸리도록 문에서 충분히 떨어뜨려야, "걷다 보니 어두워졌다"는
/// 느낌이 산다. 콜라이더는 플레이어가 못 피하게 복도 폭·높이를 꽉 채울 것.
///
/// 실제 스왑은 GameManager → ScreenFader가 화면을 완전히 검게 만든 뒤에
/// 일어나므로, 플레이어는 건물이 옮겨가는 걸 절대 볼 수 없다.
///
/// 배치: 각 모듈 프리팹의 복도 중간에 빈 오브젝트 + Collider(Is Trigger).
/// </summary>
[RequireComponent(typeof(Collider))]
public class SeamTrigger : MonoBehaviour
{
    // 컴포넌트 추가 시 자동으로 Is Trigger 켜기.
    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 플레이어만 반응 (PlayerTeleporter 보유 여부로 식별).
        if (other.GetComponentInParent<PlayerTeleporter>() == null) return;

        if (GameManager.Instance != null)
            GameManager.Instance.AdvanceToNextRoom();
    }
}
