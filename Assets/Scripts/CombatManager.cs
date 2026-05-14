using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class CombatManager : Node
{
	[Export] public NodePath VfxLayerPath { get; set; }          // optional CanvasLayer/Control for VFX
	[Export] public PackedScene DamagePopupScene { get; set; }

	// Optional convenience roots (static scenes)
	[Export] public NodePath EnemiesRootPath { get; set; }
	[Export] public NodePath PlayersRootPath { get; set; }
	[Export] public NodePath EnergyPath { get; set; }
	[Export] public float EnemyTurnStartDelaySec { get; set; } = 0.35f;

	private Node _vfx;
	private Node _enemiesRoot;
	private Node _playersRoot;
	private EnergyManager _energy;
	private bool _enemyTurnRunning;

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

		_energy = GetNodeOrNull<EnergyManager>(EnergyPath);
		if (_energy != null)
			_energy.PlayerTurnEnded += OnPlayerTurnEnded;

		GD.Print($"CombatManager ready | VFX={_vfx?.GetType().Name ?? "null"} | PopupScene={(DamagePopupScene != null)} | Enemies={_enemies.Count} | Players={_players.Count}");
	}

	public override void _ExitTree()
	{
		if (_energy != null)
			_energy.PlayerTurnEnded -= OnPlayerTurnEnded;
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

	private async void OnPlayerTurnEnded()
	{
		if (_enemyTurnRunning) return;
		_enemyTurnRunning = true;

		await ToSignal(GetTree().CreateTimer(EnemyTurnStartDelaySec), "timeout");
		foreach (var enemy in _enemies.ToList())
		{
			if (enemy == null || !GodotObject.IsInstanceValid(enemy) || !enemy.Alive)
				continue;
			if (!AlivePlayers().Any())
				break;

			await enemy.PlayTurnAsync();
		}

		_enemyTurnRunning = false;
		_energy?.StartPlayerTurn();
	}

	// -------------------------------------------------------------------------
	// Auto play entry point (used by enemies, or any auto-cards)
	// -------------------------------------------------------------------------
	public Task PlayCardAuto(Deck deck, CardData card, Deck.DeckSide side)
		=> PlayCard(deck, card, side, null, null);

	public Task PlayCardAuto(Deck deck, CardData card, Deck.DeckSide side, IDamageable source)
		=> PlayCard(deck, card, side, null, source);

	public Task PlayCardOnTarget(Deck deck, CardData card, Deck.DeckSide side, IDamageable target, IDamageable source = null)
		=> PlayCard(deck, card, side, target, source);

	private async Task PlayCard(Deck deck, CardData card, Deck.DeckSide side, IDamageable chosenTarget, IDamageable source)
	{
		if (deck == null || card == null) return;
		if (source is PlayerUnit playerUnit) playerUnit.PlayCardAnimation();
		await deck.BeginCardPlayPresentation(card);

		// Which group does this card act ON?
		// Player deck typically targets enemies; Enemy deck typically targets players.
		bool targetsEnemies = side == Deck.DeckSide.Player;

		var targetId = (card.TargetDef as TargetDef)?.Id ?? "single";   // "single" | "all" (or "multiple")
		int attack = SumEffect(card, "attack");
		bool lethal = false;

		if (attack > 0)
		{
			if (targetId == "all" || targetId == "multiple" || targetId == "all_enemies")
			{
				var group = targetsEnemies ? AliveEnemies().ToList() : AlivePlayers().ToList();
				if (group.Count == 0)
				{
					GD.Print("PlayCard | no targets for ALL.");
					await deck.AdvanceTopToDiscardWithPresentation(card);
					return;
				}
				lethal = DealDamageMany(group, attack);
			}
			else // "single" (default/fallback)
			{
				var tgt = chosenTarget;
				if (tgt == null)
				{
					var group = targetsEnemies ? AliveEnemies().ToList() : AlivePlayers().ToList();
					tgt = PickRandom(group);
				}
				if (tgt != null) lethal = DealDamage(tgt, attack);
				else GD.Print("PlayCard | no single target found.");
			}
		}

		if (lethal && card.LethalHealAmount > 0 && source != null && source.Alive)
		{
			source.Heal(card.LethalHealAmount);
			SpawnDamagePopupAt(source, card.LethalHealAmount, isHeal: true);
		}

		if (lethal && card.ReturnToDrawOnLethal)
		{
			await deck.FinishCardPlayPresentationWithoutDiscard();
			deck.EnsureTop();
		}
		else
		{
			await deck.AdvanceTopToDiscardWithPresentation(card);
		}
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
	public bool DealDamage(IDamageable target, int amount)
	{
		GD.Print($"CombatManager.DealDamage -> {amount} on {target?.GetType().Name}");
		if (target == null || !target.Alive || amount <= 0) return false;

		target.TakeDamage(amount);
		SpawnDamagePopupAt(target, amount, isHeal: false);
		return !target.Alive;
	}

	public bool DealDamageMany(IEnumerable<IDamageable> targets, int amount)
	{
		bool anyLethal = false;
		foreach (var t in targets)
			anyLethal |= DealDamage(t, amount);
		return anyLethal;
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
