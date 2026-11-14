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
        rect.position = new Vector3(Mathf.Round(screen.x), Mathf.Round(screen.y), screen.z);
    }
}
