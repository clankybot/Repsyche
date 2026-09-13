using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Basic underground-dwelling boss behavior: starts burrowed (moved straight down below
/// the surface, NavMeshAgent disabled so it doesn't try to path while buried), waits for
/// the player to come within detectionRadius, erupts straight up to the surface, chases
/// via NavMeshAgent exactly like EnemyAI, then automatically re-burrows after
/// chaseDuration to reposition for another ambush. Deals contact damage the same way
/// EnemyAI does - a trigger collider on this object, IDamageable, a cooldown - but only
/// while actually Chasing, not while underground or transitioning.
///
/// This is deliberately the "basic" state machine the brief asked for, not a full boss
/// encounter - no attack telegraphs, phases, or emerge-location prediction. Those are
/// a separate, much larger design task on top of this.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class WormBossAI : MonoBehaviour
{
    private enum State
    {
        Underground,
        Emerging,
        Chasing,
        Reburrowing,
    }

    [Header("Player")]
    [Tooltip("Assigned automatically at Awake by tag if left empty.")]
    public Transform player;
    [Tooltip("Horizontal distance at which the buried boss notices the player and starts emerging.")]
    public float detectionRadius = 25f;

    [Header("Underground")]
    [Tooltip("How far below its starting surface height the boss waits while burrowed.")]
    public float burrowDepth = 10f;
    public float emergeSpeed = 6f;
    public float reburrowSpeed = 4f;
    [Tooltip("Seconds spent chasing before automatically re-burrowing to reposition.")]
    public float chaseDuration = 15f;
    [Tooltip("Minimum seconds spent underground after re-burrowing before it can emerge again.")]
    public float minBurrowTime = 4f;

    [Header("Chase")]
    public float chaseUpdateInterval = 0.25f;

    [Header("Damage")]
    public float damageAmount = 25f;
    public float damageCooldown = 1.5f;

    private NavMeshAgent agent;
    private State state;
    private float surfaceY;
    private float stateTimer;
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

        surfaceY = transform.position.y;
        EnterUnderground();
    }

    private void EnterUnderground()
    {
        state = State.Underground;
        stateTimer = 0f;
        agent.enabled = false;
        Vector3 pos = transform.position;
        pos.y = surfaceY - burrowDepth;
        transform.position = pos;
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        switch (state)
        {
            case State.Underground:
                UpdateUnderground();
                break;
            case State.Emerging:
                UpdateEmerging();
                break;
            case State.Chasing:
                UpdateChasing();
                break;
            case State.Reburrowing:
                UpdateReburrowing();
                break;
        }

        if (damageTimer > 0f)
        {
            damageTimer -= Time.deltaTime;
        }
    }

    private void UpdateUnderground()
    {
        stateTimer += Time.deltaTime;
        if (stateTimer < minBurrowTime)
        {
            return;
        }
        if (FlatDistance(player.position, transform.position) <= detectionRadius)
        {
            state = State.Emerging;
        }
    }

    private void UpdateEmerging()
    {
        Vector3 pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, surfaceY, emergeSpeed * Time.deltaTime);
        transform.position = pos;

        if (Mathf.Abs(pos.y - surfaceY) < 0.01f)
        {
            agent.enabled = true;
            state = State.Chasing;
            chaseTimer = 0f;
            stateTimer = 0f;
        }
    }

    private void UpdateChasing()
    {
        chaseTimer += Time.deltaTime;
        stateTimer -= Time.deltaTime;

        if (stateTimer <= 0f && agent.isOnNavMesh)
        {
            agent.SetDestination(player.position);
            stateTimer = chaseUpdateInterval;
        }

        if (chaseTimer >= chaseDuration)
        {
            state = State.Reburrowing;
            agent.enabled = false;
        }
    }

    private void UpdateReburrowing()
    {
        Vector3 pos = transform.position;
        float targetY = surfaceY - burrowDepth;
        pos.y = Mathf.MoveTowards(pos.y, targetY, reburrowSpeed * Time.deltaTime);
        transform.position = pos;

        if (Mathf.Abs(pos.y - targetY) < 0.01f)
        {
            EnterUnderground();
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>Contact damage, matching EnemyAI's pattern - only while actually
    /// surfaced and chasing, so a buried or transitioning boss can't be hurt or hurt
    /// the player.</summary>
    private void OnTriggerStay(Collider other)
    {
        if (state != State.Chasing || damageTimer > 0f)
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
