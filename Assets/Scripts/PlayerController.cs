using UnityEngine;
using UnityEngine.InputSystem;

// controla o boneco com o teclado (para testar sem o fato de mocap)
// as animações estão no Animator: cada tecla dá um trigger e o controller faz a transição
// o código só desliza o corpo até ao centro da lane nova, como no Subway
// com liveMocap ligado o teclado e o Animator deixam de mexer no boneco
public class PlayerController : MonoBehaviour
{
    // nomes dos triggers do Animator (iguais aos nomes das animações)
    const string Idle = "idle";
    const string HopLeft = "hop_left";
    const string HopRight = "hop_right";
    const string HopMiddle = "hop_middle";
    const string Jump = "jump";
    const string JumpLeft = "jump_left";
    const string JumpRight = "jump_right";
    const string SplitLeft = "split_left";
    const string SplitRight = "split_right";
    public const string FeetOnGroundParameter = "FeetOnGround";

    // tempos medidos nas gravações: quando o corpo começa e acaba de ir para o lado
    static readonly (string state, float delay, float duration)[] LaneMoves =
    {
        (HopLeft, 0.10f, 0.55f),
        (HopRight, 0.10f, 0.60f),
        (JumpLeft, 0.12f, 0.55f),
        (JumpRight, 0.08f, 0.55f),
    };

    [Tooltip("turn on when the character is driven by live motion capture (Vicon)")]
    public bool liveMocap = false;

    // ligado pelo menu Piano, mas dá para arrastar à mão
    [Header("References")]
    public PianoGame game;
    public Transform track;
    public Animator animator;
    public PlayerFeet feet;

    int lane = 1;
    float x, xStart, xTarget;
    float moveStart = -99f, moveDelay, moveDuration;
    float localHeight, localZ;
    int feetOnGroundId;
    bool hasFeetOnGround;

    void Start()
    {
        if (game == null) game = FindFirstObjectByType<PianoGame>();
        if (track == null && game != null) track = game.transform;
        if (animator == null) animator = GetComponent<Animator>();
        if (feet == null) feet = GetComponent<PlayerFeet>();

        feetOnGroundId = Animator.StringToHash(FeetOnGroundParameter);
        hasFeetOnGround = HasParameter(feetOnGroundId);

        if (liveMocap)
        {
            if (animator != null) animator.enabled = false;
            return;
        }

        if (track == null || animator == null)
        {
            Debug.LogError("[Piano] PlayerController needs the track and the Animator. Run Tools > Piano > Do everything.", this);
            enabled = false;
            return;
        }

        Vector3 local = track.InverseTransformPoint(transform.position);
        localHeight = local.y;
        localZ = local.z;
        ResetPlayer();
    }

    void Update()
    {
        if (liveMocap) return;

        if (hasFeetOnGround && feet != null && animator.isActiveAndEnabled)
            animator.SetInteger(feetOnGroundId, feet.GroundedCount);

        Keyboard k = Keyboard.current;
        if (k != null && (game == null || !game.GameOver))
        {
            if (k.aKey.wasPressedThisFrame || k.digit1Key.wasPressedThisFrame) GoTo(0);
            if (k.sKey.wasPressedThisFrame || k.digit2Key.wasPressedThisFrame) GoTo(1);
            if (k.dKey.wasPressedThisFrame || k.digit3Key.wasPressedThisFrame) GoTo(2);
            if (k.leftArrowKey.wasPressedThisFrame && lane > 0) GoTo(lane - 1);
            if (k.rightArrowKey.wasPressedThisFrame && lane < 2) GoTo(lane + 1);
            if (k.qKey.wasPressedThisFrame) Split(-1);
            if (k.eKey.wasPressedThisFrame) Split(1);
            if (k.wKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) Play(Jump);
        }

        float p = moveDuration <= 0f ? 1f : Mathf.Clamp01((Time.time - moveStart - moveDelay) / moveDuration);
        p = p * p * (3f - 2f * p);
        x = Mathf.Lerp(xStart, xTarget, p);
        transform.position = track.TransformPoint(new Vector3(x, localHeight, localZ));
    }

    // 1 lane = hop, 2 lanes = salto, a mesma = hop no sítio
    void GoTo(int newLane)
    {
        newLane = Mathf.Clamp(newLane, 0, 2);
        int diff = newLane - lane;
        string state;
        if (diff == 0) state = HopMiddle;
        else if (Mathf.Abs(diff) == 1) state = diff < 0 ? HopLeft : HopRight;
        else state = diff < 0 ? JumpLeft : JumpRight;

        lane = newLane;
        xStart = x;
        xTarget = (lane - 1) * PianoGame.LaneWidth;

        moveDelay = 0.1f;
        moveDuration = 0.4f;
        foreach (var m in LaneMoves)
            if (m.state == state) { moveDelay = m.delay; moveDuration = m.duration; }
        moveStart = Time.time;

        Play(state);
    }

    // estica uma perna para a lane do lado e volta; o corpo fica
    void Split(int side)
    {
        int other = lane + side;
        if (other < 0 || other > 2) return;
        Play(side < 0 ? SplitLeft : SplitRight);
    }

    void Play(string trigger)
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        int id = Animator.StringToHash(trigger);
        if (!HasParameter(id))
        {
            Debug.LogWarning($"[Piano] the Animator has no trigger '{trigger}'. Run Tools > Piano > Do everything.", this);
            return;
        }
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.nameHash != id) animator.ResetTrigger(p.nameHash);
        animator.SetTrigger(id);
    }

    bool HasParameter(int id)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.nameHash == id) return true;
        return false;
    }

    public void ResetPlayer()
    {
        if (liveMocap) return;
        lane = 1;
        x = xStart = xTarget = 0f;
        moveStart = -99f;
        moveDuration = 0f;
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;

        animator.speed = 1f;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger) animator.ResetTrigger(p.nameHash);
        if (animator.HasState(0, Animator.StringToHash(Idle))) animator.Play(Idle, 0, 0f);
    }
}
