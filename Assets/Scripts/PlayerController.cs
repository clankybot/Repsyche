using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PSX-era "tank controls" player controller: A/D (or Left/Right) turn the player in place,
/// W/S (or Up/Down) move forward/backward relative to the direction the player is currently
/// facing. There is no strafing and no independent camera look - turning the player IS how
/// you look around, exactly like classic Resident Evil / early 3D platformer controls.
///
/// Gravity is a constant downward acceleration (not a physics-engine simulation) - this is
/// the standard approach for a CharacterController and is what most "Source Engine feel"
/// requests actually mean in practice: a snappy, constant fall acceleration plus a real jump
/// impulse, rather than literally reimplementing Source's movement code.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Forward movement speed in units/second.")]
    public float moveSpeed = 4f;

    [Tooltip("Backward movement is slower than forward, matching classic tank-control feel.")]
    [Range(0f, 1f)]
    public float backwardSpeedMultiplier = 0.5f;

    [Tooltip("Turn speed in degrees/second while holding the turn keys.")]
    public float turnSpeed = 140f;

    [Header("Sprint")]
    [Tooltip("Forward speed is multiplied by this while holding Shift. Does not apply to backward movement.")]
    public float sprintMultiplier = 1.8f;

    [Header("Gravity & Jump")]
    public float gravity = -20f;
    [Tooltip("Upward velocity applied on jump, in units/second.")]
    public float jumpSpeed = 8f;
    [Tooltip("Small downward speed applied while grounded so the controller stays snapped to slopes.")]
    public float groundedStickSpeed = -2f;

    /// <summary>Current horizontal speed as a fraction of the fastest possible move speed
    /// (sprinting forward). 0 = idle, 1 = full sprint. Read by PlayerBodyBob to drive the
    /// procedural walk animation without duplicating input-reading logic.</summary>
    public float NormalizedSpeed { get; private set; }

    /// <summary>-1 (backward) .. 0 (idle) .. 1 (forward), before speed/sprint scaling. Lets
    /// the animator distinguish walking backward from forward at the same overall speed.</summary>
    public float MoveDirection { get; private set; }

    public bool IsGrounded => controller != null && controller.isGrounded;

    private CharacterController controller;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        // Turning - rotates the player (and anything parented to it, e.g. the camera) in place.
        float turn = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
        {
            turn -= 1f;
        }
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
        {
            turn += 1f;
        }
        transform.Rotate(Vector3.up, turn * turnSpeed * Time.deltaTime, Space.World);

        // Forward/back - always relative to the player's current facing, never strafing.
        float move = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
        {
            move += 1f;
        }
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
        {
            move -= 1f;
        }

        bool sprinting = move > 0f && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        float speed = move >= 0f ? moveSpeed : moveSpeed * backwardSpeedMultiplier;
        if (sprinting)
        {
            speed *= sprintMultiplier;
        }
        Vector3 horizontalMotion = transform.forward * (move * speed);

        MoveDirection = move;
        float fastestPossibleSpeed = moveSpeed * sprintMultiplier;
        NormalizedSpeed = fastestPossibleSpeed > 0f ? Mathf.Clamp01(Mathf.Abs(move * speed) / fastestPossibleSpeed) : 0f;

        // Gravity, so the CharacterController doesn't need a Rigidbody to stay grounded.
        bool jumpPressed = kb.spaceKey.wasPressedThisFrame;
        if (controller.isGrounded)
        {
            if (jumpPressed)
            {
                verticalVelocity = jumpSpeed;
            }
            else if (verticalVelocity < 0f)
            {
                verticalVelocity = groundedStickSpeed;
            }
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = horizontalMotion;
        motion.y = verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }
}