using UnityEngine;

/// <summary>
/// Real per-limb walk-cycle animation for the procedurally generated PlayerCharacter
/// (Tools/Blender/generate_player_character.py) - rotates LegL/LegR and ArmL/ArmR around
/// their own local pivots (hip/shoulder joints, baked into each limb's object origin in
/// Blender) in a classic opposite-swing walk cycle, driven by PlayerController's actual
/// movement speed. This is possible here specifically because the new character is built
/// from separate limb objects rather than one static mesh (PlayerBody.fbx had neither
/// bones nor separate parts, so PlayerBodyBob's whole-body bob was the only option then).
/// </summary>
public class HumanoidWalkAnimator : MonoBehaviour
{
    public PlayerController controller;
    public Transform legL;
    public Transform legR;
    public Transform armL;
    public Transform armR;

    [Header("Swing")]
    public float maxLegSwingDegrees = 35f;
    public float maxArmSwingDegrees = 25f;
    public float cycleSpeed = 7f;

    [Header("Landing")]
    [Tooltip("Brief knee-bend, in degrees, applied to both legs on landing from a jump.")]
    public float landingCrouch = 12f;
    public float landingRecoverySpeed = 5f;

    private float phase;
    private bool wasGrounded = true;
    private float landingTimer;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponentInParent<PlayerController>();
        }
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
            landingTimer = landingCrouch;
        }
        wasGrounded = grounded;
        landingTimer = Mathf.MoveTowards(landingTimer, 0f, Time.deltaTime * landingRecoverySpeed * 10f);

        if (grounded && speed > 0.01f)
        {
            phase += Time.deltaTime * cycleSpeed * (0.4f + speed);
        }
        else
        {
            // Ease to the nearest "feet together" point rather than freezing mid-stride.
            phase = Mathf.Lerp(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, Time.deltaTime * 6f);
        }

        float swing = Mathf.Sin(phase) * speed;
        float legAngle = swing * maxLegSwingDegrees;
        float armAngle = swing * maxArmSwingDegrees;
        float crouch = -landingTimer;

        SetLocalX(legL, legAngle + crouch);
        SetLocalX(legR, -legAngle + crouch);
        SetLocalX(armL, -armAngle);
        SetLocalX(armR, armAngle);
    }

    private static void SetLocalX(Transform t, float degrees)
    {
        if (t == null)
        {
            return;
        }
        t.localRotation = Quaternion.Euler(degrees, 0f, 0f);
    }
}
