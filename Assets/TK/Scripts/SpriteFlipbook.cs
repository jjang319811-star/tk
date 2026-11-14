using UnityEngine;

/// <summary>
/// 스프라이트 배열을 일정 FPS 로 순환 재생 (불꽃, 흔들리는 식물 등 장식용).
/// Animator 없이 가볍게 동작하며, 시작 프레임을 랜덤으로 해 같은 오브젝트끼리 동기화되어 보이지 않게 함.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFlipbook : MonoBehaviour
{
    public Sprite[] frames;
    [Min(0.1f)] public float fps = 10f;
    public bool randomStart = true;

    SpriteRenderer sr;
    float time;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    // 풀 재사용 시 처음 프레임부터 (randomStart 면 임의 위치)
    void OnEnable()
    {
        time = randomStart && frames != null && frames.Length > 0 ? Random.Range(0f, frames.Length / fps) : 0f;
        if (frames != null && frames.Length > 0 && sr) sr.sprite = frames[0];
    }

    void Update()
    {
        if (frames == null || frames.Length == 0)
            return;
        // 게임 일시정지(timeScale 0) 중에도 배경은 살아 있도록 unscaled 시간 사용
        time += Time.unscaledDeltaTime;
        int index = (int)(time * fps) % frames.Length;
        if (sr.sprite != frames[index])
            sr.sprite = frames[index];
    }
}
