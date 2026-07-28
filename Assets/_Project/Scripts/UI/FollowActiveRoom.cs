using UnityEngine;

/// <summary>
/// 씬에 고정으로 놓인 월드 스페이스 오브젝트(진행도 UI 등)를
/// <b>현재 활성 방 모듈</b>에 붙어 다니게 만든다.
///
/// ── 왜 필요한가 ──────────────────────────────────────
/// 방 모듈은 루프마다 좌/우 슬롯을 번갈아 오간다(RoomModulePool). 그런데 씬 루트에
/// 놓인 월드 UI는 제자리에 남으므로, 건물이 반대쪽 슬롯에 서는 회차(짝수 회차)에는
/// UI만 옛 자리에 덩그러니 남아 "표시가 안 나온다"처럼 보인다.
///
/// 이 컴포넌트는 처음 한 번 "활성 모듈 기준 상대 자세"를 기억해두고, 매 프레임 그
/// 상대 자세를 <b>현재</b> 활성 모듈에 다시 적용한다. 덕분에 모듈이 어느 슬롯으로
/// 가든 UI는 늘 같은 자리(키패드 위)에 붙어 있다.
///
/// 상대 자세를 코드로 계산하므로 씬에서 UI를 옮겨도 그 위치가 그대로 기준이 된다
/// — 좌표를 하드코딩하지 않는다.
///
/// 배치: 따라다녀야 할 월드 스페이스 Canvas(진행도 UI의 루트 오브젝트).
/// </summary>
public class FollowActiveRoom : MonoBehaviour
{
    // 활성 모듈 기준 상대 자세 (최초 1회만 기억).
    private Vector3 localPosition;
    private Quaternion localRotation;
    private bool captured;

    // LateUpdate인 이유: 모듈 배치(Recycle)가 끝난 뒤에 따라가야 한 프레임도 어긋나지 않는다.
    private void LateUpdate()
    {
        var room = GameManager.Instance != null ? GameManager.Instance.ActiveRoom : null;
        if (room == null) return;   // 아직 풀 초기화 전 — 다음 프레임에 다시 시도

        if (!captured)
        {
            // 씬에 손으로 맞춰둔 자리를 '모듈 기준 상대 자세'로 환산해 기억한다.
            localPosition = room.InverseTransformPoint(transform.position);
            localRotation = Quaternion.Inverse(room.rotation) * transform.rotation;
            captured = true;
            return;
        }

        transform.SetPositionAndRotation(
            room.TransformPoint(localPosition),
            room.rotation * localRotation);
    }
}
