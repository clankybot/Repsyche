using UnityEngine;

/// <summary>
/// Keeps a fixed-offset third-person camera from clipping into terrain. The camera's
/// authored local position/rotation (its offset behind-and-above the player) works fine
/// over a flat plane, but this game's dune terrain has +/-25m of relief - a fixed offset
/// can end up embedded inside a nearby slope, which (combined with the terrain shader
/// now rendering both mesh faces) shows the inside of the ground instead of the player.
///
/// Each frame, raycasts from an anchor point near the player out toward the camera's
/// authored offset and pulls the camera in along that same line if something solid is
/// in the way, ignoring the player's own colliders.
/// </summary>
public class ThirdPersonCameraCollision : MonoBehaviour
{
    [Tooltip("Where the camera looks from/pivots around, in the parent's local space - roughly chest height, not ground level.")]
    public Vector3 anchorLocalOffset = new Vector3(0f, 1.2f, 0f);
    [Tooltip("How far to pull the camera back from a hit surface so it doesn't sit exactly on it.")]
    public float collisionBuffer = 0.3f;
    [Tooltip("Layers the camera avoids clipping into.")]
    public LayerMask collisionMask = ~0;

    private Transform anchor;
    private Vector3 desiredLocalPosition;
    private Quaternion desiredLocalRotation;

    private void Awake()
    {
        anchor = transform.parent;
        desiredLocalPosition = transform.localPosition;
        desiredLocalRotation = transform.localRotation;
    }

    private void LateUpdate()
    {
        if (anchor == null)
        {
            return;
        }

        Vector3 anchorWorld = anchor.TransformPoint(anchorLocalOffset);
        Vector3 desiredWorld = anchor.TransformPoint(desiredLocalPosition);
        Vector3 offset = desiredWorld - anchorWorld;
        float desiredDistance = offset.magnitude;
        Vector3 direction = desiredDistance > 0.0001f ? offset / desiredDistance : Vector3.back;

        float finalDistance = desiredDistance;
        var hits = Physics.RaycastAll(anchorWorld, direction, desiredDistance, collisionMask, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.collider.transform == anchor || hit.collider.transform.IsChildOf(anchor))
            {
                continue; // never treat the player's own colliders as camera-blocking geometry
            }
            finalDistance = Mathf.Min(finalDistance, Mathf.Max(hit.distance - collisionBuffer, 0.05f));
        }

        transform.position = anchorWorld + direction * finalDistance;
        transform.rotation = anchor.rotation * desiredLocalRotation;
    }
}
