using UnityEngine;

namespace CustomPhysics
{
    /// <summary>
    /// A dynamic body simulated by CustomPhysicsWorld instead of Unity's built-in
    /// Rigidbody/PhysX. Do not also add a Unity Rigidbody to this object - CustomCollider
    /// maintains a plain (Rigidbody-less) Collider for environment shape queries only,
    /// and a real Rigidbody would make PhysX simulate it as well, fighting this system.
    ///
    /// Mass properties (inverse mass, inverse inertia tensor) are exact closed-form
    /// values for the two supported primitive shapes (sphere, box) - both have their
    /// principal axes of inertia aligned with their local axes, so a diagonal local
    /// inertia tensor is not an approximation for them, just the correct value.
    /// </summary>
    [RequireComponent(typeof(CustomCollider))]
    public class CustomRigidbody : MonoBehaviour
    {
        [Header("Mass & Material")]
        public float mass = 1f;
        [Range(0f, 1f)] public float restitution = 0.15f;
        [Range(0f, 2f)] public float friction = 0.6f;

        [Header("Damping")]
        [Tooltip("Fraction of linear velocity removed per second, independent of collisions - approximates air drag.")]
        [Range(0f, 1f)] public float linearDamping = 0.03f;
        [Range(0f, 1f)] public float angularDamping = 0.08f;

        [Header("Gravity")]
        public bool useGravity = true;
        public float gravity = -20f; // matches PlayerController's gravity for a consistent world feel

        [Header("Sleeping")]
        [Tooltip("Below this combined linear+angular speed, a resting-time timer starts.")]
        public float sleepSpeedThreshold = 0.05f;
        public float sleepDelay = 1.0f;

        [HideInInspector] public Vector3 velocity;
        [HideInInspector] public Vector3 angularVelocity;
        [HideInInspector] public bool isSleeping;

        public CustomCollider Collider { get; private set; }
        public float InverseMass { get; private set; }
        public Vector3 InverseInertiaLocalDiagonal { get; private set; }

        private float sleepTimer;

        private void Awake()
        {
            Collider = GetComponent<CustomCollider>();
            RecomputeMassProperties();
        }

        private void OnEnable()
        {
            if (CustomPhysicsWorld.Instance == null)
            {
                Debug.LogWarning("[CustomRigidbody] No CustomPhysicsWorld found in the scene - " +
                                  gameObject.name + " will not be simulated until one exists. " +
                                  "Run Repsyche > Setup Custom Physics Demo, or add one manually.");
                return;
            }
            CustomPhysicsWorld.Instance.Register(this);
        }

        private void OnDisable()
        {
            CustomPhysicsWorld.Instance?.Unregister(this);
        }

        private void OnValidate()
        {
            if (Collider == null)
            {
                Collider = GetComponent<CustomCollider>();
            }
            if (Collider != null)
            {
                RecomputeMassProperties();
            }
        }

        public void RecomputeMassProperties()
        {
            mass = Mathf.Max(mass, 0.0001f);
            InverseMass = 1f / mass;

            if (Collider.shape == CustomColliderShape.Sphere)
            {
                float r = Collider.sphereRadius;
                float i = 0.4f * mass * r * r; // solid sphere: (2/5) m r^2
                float inv = i > 1e-8f ? 1f / i : 0f;
                InverseInertiaLocalDiagonal = new Vector3(inv, inv, inv);
            }
            else
            {
                Vector3 s = Collider.boxSize; // full size, not half-extents
                float ix = (1f / 12f) * mass * (s.y * s.y + s.z * s.z);
                float iy = (1f / 12f) * mass * (s.x * s.x + s.z * s.z);
                float iz = (1f / 12f) * mass * (s.x * s.x + s.y * s.y);
                InverseInertiaLocalDiagonal = new Vector3(
                    ix > 1e-8f ? 1f / ix : 0f,
                    iy > 1e-8f ? 1f / iy : 0f,
                    iz > 1e-8f ? 1f / iz : 0f);
            }
        }

        /// <summary>World-space inverse inertia tensor: R * I^-1_local * R^T.</summary>
        public Matrix4x4 WorldInverseInertiaTensor()
        {
            Matrix4x4 r = Matrix4x4.Rotate(transform.rotation);
            Matrix4x4 invLocal = Matrix4x4.identity;
            invLocal.m00 = InverseInertiaLocalDiagonal.x;
            invLocal.m11 = InverseInertiaLocalDiagonal.y;
            invLocal.m22 = InverseInertiaLocalDiagonal.z;
            return r * invLocal * r.transpose;
        }

        public Vector3 VelocityAtPoint(Vector3 worldPoint)
        {
            Vector3 r = worldPoint - transform.position;
            return velocity + Vector3.Cross(angularVelocity, r);
        }

        public void ApplyImpulse(Vector3 impulse, Vector3 worldPoint)
        {
            if (isSleeping)
            {
                WakeUp();
            }
            velocity += impulse * InverseMass;
            Vector3 r = worldPoint - transform.position;
            angularVelocity += WorldInverseInertiaTensor().MultiplyVector(Vector3.Cross(r, impulse));
        }

        public void WakeUp()
        {
            isSleeping = false;
            sleepTimer = 0f;
        }

        /// <summary>Advances the sleep timer; call once per physics step after integration
        /// and solving. Returns true if the body just fell asleep this step.</summary>
        public bool UpdateSleepState(float dt)
        {
            float speed = velocity.magnitude + angularVelocity.magnitude;
            if (speed < sleepSpeedThreshold)
            {
                sleepTimer += dt;
                if (sleepTimer >= sleepDelay && !isSleeping)
                {
                    isSleeping = true;
                    velocity = Vector3.zero;
                    angularVelocity = Vector3.zero;
                    return true;
                }
            }
            else
            {
                sleepTimer = 0f;
            }
            return false;
        }
    }
}
