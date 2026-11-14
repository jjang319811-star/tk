using UnityEngine;

/// <summary>
/// 단일 프레임 스프라이트용 임시(절차적) 애니메이션 — '딱딱 끊기는' 픽셀아트 스타일.
///  - 회전/스케일 없음 (픽셀 비율 유지), 모든 이동은 1픽셀(1/64 유닛) 단위 스텝
///  - 걷기: 일정 박자로 1px 들썩, 대기: 느린 박자로 1px 호흡
///  - 공격: 손 쪽으로 2px 툭 → 1px → 원위치 (짧고 단단하게)
///  - 예비동작/돌진/회전/포효/기절/사망: 스텝 이동·좌우반전·점멸로 표현
///  - 피격 플래시는 서서히 사라지지 않고 즉시 켜졌다 꺼짐
/// 반드시 비주얼 전용 자식 오브젝트에 붙일 것 (루트에 붙이면 콜라이더·무기까지 같이 움직임).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class ProceduralSpriteAnim : MonoBehaviour
{
    public enum Action { None, Windup, Lunge, Spin, Roar, Stunned, Dead, Attack }

    const float Px = 1f / 64f;

    [Header("Facing")]
    [Tooltip("지정하면 이 SpriteRenderer 의 flipX 를 그대로 따라감 (플레이어)")]
    public SpriteRenderer source;
    [Tooltip("원본 그림이 오른쪽을 보고 있으면 true")]
    public bool spriteFacesRight = true;
    [Tooltip("source 가 없을 때 바라볼 방향")]
    public bool faceRight = true;

    [Header("Step motion (pixels / seconds)")]
    public int walkStepPixels = 1;
    public float walkStepInterval = 0.14f;
    public float idleStepInterval = 0.55f;
    public Rigidbody2D body;

    [Header("Hand (발사 지점, 원본 그림 기준 피벗에서의 픽셀 오프셋)")]
    public Vector2 handPixels = new Vector2(20f, 14f);
    [Tooltip("손 위치를 따라다니는 앵커 (투사체·이펙트 발사 지점)")]
    public Transform handAnchor;

    static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    SpriteRenderer sr;
    MaterialPropertyBlock mpb;
    Vector3 baseLocalPos;
    Vector2 lastPos;
    float time, moveTimer, flashTimer, actionTime, actionDuration;
    bool moving;
    Color flashColor = Color.white;
    Action action;

    public SpriteRenderer Renderer => sr;
    public Action Current => action;
    public bool LookingRight => spriteFacesRight != sr.flipX;

    /// <summary>현재 손끝의 월드 좌표</summary>
    public Vector3 HandWorldPosition
    {
        get
        {
            float fwd = LookingRight ? 1f : -1f;
            Vector3 local = baseLocalPos + new Vector3(handPixels.x * fwd * Px, handPixels.y * Px, 0f);
            return transform.parent ? transform.parent.TransformPoint(local) : local;
        }
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
        baseLocalPos = transform.localPosition;
        if (body == null) body = GetComponentInParent<Rigidbody2D>();
        lastPos = Pos();
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.identity;
    }

    Vector2 Pos() => body ? body.position : (Vector2)transform.position;

    public void Play(Action a, float duration)
    {
        if (action == Action.Dead) return;
        action = a;
        actionTime = 0f;
        actionDuration = Mathf.Max(0.01f, duration);
    }

    public void Stop()
    {
        if (action != Action.Dead) action = Action.None;
    }

    /// <summary>즉시 켜졌다가 duration 뒤 꺼지는 단색 플래시</summary>
    public void Flash(Color color, float duration = 0.08f)
    {
        flashColor = color;
        flashTimer = Mathf.Max(flashTimer, Mathf.Clamp(duration, 0.04f, 0.3f));
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        Vector2 pos = Pos();
        if (dt > 0f)
        {
            bool nowMoving = (pos - lastPos).magnitude / dt > 0.05f;
            moveTimer = nowMoving ? 0.1f : moveTimer - dt; // 짧은 멈춤엔 걷기 유지
            moving = moveTimer > 0f;
        }
        lastPos = pos;
        time += dt;

        sr.flipX = source ? source.flipX : faceRight != spriteFacesRight;
        float fwd = LookingRight ? 1f : -1f;

        int ox = 0, oy = 0;
        bool visible = true;

        // 박자에 맞춘 1px 스텝 (부드러운 사인 대신 0/1 교대)
        if (moving) oy += (Mathf.FloorToInt(time / walkStepInterval) & 1) * walkStepPixels;
        else oy += Mathf.FloorToInt(time / idleStepInterval) & 1;

        if (action != Action.None)
        {
            actionTime += dt;
            float k = Mathf.Clamp01(actionTime / actionDuration);
            int beat = Mathf.FloorToInt(actionTime / 0.05f);
            switch (action)
            {
                case Action.Attack: // 툭 내지르기: 2px → 1px → 0
                    ox += Mathf.RoundToInt(fwd * (k < 0.4f ? 2 : k < 0.75f ? 1 : 0));
                    break;
                case Action.Windup: // 뒤로 1px 당기고 2박자 떨림
                    ox += Mathf.RoundToInt(-fwd * (1 + (beat & 1)));
                    break;
                case Action.Lunge:
                    ox += Mathf.RoundToInt(fwd * (k < 0.7f ? 2 : 1));
                    break;
                case Action.Spin: // 회전 대신 빠른 좌우 반전
                    sr.flipX = ((Mathf.FloorToInt(k * 6f) & 1) == 1) ? !sr.flipX : sr.flipX;
                    break;
                case Action.Roar:
                    ox += (beat & 1) == 0 ? 1 : -1;
                    break;
                case Action.Stunned:
                    ox += (Mathf.FloorToInt(actionTime / 0.16f) & 1) == 0 ? 1 : -1;
                    if ((Mathf.FloorToInt(actionTime / 0.4f) & 3) == 0) Flash(new Color(1f, 0.95f, 0.4f), 0.05f);
                    break;
                case Action.Dead: // 흰 섬광 → 2px 주저앉음 → 점멸하며 사라짐
                    oy -= k < 0.2f ? 0 : 2;
                    if (k < 0.12f) Flash(Color.white, 0.05f);
                    visible = k < 0.4f || ((Mathf.FloorToInt(actionTime / 0.08f) & 1) == 0 && k < 0.95f);
                    break;
            }
            if (k >= 1f && action != Action.Dead && action != Action.Stunned)
                action = Action.None;
        }

        flashTimer -= dt;
        float flash = flashTimer > 0f ? 1f : 0f;

        transform.localPosition = baseLocalPos + new Vector3(ox * Px, oy * Px, 0f);
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        sr.enabled = visible;

        sr.GetPropertyBlock(mpb);
        mpb.SetFloat(FlashAmountId, flash * 0.85f);
        mpb.SetColor(FlashColorId, flashColor);
        sr.SetPropertyBlock(mpb);

        if (handAnchor) handAnchor.position = HandWorldPosition;
    }
}
