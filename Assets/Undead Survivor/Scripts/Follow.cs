using UnityEngine;

public class Follow : MonoBehaviour
{
    [Tooltip("플레이어 기준 월드 오프셋 (UI 배율과 무관하게 항상 발밑에 위치)")]
    public Vector3 worldOffset = new Vector3(0f, -0.56f, 0f);

    RectTransform rect;
    void Awake()
    {
        rect = GetComponent<RectTransform>();
    }

    // 카메라 이동 후 위치를 잡도록 LateUpdate, 화면 픽셀에 정렬해 글자/바가 흔들리지 않게
    void LateUpdate()
    {
        Vector3 screen = Camera.main.WorldToScreenPoint(GameManager.instance.player.transform.position + worldOffset);
        Vector2 snapped = TKPixelGrid.SnapScreen(screen); // 게임 아트 픽셀 격자 단위로만 이동 (지터 방지)
        rect.position = new Vector3(snapped.x, snapped.y, screen.z);
    }
}
