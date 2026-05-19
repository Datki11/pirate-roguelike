using Godot;

public sealed class CombatIntentLine
{
	public Enemy SourceEnemy;
	public IDamageable TargetUnit;
	public PlayerUnit TargetPlayer;
	public Vector2 Source;
	public Vector2 Target;
	public Vector2 TargetMarker;
	public Rect2 TargetRect;
	public bool Friendly;
	public int AttackAmount;
	public int BleedAmount;
	public int WeakAmount;
}
