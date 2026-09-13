using UnityEngine;

namespace CustomPhysics
{
    /// <summary>
    /// Lets the Player's CharacterController (Unity's own, unchanged) shove
    /// CustomRigidbody props around. CharacterController.Move sweeps against every
    /// Collider in the scene regardless of whether it belongs to a custom-physics
    /// object, but by itself it never applies a force to what it hits - this listens
    /// for that hit and feeds an impulse into the custom solver instead.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerPushesCustomProps : MonoBehaviour
    {
        [Tooltip("How much of the player's own speed becomes push force on light props.")]
        public float pushStrength = 2.5f;

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var prop = hit.collider.GetComponentInParent<CustomRigidbody>();
            if (prop == null)
            {
                return;
            }

            Vector3 horizontalVelocity = hit.controller.velocity;
            horizontalVelocity.y = 0f;
            if (horizontalVelocity.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 impulse = horizontalVelocity * (pushStrength / Mathf.Max(prop.mass, 0.1f));
            prop.ApplyImpulse(impulse, hit.point);
        }
    }
}
