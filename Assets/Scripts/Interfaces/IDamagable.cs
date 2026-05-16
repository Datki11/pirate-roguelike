using Godot;

public interface IDamageable
{
	bool Alive { get; }
	int HP { get; }
	int MaxHP { get; }
	int Block { get; }

	void TakeDamage(int amount);
	int TakeAttackDamage(int amount);
	void Heal(int amount);
	void GainBlock(int amount);
	void ClearBlock();
	void ApplyStatus(string id, int amount);
	void ReduceStatus(string id, int amount);
	int GetStatusAmount(string id);

	/// World-space point above the unit’s head for damage popups.
	Vector2 GetPopupAnchorGlobal();
}
