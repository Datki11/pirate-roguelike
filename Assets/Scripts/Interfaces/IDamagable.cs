using Godot;

public interface IDamageable
{
	bool Alive { get; }
	int HP { get; }
	int MaxHP { get; }

	void TakeDamage(int amount);
	void Heal(int amount);

	/// World-space point above the unit’s head for damage popups.
	Vector2 GetPopupAnchorGlobal();
}
