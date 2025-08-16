// CombatManager.cs
using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CombatManager : Node
{
	[Export] public PackedScene DamagePopupScene { get; set; }

	public void DealDamage(IDamageable target, int amount)
	{
		GD.Print($"CombatManager.DealDamage -> {amount} on {target?.GetType().Name}");
		if (target == null || !target.Alive || amount <= 0) return;

		target.TakeDamage(amount);
		SpawnDamagePopupAt(target, amount, isHeal:false);
	}

	public void DealDamageMany(IEnumerable<IDamageable> targets, int amount)
	{
		foreach (var t in targets.Where(t => t != null && t.Alive))
			DealDamage(t, amount);
	}

	private void SpawnDamagePopupAt(IDamageable target, int amount, bool isHeal)
	{
		if (DamagePopupScene == null) { GD.PushWarning("DamagePopupScene not set."); return; }

		// 1) Make popup
		var popup = DamagePopupScene.Instantiate<DamagePopup>();
		if (popup.Size == Vector2.Zero) { popup.CustomMinimumSize = new Vector2(32, 16); popup.Size = popup.CustomMinimumSize; }
		popup.ZIndex = 4000;

		// 2) Choose a parent in the SAME canvas as the enemy (world space)
		Node parent = null;
		if (target is Node n && n.GetParent() != null) parent = n.GetParent();
		if (parent == null) parent = GetTree().CurrentScene ?? this; // last resort

		parent.AddChild(popup);

		// 3) Position in world coords (these match since parent shares the canvas)
		var anchor = target.GetPopupAnchorGlobal();
		var pos = (anchor - new Vector2(popup.Size.X * 0.5f, popup.Size.Y * 0.5f)).Floor();
		popup.GlobalPosition = pos;

		popup.ShowNumber(amount, isHeal);
	}
}
