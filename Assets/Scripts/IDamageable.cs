/// <summary>
/// Implemented by anything that can take damage (the player, destructible props, etc.),
/// so attackers - like EnemyAI's damage collider - don't need to know the concrete type.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount);
}