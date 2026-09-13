using UnityEngine;

namespace CustomPhysics
{
    /// <summary>
    /// A single resolved contact between two shapes: the surface point, the separating
    /// normal (pointing from A toward B), and how deep they currently overlap.
    /// </summary>
    public struct Contact
    {
        public Vector3 point;
        public Vector3 normal;
        public float penetration;
    }

    /// <summary>
    /// Narrow-phase shape-vs-shape tests for the custom rigid-body solver. Sphere-sphere
    /// and sphere-box are exact. Box-box uses the Separating Axis Theorem for detection
    /// (15 candidate axes: each box's 3 face normals, plus the 9 pairwise edge-cross
    /// axes) and a deliberately simplified contact-point approximation rather than full
    /// Sutherland-Hodgman face-clipping: on a face-normal axis it collects whichever of
    /// the incident box's 8 corners lie inside the reference face's bounds (up to 4
    /// points, which is what a resting box-on-box or box-on-floor contact needs for
    /// rotational stability); on an edge-edge axis it falls back to a single point at
    /// the midpoint between the two centers along the normal. This is a known,
    /// intentional simplification - full manifold clipping is what a production engine
    /// (Havok, Bullet, PhysX) does, and reimplementing that exactly was out of scope for
    /// this pass. It should still stack reasonably stably for the common case (flat
    /// faces resting on flat faces); complex multi-box tangles resting at odd angles may
    /// show more residual jitter than a full clipper would produce.
    /// </summary>
    public static class CollisionMath
    {
        public static bool SphereSphere(Vector3 posA, float radiusA, Vector3 posB, float radiusB, out Contact contact)
        {
            contact = default;
            Vector3 delta = posB - posA;
            float dist = delta.magnitude;
            float radiusSum = radiusA + radiusB;
            if (dist >= radiusSum || dist < 1e-6f)
            {
                if (dist < 1e-6f && radiusSum > 0f)
                {
                    // Degenerate: identical centers. Push apart along an arbitrary axis.
                    contact.normal = Vector3.up;
                    contact.penetration = radiusSum;
                    contact.point = posA;
                    return true;
                }
                return false;
            }

            contact.normal = delta / dist;
            contact.penetration = radiusSum - dist;
            contact.point = posA + contact.normal * radiusA;
            return true;
        }

        public static bool SphereBox(Vector3 sphereCenter, float sphereRadius, Vector3 boxCenter, Quaternion boxRot,
            Vector3 boxHalfExtents, out Contact contact)
        {
            contact = default;
            Vector3 localSphere = Quaternion.Inverse(boxRot) * (sphereCenter - boxCenter);
            Vector3 closestLocal = new Vector3(
                Mathf.Clamp(localSphere.x, -boxHalfExtents.x, boxHalfExtents.x),
                Mathf.Clamp(localSphere.y, -boxHalfExtents.y, boxHalfExtents.y),
                Mathf.Clamp(localSphere.z, -boxHalfExtents.z, boxHalfExtents.z));

            Vector3 closestWorld = boxCenter + boxRot * closestLocal;
            Vector3 delta = sphereCenter - closestWorld;
            float dist = delta.magnitude;

            if (dist < sphereRadius)
            {
                // Sphere center is outside the box surface but within radius - normal case.
                contact.normal = dist > 1e-6f ? delta / dist : (boxRot * Vector3.up);
                contact.penetration = sphereRadius - dist;
                contact.point = closestWorld;
                return true;
            }

            if (dist <= 1e-6f)
            {
                // Sphere center is INSIDE the box - push out along the shallowest local axis.
                Vector3 penetrations = boxHalfExtents - new Vector3(Mathf.Abs(localSphere.x), Mathf.Abs(localSphere.y), Mathf.Abs(localSphere.z));
                int axis = 0;
                if (penetrations.y < penetrations.x && penetrations.y < penetrations.z) axis = 1;
                else if (penetrations.z < penetrations.x && penetrations.z < penetrations.y) axis = 2;

                Vector3 localNormal = Vector3.zero;
                localNormal[axis] = localSphere[axis] >= 0f ? 1f : -1f;
                contact.normal = boxRot * localNormal;
                contact.penetration = penetrations[axis] + sphereRadius;
                contact.point = closestWorld;
                return true;
            }

            return false;
        }

        public static bool BoxBox(Vector3 posA, Quaternion rotA, Vector3 halfA, Vector3 posB, Quaternion rotB, Vector3 halfB,
            out Contact[] contacts)
        {
            contacts = null;
            Vector3[] axesA = { rotA * Vector3.right, rotA * Vector3.up, rotA * Vector3.forward };
            Vector3[] axesB = { rotB * Vector3.right, rotB * Vector3.up, rotB * Vector3.forward };

            Vector3 centerDelta = posB - posA;
            float bestOverlap = float.MaxValue;
            Vector3 bestAxis = Vector3.zero;

            // All 15 candidate axes are tested into the same bestOverlap/bestAxis pair
            // first; which category the winning axis belongs to is decided once at the
            // end (see below) rather than tracked incrementally per-loop - tracking it
            // incrementally is a classic SAT bug, since a later loop (e.g. the 9
            // edge-cross axes) can still overwrite bestAxis after an earlier loop's
            // "this is a face axis" flag was already set, leaving that flag stale.
            for (int i = 0; i < 3; i++)
            {
                if (!TestAxis(axesA[i], posA, rotA, halfA, posB, rotB, halfB, centerDelta, ref bestOverlap, ref bestAxis))
                {
                    return false;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                if (!TestAxis(axesB[i], posA, rotA, halfA, posB, rotB, halfB, centerDelta, ref bestOverlap, ref bestAxis))
                {
                    return false;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Vector3 axis = Vector3.Cross(axesA[i], axesB[j]);
                    if (axis.sqrMagnitude < 1e-8f)
                    {
                        continue; // near-parallel edges - already covered by face axes
                    }
                    axis.Normalize();
                    if (!TestAxis(axis, posA, rotA, halfA, posB, rotB, halfB, centerDelta, ref bestOverlap, ref bestAxis))
                    {
                        return false;
                    }
                }
            }

            bool bestIsFaceOfA = IsSameAxis(bestAxis, axesA[0]) || IsSameAxis(bestAxis, axesA[1]) || IsSameAxis(bestAxis, axesA[2]);

            // Make sure the normal points from A toward B.
            if (Vector3.Dot(bestAxis, centerDelta) < 0f)
            {
                bestAxis = -bestAxis;
            }

            if (bestIsFaceOfA)
            {
                contacts = FaceVertexContacts(posA, rotA, halfA, posB, rotB, halfB, bestAxis, bestOverlap);
            }
            else
            {
                // Edge-edge or a face axis of B - single midpoint contact is a reasonable
                // approximation for these rarer configurations (see class doc comment).
                Vector3 mid = posA + centerDelta * 0.5f;
                contacts = new[] { new Contact { point = mid, normal = bestAxis, penetration = bestOverlap } };
            }
            return true;
        }

        private static bool TestAxis(Vector3 axis, Vector3 posA, Quaternion rotA, Vector3 halfA,
            Vector3 posB, Quaternion rotB, Vector3 halfB, Vector3 centerDelta, ref float bestOverlap, ref Vector3 bestAxis)
        {
            float projA = ProjectedRadius(rotA, halfA, axis);
            float projB = ProjectedRadius(rotB, halfB, axis);
            float dist = Mathf.Abs(Vector3.Dot(centerDelta, axis));
            float overlap = projA + projB - dist;
            if (overlap < 0f)
            {
                return false; // separating axis found - boxes do not intersect
            }
            if (overlap < bestOverlap)
            {
                bestOverlap = overlap;
                bestAxis = axis;
            }
            return true;
        }

        private static bool IsSameAxis(Vector3 a, Vector3 b)
        {
            // Same line, either direction - compare via the dot product rather than
            // Vector3's built-in approximate equality, which would miss the -b case.
            return Mathf.Abs(Vector3.Dot(a, b)) > 1f - 1e-4f;
        }

        private static float ProjectedRadius(Quaternion rot, Vector3 halfExtents, Vector3 axis)
        {
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;
            Vector3 fwd = rot * Vector3.forward;
            return halfExtents.x * Mathf.Abs(Vector3.Dot(right, axis))
                 + halfExtents.y * Mathf.Abs(Vector3.Dot(up, axis))
                 + halfExtents.z * Mathf.Abs(Vector3.Dot(fwd, axis));
        }

        private static Contact[] FaceVertexContacts(Vector3 posA, Quaternion rotA, Vector3 halfA,
            Vector3 posB, Quaternion rotB, Vector3 halfB, Vector3 normal, float overlap)
        {
            // Collect corners of B that lie within A's face footprint (the two axes
            // perpendicular to the contact normal), approximating the clipped manifold.
            Vector3 localNormal = Quaternion.Inverse(rotA) * normal;
            int normalAxis = 0;
            float best = Mathf.Abs(localNormal.x);
            if (Mathf.Abs(localNormal.y) > best) { best = Mathf.Abs(localNormal.y); normalAxis = 1; }
            if (Mathf.Abs(localNormal.z) > best) { normalAxis = 2; }
            int axisU = (normalAxis + 1) % 3;
            int axisV = (normalAxis + 2) % 3;

            System.Collections.Generic.List<Contact> result = new System.Collections.Generic.List<Contact>(4);
            for (int cx = -1; cx <= 1; cx += 2)
            {
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    for (int cz = -1; cz <= 1; cz += 2)
                    {
                        Vector3 cornerLocal = new Vector3(cx * halfB.x, cy * halfB.y, cz * halfB.z);
                        Vector3 cornerWorld = posB + rotB * cornerLocal;
                        Vector3 cornerInA = Quaternion.Inverse(rotA) * (cornerWorld - posA);

                        float u = cornerInA[axisU];
                        float v = cornerInA[axisV];
                        float halfU = halfA[axisU];
                        float halfV = halfA[axisV];

                        if (u >= -halfU - 0.02f && u <= halfU + 0.02f && v >= -halfV - 0.02f && v <= halfV + 0.02f)
                        {
                            result.Add(new Contact { point = cornerWorld, normal = normal, penetration = overlap });
                            if (result.Count >= 4)
                            {
                                return result.ToArray();
                            }
                        }
                    }
                }
            }

            if (result.Count == 0)
            {
                // No corner fell inside the face footprint (can happen with very small
                // or heavily rotated boxes) - fall back to the center-to-center point.
                result.Add(new Contact { point = (posA + posB) * 0.5f, normal = normal, penetration = overlap });
            }
            return result.ToArray();
        }
    }
}
