using UnityEngine;

namespace CustomPhysics
{
    public enum CustomColliderShape
    {
        Sphere,
        Box,
    }

    /// <summary>
    /// Shape data for a CustomRigidbody. Also maintains a matching, Rigidbody-less
    /// Unity Collider on the same object purely so CustomPhysicsWorld can query the
    /// static environment (terrain, buildings) via Physics.ComputePenetration /
    /// OverlapSphere - reimplementing collision detection against arbitrary imported
    /// meshes (a 17000-vertex terrain, in this project's case) from scratch is a wholly
    /// different, much larger problem than rigid-body dynamics and wasn't what was
    /// actually being asked for. Everything about how bodies integrate, collide with
    /// EACH OTHER, and resolve (impulses, friction, stacking) is custom; only the
    /// environment shape-overlap query reuses Unity's proven geometry primitives. This
    /// helper collider has no Rigidbody attached, so PhysX never simulates it - Unity
    /// treats it as static geometry, which is exactly what we want to query against.
    /// </summary>
    [DisallowMultipleComponent]
    public class CustomCollider : MonoBehaviour
    {
        public CustomColliderShape shape = CustomColliderShape.Box;
        public Vector3 center = Vector3.zero;
        [Tooltip("Full size (not half-extents) for a Box shape, matching Unity's own BoxCollider.size convention.")]
        public Vector3 boxSize = Vector3.one;
        public float sphereRadius = 0.5f;

        private Collider environmentQueryCollider;

        public Vector3 HalfExtents => boxSize * 0.5f;

        public Bounds WorldBounds
        {
            get
            {
                Vector3 worldCenter = transform.TransformPoint(center);
                float radius = shape == CustomColliderShape.Sphere ? sphereRadius : boxSize.magnitude * 0.5f;
                return new Bounds(worldCenter, Vector3.one * radius * 2f);
            }
        }

        private void Awake()
        {
            SyncEnvironmentQueryCollider();
        }

        private void OnValidate()
        {
            SyncEnvironmentQueryCollider();
        }

        private void SyncEnvironmentQueryCollider()
        {
            // Remove a stale collider of the wrong type if the shape was changed.
            var box = GetComponent<BoxCollider>();
            var sphere = GetComponent<SphereCollider>();
            if (shape == CustomColliderShape.Box && sphere != null)
            {
                DestroyHelper(sphere);
                sphere = null;
            }
            if (shape == CustomColliderShape.Sphere && box != null)
            {
                DestroyHelper(box);
                box = null;
            }

            if (shape == CustomColliderShape.Box)
            {
                if (box == null)
                {
                    box = gameObject.AddComponent<BoxCollider>();
                }
                box.center = center;
                box.size = boxSize;
                box.isTrigger = false;
                environmentQueryCollider = box;
            }
            else
            {
                if (sphere == null)
                {
                    sphere = gameObject.AddComponent<SphereCollider>();
                }
                sphere.center = center;
                sphere.radius = sphereRadius;
                sphere.isTrigger = false;
                environmentQueryCollider = sphere;
            }
        }

        private static void DestroyHelper(Collider c)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(c);
            }
            else
            {
                Object.DestroyImmediate(c);
            }
        }

        public Collider EnvironmentQueryCollider
        {
            get
            {
                if (environmentQueryCollider == null)
                {
                    SyncEnvironmentQueryCollider();
                }
                return environmentQueryCollider;
            }
        }
    }
}
