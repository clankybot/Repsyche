using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PSX-era "tank controls" player controller: A/D (or Left/Right) turn the player in place,
/// W/S (or Up/Down) move forward/backward relative to the direction the player is currently
/// facing. There is no strafing and no independent camera look - turning the player IS how
/// you look around, exactly like classic Resident Evil / early 3D platformer controls.
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

    [Header("Gravity")]
    public float gravity = -20f;
    [Tooltip("Small downward speed applied while grounded so the controller stays snapped to slopes.")]
    public float groundedStickSpeed = -2f;

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

        float speed = move >= 0f ? moveSpeed : moveSpeed * backwardSpeedMultiplier;
        Vector3 horizontalMotion = transform.forward * (move * speed);

        // Gravity, so the CharacterController doesn't need a Rigidbody to stay grounded.
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = groundedStickSpeed;
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = horizontalMotion;
        motion.y = verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }
}