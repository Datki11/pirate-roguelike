using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CombatManager : Node
{
	[Export] public NodePath VfxLayerPath { get; set; }          // optional CanvasLayer/Control for VFX
	[Export] public PackedScene DamagePopupScene { get; set; }

	// Optional convenience roots (static scenes)
	[Export] public NodePath EnemiesRootPath { get; set; }
	[Export] public NodePath PlayersRootPath { get; set; }

	private Node _vfx;
	private Node _enemiesRoot;
	private Node _playersRoot;

	private readonly List<Enemy> _enemies = new();
	private readonly List<IDamageable> _players = new();         // keep generic for future player units

	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		_rng.Randomize();

		_vfx = GetNodeOrNull<Node>(VfxLayerPath);

		_enemiesRoot = GetNodeOrNull<Node>(EnemiesRootPath);
		if (_enemiesRoot != null) RefreshEnemies();

		_playersRoot = GetNodeOrNull<Node>(PlayersRootPath);
		if (_playersRoot != null) RefreshPlayers();

		GD.Print($"CombatManager ready | VFX={_vfx?.GetType().Name ?? "null"} | PopupScene={(DamagePopupScene != null)} | Enemies={_enemies.Count} | Players={_players.Count}");
	}

	// -------------------------------------------------------------------------
	// Roster management
	// -------------------------------------------------------------------------
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

	public IEnumerable<IDamageable> AliveEnemies() =>
		_enemies.Where(e => e != null && GodotObject.IsInstanceValid(e) && e.Alive)
				.Cast<IDamageable>();

	public void RefreshPlayers()
	{
		_players.Clear();
		if (_playersRoot == null) return;
		foreach (var n in _playersRoot.GetChildren())
			if (n is IDamageable d && (n as Node) != null) _players.Add(d);
	}

	public void RegisterPlayer(IDamageable d)
	{
		if (d != null && !_players.Contains(d)) _players.Add(d);
	}

	public void UnregisterPlayer(IDamageable d)
	{
		if (d != null) _players.Remove(d);
	}

	public IEnumerable<IDamageable> AlivePlayers() =>
		_players.Where(p => p != null && (p as Node) != null && GodotObject.IsInstanceValid(p as Node) && p.Alive);

	// -------------------------------------------------------------------------
	// Auto play entry point (used by enemies, or any auto-cards)
	// -------------------------------------------------------------------------
	public void PlayCardAuto(Deck deck, CardData card, Deck.DeckSide side)
	{
		if (deck == null || card == null) return;

		// Which group does this card act ON?
		// Player deck typically targets enemies; Enemy deck typically targets players.
		bool targetsEnemies = side == Deck.DeckSide.Player;

		var targetId = (card.TargetDef as TargetDef)?.Id ?? "single";   // "single" | "all" (or "multiple")
		int attack = SumEffect(card, "attack");
		if (attack <= 0)
		{
			GD.Print("PlayCardAuto | no attack on card, just discarding.");
			deck.AdvanceTopToDiscard();
			return;
		}

		if (targetId == "all" || targetId == "multiple")
		{
			var group = targetsEnemies ? AliveEnemies().ToList() : AlivePlayers().ToList();
			if (group.Count == 0)
			{
				GD.Print("PlayCardAuto | no targets for ALL.");
				deck.AdvanceTopToDiscard();
				return;
			}
			DealDamageMany(group, attack);
		}
		else // "single" (default/fallback)
		{
			var group = targetsEnemies ? AliveEnemies().ToList() : AlivePlayers().ToList();
			var tgt = PickRandom(group);
			if (tgt != null) DealDamage(tgt, attack);
			else GD.Print("PlayCardAuto | no single target found.");
		}

		deck.AdvanceTopToDiscard();
	}

	private T PickRandom<T>(IList<T> list) where T : class
	{
		if (list == null || list.Count == 0) return null;
		int i = (int)_rng.RandiRange(0, list.Count - 1);
		return list[i];
	}

	private int SumEffect(CardData c, string effectId) =>
		c?.Effects == null ? 0 :
		c.Effects.Where(e => e?.Def?.Id == effectId)
				 .Sum(e => e.Amount);

	// -------------------------------------------------------------------------
	// Effects
	// -------------------------------------------------------------------------
	public void DealDamage(IDamageable target, int amount)
	{
		GD.Print($"CombatManager.DealDamage -> {amount} on {target?.GetType().Name}");
		if (target == null || !target.Alive || amount <= 0) return;

		target.TakeDamage(amount);
		SpawnDamagePopupAt(target, amount, isHeal: false);
	}

	public void DealDamageMany(IEnumerable<IDamageable> targets, int amount)
	{
		foreach (var t in targets) DealDamage(t, amount);
	}

	private void SpawnDamagePopupAt(IDamageable target, int amount, bool isHeal)
	{
		if (DamagePopupScene == null) { GD.PushWarning("DamagePopupScene not set."); return; }
		var node = target as Node;
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		var popup = DamagePopupScene.Instantiate<DamagePopup>();
		if (popup.Size == Vector2.Zero)
		{
			popup.CustomMinimumSize = new Vector2(32, 16);
			popup.Size = popup.CustomMinimumSize;
		}

		// Parent under the target's parent so global coordinates make sense
		Node parent = node.GetParent() ?? GetTree().CurrentScene ?? this;
		parent.AddChild(popup);

		var anchor = target.GetPopupAnchorGlobal();
		var pos = (anchor - new Vector2(popup.Size.X * 0.5f, popup.Size.Y)).Floor();
		popup.GlobalPosition = pos;

		// No extreme z; just relative
		popup.ZAsRelative = true;

		GD.Print($"SpawnDamagePopup(world-parent) -> pos={pos} amount={amount}");
		popup.ShowNumber(amount, isHeal);
	}
}
