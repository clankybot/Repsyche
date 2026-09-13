using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chases the player via NavMeshAgent and deals damage through a trigger collider on
/// contact. Requires a NavMesh baked in the scene and a separate trigger Collider (a
/// child object is recommended so it can be sized independently of the body's own
/// physical collider) tagged to call into this component's OnTriggerStay.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    [Header("Chase")]
    [Tooltip("Assigned automatically at Awake by tag if left empty.")]
    public Transform player;
    [Tooltip("How often the destination is refreshed, in seconds. Doesn't need to be every frame.")]
    public float chaseUpdateInterval = 0.2f;

    [Header("Damage")]
    public float damageAmount = 10f;
    public float damageCooldown = 1f;

    private NavMeshAgent agent;
    private float chaseTimer;
    private float damageTimer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found != null)
            {
                player = found.transform;
            }
        }
    }

    private void Update()
    {
        if (player == null || !agent.isOnNavMesh)
        {
            return;
        }

        chaseTimer -= Time.deltaTime;
        if (chaseTimer <= 0f)
        {
            agent.SetDestination(player.position);
            chaseTimer = chaseUpdateInterval;
        }

        if (damageTimer > 0f)
        {
            damageTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// The damage collider itself: a trigger Collider on this same GameObject. While
    /// something damageable stays inside it, deal damage on a cooldown rather than every
    /// physics tick.
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        if (damageTimer > 0f)
        {
            return;
        }

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null)
        {
            return;
        }

        damageable.TakeDamage(damageAmount);
        damageTimer = damageCooldown;
    }
}