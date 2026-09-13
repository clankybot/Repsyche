using UnityEngine;

/// <summary>
/// Procedural walk-cycle substitute for PlayerBody, which is a single static 48-vertex
/// mesh with no skeleton to animate (confirmed by reimporting the FBX in Blender - no
/// armature, no vertex groups). Bobs the body up/down and leans it forward/back and side
/// to side based on the PlayerController's actual movement state, so moving reads as
/// walking (or running, when sprinting) instead of gliding flatly across the ground.
///
/// Attach to the PlayerBody child object (the visual mesh), not the Player root - it
/// animates its own transform.localPosition/localRotation relative to whatever they were
/// at Awake, so it composes safely with the body's existing offset under Player.
/// </summary>
public class PlayerBodyBob : MonoBehaviour
{
    [Tooltip("The PlayerController to read movement state from. Auto-found on a parent if left empty.")]
    public PlayerController controller;

    [Header("Bob")]
    [Tooltip("Peak vertical bob height at full sprint, in units.")]
    public float bobHeight = 0.12f;
    [Tooltip("How quickly the bob cycles at full speed.")]
    public float bobSpeed = 8f;

    [Header("Lean")]
    [Tooltip("Forward lean at full sprint, in degrees. Leans back slightly when walking backward.")]
    public float forwardLeanDegrees = 8f;
    [Tooltip("Side-to-side sway at full speed, in degrees.")]
    public float swayDegrees = 3f;

    [Header("Landing")]
    [Tooltip("How far the body squashes downward for an instant on landing from a jump.")]
    public float landingSquash = 0.15f;
    [Tooltip("How quickly the landing squash recovers.")]
    public float landingRecoverySpeed = 4f;

    private Vector3 basePosition;
    private Quaternion baseRotation;
    private float bobPhase;
    private bool wasGrounded = true;
    private float landingBounceTimer;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponentInParent<PlayerController>();
        }
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
    }

    private void Update()
    {
        if (controller == null)
        {
            return;
        }

        float speed = controller.NormalizedSpeed;
        bool grounded = controller.IsGrounded;

        if (grounded && !wasGrounded)
        {
            landingBounceTimer = landingSquash;
        }
        wasGrounded = grounded;
        landingBounceTimer = Mathf.MoveTowards(landingBounceTimer, 0f, Time.deltaTime * landingRecoverySpeed);

        if (grounded && speed > 0.01f)
        {
            bobPhase += Time.deltaTime * bobSpeed * (0.5f + speed);
        }
        else
        {
            // Ease toward the nearest "feet together" point rather than freezing mid-stride
            // when the player stops.
            bobPhase = Mathf.Lerp(bobPhase, Mathf.Round(bobPhase / Mathf.PI) * Mathf.PI, Time.deltaTime * 6f);
        }

        float bob = grounded ? Mathf.Abs(Mathf.Sin(bobPhase)) * bobHeight * speed : 0f;
        transform.localPosition = basePosition + new Vector3(0f, bob - landingBounceTimer, 0f);

        float lean = forwardLeanDegrees * controller.MoveDirection * speed;
        float sway = Mathf.Sin(bobPhase * 0.5f) * swayDegrees * speed;
        Quaternion targetRot = baseRotation * Quaternion.Euler(lean, 0f, sway);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, Time.deltaTime * 10f);
    }
}
