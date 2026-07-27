using UnityEngine;

/// <summary>
/// 방 모듈끼리 맞물리는 접합부(소켓) 마커. 모듈 프리팹의 복도 입구에 둔다.
///
/// ── 왜 소켓인가 ────────────────────────────────────────
/// "앵커 좌표를 씬에 하드코딩"하면 모듈 크기를 조금만 바꿔도 전부 다시
/// 재는 수작업이 된다. 대신 모듈이 자기 접합부를 스스로 선언하게 하고,
/// 배치는 소켓끼리 맞추는 계산으로 뽑아낸다 (모듈러 레벨 조립의 표준 패턴).
/// → 모듈 지오메트리를 늘리든 줄이든, 소켓만 따라 옮기면 배치는 자동.
///
/// 규약: forward(파란 축)는 <b>모듈 바깥</b>을 향한다. 두 모듈은 소켓이
/// 서로 마주보도록(forward 반대) 붙는다.
/// </summary>
public class RoomSocket : MonoBehaviour
{
    [Tooltip("에디터에서 소켓 크기를 눈으로 확인하기 위한 기즈모 반경.")]
    [SerializeField] private float gizmoRadius = 1.5f;

    // 소켓은 눈에 보이지 않는 빈 오브젝트라, 기즈모가 없으면 배치가 불가능하다.
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * gizmoRadius * 2f);
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, gizmoRadius);
    }
}
