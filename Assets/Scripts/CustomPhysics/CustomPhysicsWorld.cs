using System.Collections.Generic;
using UnityEngine;

namespace CustomPhysics
{
    /// <summary>
    /// Drives every CustomRigidbody in the scene through FixedUpdate: semi-implicit
    /// Euler integration, broad+narrow phase collision detection (bodies against each
    /// other, and against the static environment via CustomCollider's helper Unity
    /// Collider), an iterative sequential-impulse velocity solver with restitution and
    /// Coulomb friction, and a separate positional-correction pass so stacked bodies
    /// don't slowly sink into each other or the floor.
    ///
    /// This is a from-scratch solver, not a thin wrapper over PhysX - Unity's Physics.*
    /// calls used here (OverlapSphere, ComputePenetration) query STATIC environment
    /// shapes only; no Unity Rigidbody is ever involved, and no PhysX solver runs on
    /// any CustomRigidbody. Only one CustomPhysicsWorld should exist in a scene - it
    /// self-registers as a singleton on Awake.
    ///
    /// Known limitations (see CollisionMath's doc comment for the box-box specifics):
    /// this targets "stack a reasonable number of crates/props stably," not "match
    /// Havok/Source bit for bit." Broad phase is O(n^2), fine for tens of bodies, not
    /// thousands. Environment contact points/penetration come from Physics.ComputePenetration,
    /// so environment collision quality inherits whatever Unity's own MeshCollider
    /// cooking produces for the terrain.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CustomPhysicsWorld : MonoBehaviour
    {
        public static CustomPhysicsWorld Instance { get; private set; }

        [Header("Solver")]
        [Tooltip("Sequential-impulse iterations per physics step. More = stiffer/more stable stacks, more expensive.")]
        public int velocityIterations = 10;
        [Range(0f, 1f)] public float positionCorrectionPercent = 0.2f;
        [Tooltip("Allowed penetration before position correction kicks in, so resting contacts don't jitter.")]
        public float penetrationSlop = 0.01f;

        [Header("Environment Query")]
        public LayerMask environmentLayerMask = ~0;

        private readonly List<CustomRigidbody> bodies = new List<CustomRigidbody>();
        private readonly List<ContactConstraint> contacts = new List<ContactConstraint>(64);
        private readonly Collider[] envOverlapBuffer = new Collider[32];

        private struct ContactConstraint
        {
            public CustomRigidbody a; // null means "static environment"
            public CustomRigidbody b;
            public Vector3 point;
            public Vector3 normal; // points from a toward b (or from environment surface into b, if a is null)
            public float penetration;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CustomPhysicsWorld] More than one instance in the scene - destroying the extra one.");
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Register(CustomRigidbody body)
        {
            if (!bodies.Contains(body))
            {
                bodies.Add(body);
            }
        }

        public void Unregister(CustomRigidbody body)
        {
            bodies.Remove(body);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            IntegrateVelocities(dt);
            BuildContacts();
            for (int i = 0; i < velocityIterations; i++)
            {
                SolveVelocities();
            }
            IntegratePositions(dt);
            CorrectPositions();
            UpdateSleepStates(dt);
        }

        private void IntegrateVelocities(float dt)
        {
            foreach (var body in bodies)
            {
                if (body.isSleeping)
                {
                    continue;
                }
                if (body.useGravity)
                {
                    body.velocity += Vector3.up * body.gravity * dt;
                }
                // Exponential (frame-rate-independent) damping: velocity *= (1-damping)^dt,
                // rather than the naive v *= (1 - damping*dt), which only approximates this
                // correctly at small dt and drifts at larger step sizes.
                body.velocity *= Mathf.Pow(1f - Mathf.Clamp01(body.linearDamping), dt);
                body.angularVelocity *= Mathf.Pow(1f - Mathf.Clamp01(body.angularDamping), dt);
            }
        }

        private void BuildContacts()
        {
            contacts.Clear();

            // Body-vs-body, O(n^2) broad+narrow phase - fine for the small prop counts
            // this system targets.
            for (int i = 0; i < bodies.Count; i++)
            {
                var a = bodies[i];
                if (a.isSleeping)
                {
                    continue;
                }
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    var b = bodies[j];
                    if (a.isSleeping && b.isSleeping)
                    {
                        continue;
                    }
                    TryBodyBodyContact(a, b);
                }
            }

            // Body-vs-static-environment.
            foreach (var body in bodies)
            {
                if (body.isSleeping)
                {
                    continue;
                }
                TryEnvironmentContact(body);
            }
        }

        private void TryBodyBodyContact(CustomRigidbody a, CustomRigidbody b)
        {
            var colA = a.Collider;
            var colB = b.Collider;
            Vector3 posA = a.transform.TransformPoint(colA.center);
            Vector3 posB = b.transform.TransformPoint(colB.center);

            bool any = false;
            if (colA.shape == CustomColliderShape.Sphere && colB.shape == CustomColliderShape.Sphere)
            {
                if (CollisionMath.SphereSphere(posA, colA.sphereRadius, posB, colB.sphereRadius, out Contact c))
                {
                    contacts.Add(new ContactConstraint { a = a, b = b, point = c.point, normal = c.normal, penetration = c.penetration });
                    any = true;
                }
            }
            else if (colA.shape == CustomColliderShape.Box && colB.shape == CustomColliderShape.Box)
            {
                if (CollisionMath.BoxBox(posA, a.transform.rotation, colA.HalfExtents, posB, b.transform.rotation, colB.HalfExtents, out Contact[] cs))
                {
                    foreach (var c in cs)
                    {
                        contacts.Add(new ContactConstraint { a = a, b = b, point = c.point, normal = c.normal, penetration = c.penetration });
                    }
                    any = true;
                }
            }
            else
            {
                // One sphere, one box - figure out which is which and flip the normal
                // convention back to "a toward b" afterward if needed.
                bool aIsSphere = colA.shape == CustomColliderShape.Sphere;
                CustomRigidbody sphereBody = aIsSphere ? a : b;
                CustomRigidbody boxBody = aIsSphere ? b : a;
                Vector3 spherePos = aIsSphere ? posA : posB;
                Vector3 boxPos = aIsSphere ? posB : posA;
                var sphereCol = sphereBody.Collider;
                var boxCol = boxBody.Collider;

                if (CollisionMath.SphereBox(spherePos, sphereCol.sphereRadius, boxPos, boxBody.transform.rotation, boxCol.HalfExtents, out Contact c))
                {
                    // CollisionMath.SphereBox's normal points away from the box surface,
                    // i.e. from box toward sphere.
                    Vector3 normalAToB = aIsSphere ? -c.normal : c.normal;
                    contacts.Add(new ContactConstraint { a = a, b = b, point = c.point, normal = normalAToB, penetration = c.penetration });
                    any = true;
                }
            }

            if (any)
            {
                // Without this, a sleeping body hit by an awake one gets its velocity
                // changed by the solver below but stays flagged asleep, and sleeping
                // bodies are skipped during position integration - so the impulse would
                // silently do nothing. This is only reached when at least one of the
                // pair is already awake (BuildContacts skips all-sleeping pairs), so it
                // never spuriously disturbs an already-resting stack.
                a.WakeUp();
                b.WakeUp();
            }
        }

        private void TryEnvironmentContact(CustomRigidbody body)
        {
            var col = body.Collider;
            Bounds bounds = col.WorldBounds;
            int count = Physics.OverlapSphereNonAlloc(bounds.center, bounds.extents.magnitude, envOverlapBuffer, environmentLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider envCol = envOverlapBuffer[i];
                if (envCol == col.EnvironmentQueryCollider || envCol.attachedRigidbody != null)
                {
                    continue; // skip self and anything Unity itself is simulating (e.g. the Player's CharacterController capsule)
                }

                // ComputePenetration's positionA/rotationA represent a hypothetical
                // transform for colliderA - its own local center/size offset is applied
                // on top of that internally, exactly as in normal use. So this must be
                // the raw transform position, NOT body.transform.TransformPoint(col.center)
                // (which would double-apply that offset).
                bool hit = Physics.ComputePenetration(
                    col.EnvironmentQueryCollider, body.transform.position, body.transform.rotation,
                    envCol, envCol.transform.position, envCol.transform.rotation,
                    out Vector3 direction, out float distance);

                if (hit && distance > 1e-6f)
                {
                    Vector3 contactPoint = body.transform.TransformPoint(col.center) - direction * (distance * 0.5f);
                    // direction points out of envCol, i.e. the way to push the body to
                    // resolve overlap - already "environment toward body," matching this
                    // constraint's a=null convention below.
                    contacts.Add(new ContactConstraint { a = null, b = body, point = contactPoint, normal = direction, penetration = distance });
                }
            }
        }

        private void SolveVelocities()
        {
            for (int idx = 0; idx < contacts.Count; idx++)
            {
                var c = contacts[idx];
                CustomRigidbody a = c.a;
                CustomRigidbody b = c.b;

                Vector3 rB = c.point - b.transform.position;
                Vector3 velB = b.VelocityAtPoint(c.point);
                Vector3 velA = a != null ? a.VelocityAtPoint(c.point) : Vector3.zero;
                Vector3 relVel = velB - velA;

                float velAlongNormal = Vector3.Dot(relVel, c.normal);
                if (velAlongNormal > 0f)
                {
                    continue; // already separating
                }

                Matrix4x4 invIB = b.WorldInverseInertiaTensor();
                Vector3 rbCrossN = Vector3.Cross(rB, c.normal);
                float angularTermB = Vector3.Dot(invIB.MultiplyVector(rbCrossN), rbCrossN);

                float invMassSum = b.InverseMass;
                float angularTermA = 0f;
                Vector3 rA = Vector3.zero;
                Matrix4x4 invIA = Matrix4x4.zero;
                if (a != null)
                {
                    rA = c.point - a.transform.position;
                    invIA = a.WorldInverseInertiaTensor();
                    Vector3 raCrossN = Vector3.Cross(rA, c.normal);
                    angularTermA = Vector3.Dot(invIA.MultiplyVector(raCrossN), raCrossN);
                    invMassSum += a.InverseMass;
                }

                float denom = invMassSum + angularTermA + angularTermB;
                if (denom <= 1e-8f)
                {
                    continue;
                }

                // Restitution only kicks in above a small approach-speed threshold, so
                // resting/stacked contacts don't perpetually re-bounce each other.
                float restitution = Mathf.Max(a != null ? a.restitution : 0f, b.restitution);
                float e = velAlongNormal < -1f ? restitution : 0f;

                float j = -(1f + e) * velAlongNormal / denom;
                j = Mathf.Max(j, 0f);
                Vector3 impulse = c.normal * j;

                ApplyPair(a, b, rA, rB, impulse, invIA, invIB);

                // Friction, clamped to the Coulomb cone using this contact's normal impulse.
                Vector3 tangentVel = relVel - c.normal * velAlongNormal;
                float tangentSpeed = tangentVel.magnitude;
                if (tangentSpeed > 1e-5f)
                {
                    Vector3 tangent = tangentVel / tangentSpeed;
                    Vector3 raCrossT = a != null ? Vector3.Cross(rA, tangent) : Vector3.zero;
                    Vector3 rbCrossT = Vector3.Cross(rB, tangent);
                    float angularTermAT = a != null ? Vector3.Dot(invIA.MultiplyVector(raCrossT), raCrossT) : 0f;
                    float angularTermBT = Vector3.Dot(invIB.MultiplyVector(rbCrossT), rbCrossT);
                    float denomT = invMassSum + angularTermAT + angularTermBT;
                    if (denomT > 1e-8f)
                    {
                        float combinedFriction = Mathf.Sqrt(Mathf.Max(0f, (a != null ? a.friction : 0f) * b.friction));
                        float jt = -Vector3.Dot(relVel, tangent) / denomT;
                        float maxFriction = combinedFriction * j;
                        jt = Mathf.Clamp(jt, -maxFriction, maxFriction);
                        Vector3 frictionImpulse = tangent * jt;
                        ApplyPair(a, b, rA, rB, frictionImpulse, invIA, invIB);
                    }
                }
            }
        }

        private static void ApplyPair(CustomRigidbody a, CustomRigidbody b, Vector3 rA, Vector3 rB, Vector3 impulse, Matrix4x4 invIA, Matrix4x4 invIB)
        {
            if (a != null)
            {
                a.velocity -= impulse * a.InverseMass;
                a.angularVelocity -= invIA.MultiplyVector(Vector3.Cross(rA, impulse));
            }
            b.velocity += impulse * b.InverseMass;
            b.angularVelocity += invIB.MultiplyVector(Vector3.Cross(rB, impulse));
        }

        private void IntegratePositions(float dt)
        {
            foreach (var body in bodies)
            {
                if (body.isSleeping)
                {
                    continue;
                }
                body.transform.position += body.velocity * dt;
                float angle = body.angularVelocity.magnitude;
                if (angle > 1e-8f)
                {
                    Quaternion delta = Quaternion.AngleAxis(angle * Mathf.Rad2Deg * dt, body.angularVelocity / angle);
                    body.transform.rotation = delta * body.transform.rotation;
                }
            }
        }

        private void CorrectPositions()
        {
            foreach (var c in contacts)
            {
                float correction = Mathf.Max(c.penetration - penetrationSlop, 0f) * positionCorrectionPercent;
                if (correction <= 0f)
                {
                    continue;
                }

                float invMassA = c.a != null ? c.a.InverseMass : 0f;
                float invMassB = c.b.InverseMass;
                float invMassSum = invMassA + invMassB;
                if (invMassSum <= 1e-8f)
                {
                    continue;
                }

                Vector3 move = c.normal * (correction / invMassSum);
                if (c.a != null)
                {
                    c.a.transform.position -= move * invMassA;
                }
                c.b.transform.position += move * invMassB;
            }
        }

        private void UpdateSleepStates(float dt)
        {
            foreach (var body in bodies)
            {
                body.UpdateSleepState(dt);
            }
        }
    }
}
