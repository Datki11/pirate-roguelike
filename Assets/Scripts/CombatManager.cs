// CombatManager.cs
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CombatManager : Node
{
	[Export] public NodePath VfxLayerPath { get; set; }     // optional CanvasLayer/Control for VFX
	[Export] public PackedScene DamagePopupScene { get; set; }

	// NEW: where your Enemy nodes live (optional; convenient for static scenes)
	[Export] public NodePath EnemiesRootPath { get; set; }

	private Node _vfx;
	private Node _enemiesRoot;
	private readonly List<Enemy> _enemies = new();

	public override void _Ready()
	{
		_vfx = GetNodeOrNull<Node>(VfxLayerPath);
		_enemiesRoot = GetNodeOrNull<Node>(EnemiesRootPath);
		if (_enemiesRoot != null) RefreshEnemies();

		GD.Print($"CombatManager ready | VFX={_vfx?.GetType().Name ?? "null"} | PopupScene={(DamagePopupScene != null)} | Enemies={_enemies.Count}");
	}

	// ----- Roster management -----
	public void RefreshEnemies()
	{
		_enemies.Clear();
		if (_enemiesRoot == null) return;
		foreach (var n in _enemiesRoot.GetChildren())
			if (n is Enemy e) _enemies.Add(e);
	}

	public void RegisterEnemy(Enemy e)
	{
		if (e != null && !_enemies.Contains(e)) _enemies.Add(e);
	}

	public void UnregisterEnemy(Enemy e)
	{
		if (e != null) _enemies.Remove(e);
	}

	public IEnumerable<IDamageable> AliveEnemies()
	=> _enemies
		.Where(e => e != null && GodotObject.IsInstanceValid(e) && e.Alive)
		.Cast<IDamageable>();

	// ----- Effects -----
	public void DealDamage(IDamageable target, int amount)
	{
		GD.Print($"CombatManager.DealDamage -> {amount} on {target?.GetType().Name}");
		if (target == null || !target.Alive || amount <= 0) return;

		target.TakeDamage(amount);
		SpawnDamagePopupAt(target, amount, isHeal:false);
	}

	public void DealDamageMany(IEnumerable<IDamageable> targets, int amount)
	{
		foreach (var t in targets) DealDamage(t, amount);
	}

	private void SpawnDamagePopupAt(IDamageable target, int amount, bool isHeal)
	{
		if (DamagePopupScene == null) { GD.PushWarning("DamagePopupScene not set."); return; }

		var popup = DamagePopupScene.Instantiate<DamagePopup>();
		if (popup.Size == Vector2.Zero) { popup.CustomMinimumSize = new Vector2(32, 16); popup.Size = popup.CustomMinimumSize; }

		// Parent under the enemy’s parent (world space) so GlobalPosition just works.
		Node parent = (target as Node)?.GetParent() ?? GetTree().CurrentScene ?? this;
		parent.AddChild(popup);

		var anchor = target.GetPopupAnchorGlobal();
		var pos = (anchor - new Vector2(popup.Size.X * 0.5f, popup.Size.Y)).Floor();
		popup.GlobalPosition = pos;

		// No crazy Z values; being last child is enough
		popup.ZAsRelative = true;
		// parent.MoveChild(popup, parent.GetChildCount() - 1); // optional bring-to-front

		GD.Print($"SpawnDamagePopup(world-parent) -> pos={pos} amount={amount}");
		popup.ShowNumber(amount, isHeal);
	}
}
