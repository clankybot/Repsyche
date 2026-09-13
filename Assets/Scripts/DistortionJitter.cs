using UnityEngine;

/// <summary>
/// Rapidly randomizes the target SkinnedMeshRenderer's blend shape weights for a jittery,
/// twitching distortion effect - driving the "Distortion_XX" shape keys baked into the
/// Cognitive Distortion Cluster model at a fixed interval rather than a smooth animation
/// curve, so it reads as glitchy rather than animated.
/// </summary>
public class DistortionJitter : MonoBehaviour
{
    [Tooltip("Defaults to the first SkinnedMeshRenderer found on this object or its children.")]
    public SkinnedMeshRenderer targetRenderer;

    [Tooltip("Seconds between jitter updates. Small values read as a rapid, glitchy twitch.")]
    public float jitterInterval = 0.05f;

    [Range(0f, 100f)]
    [Tooltip("Upper bound for a randomized blend shape weight when it's chosen to activate.")]
    public float maxWeight = 100f;

    [Range(0f, 1f)]
    [Tooltip("Chance per shape key, per update, that it activates at all (vs. resting at 0).")]
    public float activationChance = 0.5f;

    private float timer;

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        }
    }

    private void Update()
    {
        if (targetRenderer == null || targetRenderer.sharedMesh == null)
        {
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f)
        {
            return;
        }
        timer = jitterInterval;

        int count = targetRenderer.sharedMesh.blendShapeCount;
        for (int i = 0; i < count; i++)
        {
            float weight = Random.value < activationChance ? Random.Range(0f, maxWeight) : 0f;
            targetRenderer.SetBlendShapeWeight(i, weight);
        }
    }
}