using UnityEngine;

/// <summary>
/// Minimal health component so damage sources (e.g. EnemyAI's damage collider) have
/// something to actually affect on the player.
/// </summary>
public class Health : MonoBehaviour, IDamageable
{
    public float maxHealth = 100f;
    public float currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        Debug.Log($"{gameObject.name} took {amount} damage ({currentHealth}/{maxHealth} remaining).");

        if (currentHealth <= 0f)
        {
            Debug.Log($"{gameObject.name} died.");
        }
    }
}