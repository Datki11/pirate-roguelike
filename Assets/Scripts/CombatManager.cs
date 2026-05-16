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
	private readonly HashSet<Enemy> _enemiesPlayedThisTurn = new();

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
		{
			_energy.PlayerTurnStarted += OnPlayerTurnStarted;
			_energy.PlayerTurnEnded += OnPlayerTurnEnded;
		}

		ApplyDeckStacking();
		ApplyTurnDeckStates();

		GD.Print($"CombatManager ready | VFX={_vfx?.GetType().Name ?? "null"} | PopupScene={(DamagePopupScene != null)} | Enemies={_enemies.Count} | Players={_players.Count}");
	}

	public override void _ExitTree()
	{
		if (_energy != null)
		{
			_energy.PlayerTurnStarted -= OnPlayerTurnStarted;
			_energy.PlayerTurnEnded -= OnPlayerTurnEnded;
		}
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
		ApplyDeckStacking();
		ApplyTurnDeckStates();
	}

	public void RegisterEnemy(Enemy e)
	{
		if (e != null && !_enemies.Contains(e)) _enemies.Add(e);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
	}

	public void UnregisterEnemy(Enemy e)
	{
		if (e != null) _enemies.Remove(e);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
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
		ApplyDeckStacking();
		ApplyTurnDeckStates();
	}

	public void RegisterPlayer(IDamageable d)
	{
		if (d != null && !_players.Contains(d)) _players.Add(d);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
	}

	public void UnregisterPlayer(IDamageable d)
	{
		if (d != null) _players.Remove(d);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
	}

	public IEnumerable<IDamageable> AlivePlayers() =>
		_players.Where(p => p != null && (p as Node) != null && GodotObject.IsInstanceValid(p as Node) && p.Alive);

	private void OnPlayerTurnStarted()
	{
		_enemiesPlayedThisTurn.Clear();
		foreach (var unit in AlivePlayers().Concat(AliveEnemies()).ToList())
			unit.ClearBlock();
		ApplyDeckStacking();
		ApplyTurnDeckStates();
	}

	private async void OnPlayerTurnEnded()
	{
		if (_enemyTurnRunning) return;
		_enemyTurnRunning = true;
		_enemiesPlayedThisTurn.Clear();
		ApplyTurnDeckStates();
		foreach (var player in AlivePlayers().ToList())
			ApplyEndOfTurnStatuses(player);

		await ToSignal(GetTree().CreateTimer(EnemyTurnStartDelaySec), "timeout");
		foreach (var enemy in _enemies.ToList())
		{
			if (enemy == null || !GodotObject.IsInstanceValid(enemy) || !enemy.Alive)
				continue;
			if (!AlivePlayers().Any())
				break;

			await enemy.PlayTurnAsync();
			if (enemy != null && GodotObject.IsInstanceValid(enemy) && enemy.Alive)
				ApplyEndOfTurnStatuses(enemy);
			_enemiesPlayedThisTurn.Add(enemy);
			ApplyTurnDeckStates();
		}

		_enemyTurnRunning = false;
		_energy?.StartPlayerTurn();
	}

	private void ApplyTurnDeckStates()
	{
		bool playerTurn = _energy == null || _energy.IsPlayerTurn;

		foreach (var enemy in _enemies)
		{
			if (enemy == null || !GodotObject.IsInstanceValid(enemy))
				continue;

			bool enemyWillActThisTurn = !playerTurn && enemy.Alive && !_enemiesPlayedThisTurn.Contains(enemy);
			enemy.SetDeckTurnDimmed(!enemyWillActThisTurn);
		}

		foreach (var player in _players)
		{
			if (player is PlayerUnit playerUnit && GodotObject.IsInstanceValid(playerUnit))
				playerUnit.SetDeckTurnDimmed(!playerTurn);
		}
	}

	private void ApplyDeckStacking()
	{
		var sortedEnemies = _enemies
			.Where(e => e != null && GodotObject.IsInstanceValid(e))
			.OrderBy(e => e.GetDeckStackAnchorGlobal().X)
			.ToList();

		for (int i = 0; i < sortedEnemies.Count; i++)
			sortedEnemies[i].SetDeckBaseDrawPriority(2000 + (sortedEnemies.Count - i) * 10);

		var sortedPlayers = _players
			.OfType<PlayerUnit>()
			.Where(p => GodotObject.IsInstanceValid(p))
			.OrderBy(p => p.GetDeckStackAnchorGlobal().X)
			.ToList();

		for (int i = 0; i < sortedPlayers.Count; i++)
			sortedPlayers[i].SetDeckBaseDrawPriority(1000 + (sortedPlayers.Count - i) * 10);
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
		source ??= deck.GetParent() as IDamageable;
		if (source is PlayerUnit playerUnit) playerUnit.PlayCardAnimation();
		await deck.BeginCardPlayPresentation(card);

		bool targetsEnemies = side == Deck.DeckSide.Player;
		var opponents = targetsEnemies ? AliveEnemies().ToList() : AlivePlayers().ToList();
		var allies = targetsEnemies ? AlivePlayers().ToList() : AliveEnemies().ToList();

		var targetId = (card.TargetDef as TargetDef)?.Id ?? "single";   // "single" | "all" (or "multiple")
		IDamageable singleOpponent = chosenTarget != null && chosenTarget.Alive ? chosenTarget : PickRandom(opponents);
		bool lethal = false;
		bool pierced = false;

		if (card.Effects != null)
		{
			foreach (var effect in card.Effects)
			{
				if (effect?.Def == null || effect.Amount <= 0)
					continue;

				string effectId = effect.Def.Id;
				switch (effectId)
				{
					case "attack":
					{
						int attack = GetAttackAmountAfterWeak(source, effect.Amount);
						foreach (var target in ResolveHarmfulTargets(targetId, opponents, singleOpponent))
						{
							lethal |= DealAttackDamage(target, attack, out bool unblocked);
							pierced |= unblocked;
						}
						break;
					}
					case "block":
					{
						foreach (var target in ResolveBeneficialTargets(targetId, allies, source))
							target.GainBlock(effect.Amount);
						break;
					}
					case "heal":
					{
						foreach (var target in ResolveBeneficialTargets(targetId, allies, source))
							HealWithPopup(target, effect.Amount);
						break;
					}
					case "regen":
					{
						foreach (var target in ResolveBeneficialTargets(targetId, allies, source))
							target.ApplyStatus("regen", effect.Amount);
						break;
					}
					case "bleed":
					case "weak":
					{
						foreach (var target in ResolveHarmfulTargets(targetId, opponents, singleOpponent))
							target.ApplyStatus(effectId, effect.Amount);
						break;
					}
					case "play_top_cards":
					{
						if (source is Enemy enemySource)
							await PlayAllyTopCards(enemySource, effect.Amount);
						break;
					}
				}
			}
		}

		if (pierced && card.Trigger == CardTrigger.Pierce && card.PierceHealAmount > 0 && source != null && source.Alive)
			HealWithPopup(source, card.PierceHealAmount);

		if (lethal && card.LethalHealAmount > 0 && source != null && source.Alive)
			HealWithPopup(source, card.LethalHealAmount);

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
		return DealAttackDamage(target, amount, out _);
	}

	public bool DealDamageMany(IEnumerable<IDamageable> targets, int amount)
	{
		bool anyLethal = false;
		foreach (var t in targets)
			anyLethal |= DealAttackDamage(t, amount, out _);
		return anyLethal;
	}

	private bool DealAttackDamage(IDamageable target, int amount, out bool unblocked)
	{
		unblocked = false;
		GD.Print($"CombatManager.DealAttackDamage -> {amount} on {target?.GetType().Name}");
		if (target == null || !target.Alive || amount <= 0) return false;

		int hpDamage = target.TakeAttackDamage(amount);
		unblocked = hpDamage > 0;
		if (hpDamage > 0)
			SpawnDamagePopupAt(target, hpDamage, isHeal: false);
		return !target.Alive;
	}

	private bool DealDirectDamage(IDamageable target, int amount)
	{
		if (target == null || !target.Alive || amount <= 0) return false;
		target.TakeDamage(amount);
		SpawnDamagePopupAt(target, amount, isHeal: false);
		return !target.Alive;
	}

	private void HealWithPopup(IDamageable target, int amount)
	{
		if (target == null || !target.Alive || amount <= 0) return;
		int before = target.HP;
		target.Heal(amount);
		int healed = target.HP - before;
		if (healed > 0)
			SpawnDamagePopupAt(target, healed, isHeal: true);
	}

	private int GetAttackAmountAfterWeak(IDamageable source, int amount)
	{
		if (source == null || source.GetStatusAmount("weak") <= 0)
			return amount;

		return Mathf.Max(1, Mathf.CeilToInt(amount * 0.5f));
	}

	private List<IDamageable> ResolveHarmfulTargets(string targetId, List<IDamageable> opponents, IDamageable singleOpponent)
	{
		if (IsAllTarget(targetId))
			return opponents.Where(t => t != null && t.Alive).ToList();

		return singleOpponent != null && singleOpponent.Alive ? new List<IDamageable> { singleOpponent } : new List<IDamageable>();
	}

	private List<IDamageable> ResolveBeneficialTargets(string targetId, List<IDamageable> allies, IDamageable source)
	{
		if (IsAllTarget(targetId))
			return allies.Where(t => t != null && t.Alive).ToList();

		if (source != null && source.Alive)
			return new List<IDamageable> { source };

		return new List<IDamageable>();
	}

	private bool IsAllTarget(string targetId)
		=> targetId is "all" or "multiple" or "all_enemies" or "all_allies";

	private async Task PlayAllyTopCards(Enemy source, int count)
	{
		if (source == null || count <= 0)
			return;

		var allies = _enemies
			.Where(e => e != null && GodotObject.IsInstanceValid(e) && e.Alive && e != source)
			.ToList();
		var ally = PickRandom(allies);
		if (ally != null)
			await ally.PlayTopCardsAsync(count);
	}

	private void ApplyEndOfTurnStatuses(IDamageable unit)
	{
		if (unit == null || !unit.Alive)
			return;

		int bleed = unit.GetStatusAmount("bleed");
		if (bleed > 0)
			DealDirectDamage(unit, bleed);

		if (!unit.Alive)
			return;

		int regen = unit.GetStatusAmount("regen");
		if (regen > 0)
		{
			HealWithPopup(unit, regen);
			unit.ReduceStatus("regen", 1);
		}

		unit.ReduceStatus("weak", 1);
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
