using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class CombatManager : Node
{
	private const int PopupCanvasLayer = 950;
	private const int PopupZIndex = 4096;
	private const ulong PopupBurstWindowMsec = 250;
	private const string PopupLayerName = "CombatPopupLayer";
	private const int IntentCanvasLayer = 1990;
	private const int IntentZIndex = 4096;
	private const string IntentLayerName = "CombatIntentLayer";
	private const string IntentOverlayName = "CombatIntentOverlay";

	[Export] public NodePath VfxLayerPath { get; set; }          // optional CanvasLayer/Control for VFX
	[Export] public PackedScene DamagePopupScene { get; set; }

	// Optional convenience roots (static scenes)
	[Export] public NodePath EnemiesRootPath { get; set; }
	[Export] public NodePath PlayersRootPath { get; set; }
	[Export] public NodePath EnergyPath { get; set; }
	[Export] public float EnemyTurnStartDelaySec { get; set; } = 0.35f;
	[Export] public float StartOfTurnStatusEffectDelaySec { get; set; } = 0.9f;
	[Export] public float EnemyIntentArrowDurationSec { get; set; } = 0.365f;
	[Export] public float EnemyIntentTargetHoldSec { get; set; } = 0.20f;

	private Node _vfx;
	private CanvasLayer _popupLayer;
	private CanvasLayer _intentLayer;
	private CombatIntentOverlay _intentOverlay;
	private bool _intentOverlayAddDeferred;
	private Node _enemiesRoot;
	private Node _playersRoot;
	private EnergyManager _energy;
	private CombatTargeting _targeting;
	private bool _enemyTurnRunning;
	private bool _suppressEnemyHoverIntents;
	private readonly HashSet<Enemy> _enemiesPlayedThisTurn = new();

	private readonly List<Enemy> _enemies = new();
	private readonly List<IDamageable> _players = new();         // keep generic for future player units
	private readonly Dictionary<Enemy, IDamageable> _plannedEnemyTargets = new();
	private readonly List<CombatIntentLine> _enemyIntentLines = new();
	private readonly Dictionary<Node, PopupBurstState> _popupBursts = new();

	private readonly RandomNumberGenerator _rng = new();
	private readonly Dictionary<string, Texture2D> _popupIcons = new();
	private static readonly Color PopupBuffColor = new(0f, 1f, 1f);
	private static readonly Color PopupCurseColor = new(1f, 0f, 1f);
	private static readonly Color PopupBlockLossColor = new(0.52f, 0.84f, 1f);
	private static readonly Dictionary<string, string> PopupIconPaths = new()
	{
		["block"] = "res://Assets/Sprites/Icons/Generated/block_32x36.png",
		["protector"] = "res://Assets/Sprites/Icons/Generated/block_32x36.png",
		["bleed"] = "res://Assets/Sprites/Icons/Generated/bleed_32x36.png",
		["weak"] = "res://Assets/Sprites/Icons/Generated/weak_32x36.png",
		["regen"] = "res://Assets/Sprites/Icons/Generated/regen_32x36.png"
	};

	private sealed class PopupBurstState
	{
		public ulong LastSpawnMsec;
		public int NextSlot;
	}

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
			_energy.EnergyChanged += OnEnergyChanged;
		}

		ApplyDeckStacking();
		ApplyTurnDeckStates();
		RefreshEnemyIntents();

		GD.Print($"CombatManager ready | VFX={_vfx?.GetType().Name ?? "null"} | PopupScene={(DamagePopupScene != null)} | Enemies={_enemies.Count} | Players={_players.Count}");
	}

	public override void _ExitTree()
	{
		if (_energy != null)
		{
			_energy.PlayerTurnStarted -= OnPlayerTurnStarted;
			_energy.PlayerTurnEnded -= OnPlayerTurnEnded;
			_energy.EnergyChanged -= OnEnergyChanged;
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
		foreach (var enemy in _plannedEnemyTargets.Keys.Where(enemy => !_enemies.Contains(enemy)).ToList())
			_plannedEnemyTargets.Remove(enemy);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
		RefreshEnemyIntents();
	}

	public void RegisterEnemy(Enemy e)
	{
		if (e != null && !_enemies.Contains(e)) _enemies.Add(e);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
		RefreshEnemyIntents();
	}

	public void UnregisterEnemy(Enemy e)
	{
		if (e != null) _enemies.Remove(e);
		if (e != null) _plannedEnemyTargets.Remove(e);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
		RefreshEnemyIntents();
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
		foreach (var enemy in _plannedEnemyTargets.Where(kvp => !_players.Contains(kvp.Value)).Select(kvp => kvp.Key).ToList())
			_plannedEnemyTargets.Remove(enemy);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
		RefreshEnemyIntents();
	}

	public void RegisterPlayer(IDamageable d)
	{
		if (d != null && !_players.Contains(d)) _players.Add(d);
		ApplyDeckStacking();
		ApplyTurnDeckStates();
		RefreshEnemyIntents();
	}

	public void UnregisterPlayer(IDamageable d)
	{
		if (d != null) _players.Remove(d);
		if (d != null)
		{
			foreach (var enemy in _plannedEnemyTargets.Where(kvp => kvp.Value == d).Select(kvp => kvp.Key).ToList())
				_plannedEnemyTargets.Remove(enemy);
		}
		ApplyDeckStacking();
		ApplyTurnDeckStates();
		RefreshEnemyIntents();
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
		RefreshEnemyIntents();
	}

	private async void OnPlayerTurnEnded()
	{
		if (_enemyTurnRunning) return;
		_enemyTurnRunning = true;
		_enemiesPlayedThisTurn.Clear();
		ClearEnemyIntents();
		ApplyTurnDeckStates();

		await ToSignal(GetTree().CreateTimer(EnemyTurnStartDelaySec), "timeout");
		foreach (var enemy in _enemies.ToList())
		{
			if (enemy == null || !GodotObject.IsInstanceValid(enemy) || !enemy.Alive)
				continue;
			if (!AlivePlayers().Any())
				break;

			await ApplyStartOfTurnStatusesAsync(enemy);
			if (enemy == null || !GodotObject.IsInstanceValid(enemy) || !enemy.Alive)
			{
				_enemiesPlayedThisTurn.Add(enemy);
				ApplyTurnDeckStates();
				continue;
			}

			await enemy.PlayTurnAsync();
			_enemiesPlayedThisTurn.Add(enemy);
			ApplyTurnDeckStates();
		}

		foreach (var player in AlivePlayers().ToList())
			await ApplyStartOfTurnStatusesAsync(player);

		_enemyTurnRunning = false;
		_energy?.StartPlayerTurn();
	}

	private void ApplyTurnDeckStates()
	{
		bool playerTurn = _energy == null || _energy.IsPlayerTurn;
		bool playerHasEnergy = _energy == null || _energy.CanSpend(_energy.CardEnergyCost);

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
			{
				playerUnit.SetDeckDeadDimmed(!playerUnit.Alive);
				playerUnit.SetDeckTurnDimmed(!playerTurn || !playerUnit.Alive);
				playerUnit.SetDeckEnergyDimmed(playerTurn && playerUnit.Alive && !playerHasEnergy);
			}
		}
	}

	private void OnEnergyChanged(int current, int max)
	{
		ApplyTurnDeckStates();
		RefreshEnemyIntents();
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
		bool previousSuppressEnemyHoverIntents = _suppressEnemyHoverIntents;
		_suppressEnemyHoverIntents = true;
		UpdateIntentOverlayVisibility();

		try
		{
			if (source is PlayerUnit playerUnit) playerUnit.PlayCardAnimation();
			await deck.BeginCardPlayPresentation(card);

			bool targetsEnemies = side == Deck.DeckSide.Player;
			var opponents = targetsEnemies ? AliveEnemies().ToList() : AlivePlayers().ToList();
			var allies = targetsEnemies ? AlivePlayers().ToList() : AliveEnemies().ToList();

			var targetId = (card.TargetDef as TargetDef)?.Id ?? "single";   // "single" | "all" (or "multiple")
			IDamageable singleOpponent = chosenTarget != null && chosenTarget.Alive ? chosenTarget : GetPlannedEnemyTarget(source, card, targetId, opponents);
			bool lethal = false;
			bool pierced = false;

			await PresentCardIntentAsync(deck, card, source, opponents, allies, singleOpponent, chosenTarget, targetId);

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
							int attack = GetAttackAmountAfterWeak(source, effect.Amount, pulse: true);
							foreach (var target in ResolveAttackTargets(targetId, opponents, singleOpponent))
							{
								lethal |= DealAttackDamage(target, attack, out bool unblocked);
								pierced |= unblocked;
							}
							break;
						}
						case "body_slam":
						{
							int attack = GetAttackAmountAfterWeak(source, source?.Block ?? 0, pulse: true);
							foreach (var target in ResolveAttackTargets(targetId, opponents, singleOpponent))
							{
								lethal |= DealAttackDamage(target, attack, out bool unblocked);
								pierced |= unblocked;
							}
							break;
						}
						case "block":
						{
							foreach (var target in ResolveBeneficialTargets(targetId, allies, source))
								GainBlockWithPopup(target, effect.Amount);
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
								ApplyStatusWithPopup(target, "regen", effect.Amount);
							break;
						}
						case "protect":
						{
							foreach (var target in ResolveBeneficialTargets(targetId, allies, source))
								ApplyStatusWithPopup(target, "protector", effect.Amount);
							break;
						}
						case "strategist":
						{
							foreach (var target in ResolveBeneficialTargets(targetId, allies, source))
								ApplyStatusWithPopup(target, "strategist", effect.Amount);
							break;
						}
						case "bleed":
						case "weak":
						{
							foreach (var target in ResolveHarmfulTargets(targetId, opponents, singleOpponent))
								ApplyStatusWithPopup(target, effectId, effect.Amount);
							break;
						}
						case "play_top_cards":
						{
							await PlayAllyTopCards(source, chosenTarget, allies, targetId, effect.Amount);
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
		finally
		{
			_suppressEnemyHoverIntents = previousSuppressEnemyHoverIntents;
			RefreshEnemyIntents();
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

	public IReadOnlyList<CombatIntentLine> GetEnemyIntentLines()
		=> _enemyIntentLines;

	public void RefreshEnemyIntents()
	{
		if (Engine.IsEditorHint())
			return;

		bool playerTurn = _energy == null || _energy.IsPlayerTurn;
		if (!playerTurn || _enemyTurnRunning || IsPlayerTargetingActive())
		{
			ClearEnemyIntents();
			return;
		}

		_enemyIntentLines.Clear();
		var opponents = AlivePlayers().ToList();
		if (opponents.Count == 0)
		{
			UpdateIntentOverlayVisibility();
			return;
		}

		foreach (var enemy in _enemies.ToList())
		{
			if (enemy == null || !GodotObject.IsInstanceValid(enemy) || !enemy.Alive)
				continue;

			var card = enemy.PeekTopCard();
			if (card == null || !HasHarmfulEffect(card))
				continue;

			string targetId = (card.TargetDef as TargetDef)?.Id ?? "single";
			IDamageable singleOpponent = GetPlannedEnemyTarget(enemy, card, targetId, opponents);
			_enemyIntentLines.AddRange(BuildIntentLinesForCard(
				enemy,
				enemy,
				enemy.GetIntentSourceAnchorCanvas(),
				card,
				opponents,
				AliveEnemies().ToList(),
				singleOpponent,
				chosenTarget: null,
				targetId,
				includePositive: false));
		}

		UpdateIntentOverlayVisibility();
		_intentOverlay?.QueueRedraw();
	}

	private void ClearEnemyIntents()
	{
		_enemyIntentLines.Clear();
		UpdateIntentOverlayVisibility();
		_intentOverlay?.QueueRedraw();
	}

	private List<CombatIntentLine> BuildIntentLinesForCard(
		IDamageable source,
		Enemy sourceEnemy,
		Vector2 sourcePoint,
		CardData card,
		List<IDamageable> opponents,
		List<IDamageable> allies,
		IDamageable singleOpponent,
		IDamageable chosenTarget,
		string targetId,
		bool includePositive)
	{
		var byTarget = new Dictionary<(IDamageable Unit, bool Friendly), CombatIntentLine>();

		foreach (var effect in card.Effects ?? new Godot.Collections.Array<EffectEntry>())
		{
			if (effect?.Def == null || effect.Amount <= 0)
				continue;

			string effectId = effect.Def.Id;
			switch (effectId)
			{
				case "attack":
				{
					int attack = GetAttackAmountAfterWeak(source, effect.Amount);
					foreach (var target in ResolveAttackTargets(targetId, opponents, singleOpponent))
						GetOrCreateIntent(byTarget, sourceEnemy, sourcePoint, target, friendly: false).AttackAmount += attack;
					break;
				}
				case "body_slam":
				{
					int attack = GetAttackAmountAfterWeak(source, source?.Block ?? 0);
					foreach (var target in ResolveAttackTargets(targetId, opponents, singleOpponent))
						GetOrCreateIntent(byTarget, sourceEnemy, sourcePoint, target, friendly: false).AttackAmount += attack;
					break;
				}
				case "bleed":
				{
					foreach (var target in ResolveHarmfulTargets(targetId, opponents, singleOpponent))
						GetOrCreateIntent(byTarget, sourceEnemy, sourcePoint, target, friendly: false).BleedAmount += effect.Amount;
					break;
				}
				case "weak":
				{
					foreach (var target in ResolveHarmfulTargets(targetId, opponents, singleOpponent))
						GetOrCreateIntent(byTarget, sourceEnemy, sourcePoint, target, friendly: false).WeakAmount += effect.Amount;
					break;
				}
				case "block":
				case "heal":
				case "regen":
				case "protect":
				case "strategist":
				{
					if (!includePositive)
						break;

					foreach (var target in ResolveBeneficialTargets(targetId, allies, source))
						GetOrCreateIntent(byTarget, sourceEnemy, sourcePoint, target, friendly: true);
					break;
				}
				case "play_top_cards":
				{
					if (!includePositive)
						break;

					foreach (var target in ResolvePlayTopCardsIntentTargets(source, chosenTarget, allies, targetId))
						GetOrCreateIntent(byTarget, sourceEnemy, sourcePoint, target, friendly: true);
					break;
				}
			}
		}

		return byTarget.Values
			.Where(line => includePositive || line.AttackAmount > 0 || line.BleedAmount > 0 || line.WeakAmount > 0)
			.ToList();
	}

	private async Task PresentCardIntentAsync(
		Deck deck,
		CardData card,
		IDamageable source,
		List<IDamageable> opponents,
		List<IDamageable> allies,
		IDamageable singleOpponent,
		IDamageable chosenTarget,
		string targetId)
	{
		if (deck == null || card == null || source == null)
			return;

		Rect2 cardRect = deck.GetPlayedCardCanvasRect();
		Vector2 sourcePoint = (cardRect.Position + cardRect.Size * 0.5f).Floor();
		var lines = BuildIntentLinesForCard(
			source,
			source as Enemy,
			sourcePoint,
			card,
			opponents,
			allies,
			singleOpponent,
			chosenTarget,
			targetId,
			includePositive: true);
		if (lines.Count == 0)
			return;

		var overlay = EnsureIntentOverlay();
		if (overlay == null)
			return;

		overlay.Visible = true;
		if (!overlay.IsInsideTree())
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		float duration = Mathf.Max(0.01f, EnemyIntentArrowDurationSec);
		ulong startMsec = Time.GetTicksMsec();
		while (GodotObject.IsInstanceValid(overlay) && overlay.IsInsideTree())
		{
			float elapsed = (Time.GetTicksMsec() - startMsec) / 1000f;
			float progress = Mathf.Clamp(elapsed / duration, 0f, 1f);
			overlay.SetPresentationLines(lines, EaseOutCubic(progress));
			if (progress >= 1f)
				break;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		if (GodotObject.IsInstanceValid(overlay))
		{
			overlay.SetPresentationLines(lines, 1f);
			if (EnemyIntentTargetHoldSec > 0f)
				await ToSignal(GetTree().CreateTimer(EnemyIntentTargetHoldSec), "timeout");
			overlay.ClearPresentationLines();
			UpdateIntentOverlayVisibility();
		}
	}

	private static float EaseOutCubic(float t)
	{
		t = Mathf.Clamp(t, 0f, 1f);
		float inv = 1f - t;
		return 1f - inv * inv * inv;
	}

	private CombatIntentLine GetOrCreateIntent(Dictionary<(IDamageable Unit, bool Friendly), CombatIntentLine> byTarget, Enemy sourceEnemy, Vector2 source, IDamageable target, bool friendly)
	{
		if (target == null || !target.Alive)
			return new CombatIntentLine();

		var key = (target, friendly);
		if (byTarget.TryGetValue(key, out var line))
			return line;

		line = new CombatIntentLine
		{
			SourceEnemy = sourceEnemy,
			TargetUnit = target,
			TargetPlayer = target as PlayerUnit,
			Source = source,
			Target = GetUnitAnchorCanvas(target),
			TargetMarker = GetUnitTargetMarkerCanvas(target),
			TargetRect = GetUnitTargetRectCanvas(target),
			Friendly = friendly
		};
		byTarget[key] = line;
		return line;
	}

	private IDamageable GetPlannedEnemyTarget(IDamageable source, CardData card, string targetId, List<IDamageable> opponents)
	{
		if (IsAllTarget(targetId))
			return null;

		if (source is not Enemy enemy || !HasHarmfulEffect(card))
			return PickRandom(opponents);

		if (_plannedEnemyTargets.TryGetValue(enemy, out var planned) && planned != null && planned.Alive && opponents.Contains(planned))
			return planned;

		planned = PickRandom(opponents);
		if (planned != null)
			_plannedEnemyTargets[enemy] = planned;
		return planned;
	}

	private bool HasHarmfulEffect(CardData card)
		=> card?.Effects != null && card.Effects.Any(effect =>
			effect?.Def != null
			&& effect.Amount > 0
			&& effect.Def.Id is "attack" or "body_slam" or "bleed" or "weak");

	private Vector2 GetUnitAnchorCanvas(IDamageable unit)
	{
		return unit switch
		{
			PlayerUnit player when GodotObject.IsInstanceValid(player) => player.GetTargetingAnchorCanvas(),
			Enemy enemy when GodotObject.IsInstanceValid(enemy) => enemy.GetTargetingAnchorCanvas(),
			_ => GetPopupAnchorCanvas(unit, unit as Node)
		};
	}

	private Vector2 GetUnitTargetMarkerCanvas(IDamageable unit)
	{
		return unit switch
		{
			PlayerUnit player when GodotObject.IsInstanceValid(player) => player.GetIntentTargetMarkerCanvas(),
			Enemy enemy when GodotObject.IsInstanceValid(enemy) => enemy.GetTargetingAnchorCanvas() + new Vector2(0f, 24f),
			_ => GetPopupAnchorCanvas(unit, unit as Node) + new Vector2(0f, 24f)
		};
	}

	private Rect2 GetUnitTargetRectCanvas(IDamageable unit)
	{
		return unit switch
		{
			PlayerUnit player when GodotObject.IsInstanceValid(player) => player.GetTargetingCanvasRect(),
			Enemy enemy when GodotObject.IsInstanceValid(enemy) => enemy.GetTargetingCanvasRect(),
			_ => new Rect2(GetUnitAnchorCanvas(unit) - new Vector2(24f, 24f), new Vector2(48f, 48f))
		};
	}

	private void UpdateIntentOverlayVisibility()
	{
		var overlay = EnsureIntentOverlay();
		if (overlay == null)
			return;

		bool visible = !_suppressEnemyHoverIntents && _enemyIntentLines.Count > 0;
		if (!visible)
			overlay.ClearIntentTargetPreviews();
		overlay.Visible = visible;
	}

	private bool IsPlayerTargetingActive()
	{
		if (_targeting == null || !GodotObject.IsInstanceValid(_targeting))
			_targeting = GetTree()?.CurrentScene?.FindChild("CombatTargeting", true, false) as CombatTargeting
				?? GetTree()?.Root?.FindChild("CombatTargeting", true, false) as CombatTargeting;

		return _targeting?.IsTargetingActive == true;
	}

	private CombatIntentOverlay EnsureIntentOverlay()
	{
		if (_intentOverlay != null && GodotObject.IsInstanceValid(_intentOverlay))
		{
			EnsureIntentOverlayLayering();
			return _intentOverlay;
		}

		var root = GetTree()?.Root;
		CanvasLayer intentLayer = EnsureIntentLayer();
		Node parent = intentLayer ?? GetTree()?.CurrentScene ?? (Node)root ?? this;
		_intentOverlay = parent.GetNodeOrNull<CombatIntentOverlay>(IntentOverlayName);
		if (_intentOverlay == null || !GodotObject.IsInstanceValid(_intentOverlay))
		{
			_intentOverlay = CreateIntentOverlay();
			_intentOverlayAddDeferred = true;
			parent.CallDeferred(Node.MethodName.AddChild, _intentOverlay);
			return _intentOverlay;
		}
		else
		{
			_intentOverlay.Combat = this;
			_intentOverlay.TopLevel = true;
			_intentOverlay.ZAsRelative = false;
			_intentOverlay.ZIndex = IntentZIndex;
		}

		EnsureIntentOverlayLayering();
		return _intentOverlay;
	}

	private CanvasLayer EnsureIntentLayer()
	{
		if (_intentLayer != null && GodotObject.IsInstanceValid(_intentLayer))
		{
			_intentLayer.Layer = IntentCanvasLayer;
			return _intentLayer;
		}

		var root = GetTree()?.Root;
		if (root == null)
			return null;

		_intentLayer = root.GetNodeOrNull<CanvasLayer>(IntentLayerName);
		if (_intentLayer == null || !GodotObject.IsInstanceValid(_intentLayer))
		{
			_intentLayer = new CanvasLayer
			{
				Name = IntentLayerName,
				Layer = IntentCanvasLayer
			};
			root.CallDeferred(Node.MethodName.AddChild, _intentLayer);
		}
		else
		{
			_intentLayer.Layer = IntentCanvasLayer;
		}

		return _intentLayer;
	}

	private void EnsureIntentOverlayLayering()
	{
		CanvasLayer intentLayer = EnsureIntentLayer();
		if (_intentOverlay == null || !GodotObject.IsInstanceValid(_intentOverlay))
			return;

		Node currentParent = _intentOverlay.GetParent();
		if (currentParent != null)
			_intentOverlayAddDeferred = false;

		_intentOverlay.Combat = this;
		_intentOverlay.TopLevel = true;
		_intentOverlay.ZAsRelative = false;
		_intentOverlay.ZIndex = IntentZIndex;
		_intentOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;

		if (intentLayer != null && currentParent != intentLayer && !_intentOverlayAddDeferred)
		{
			currentParent?.RemoveChild(_intentOverlay);
			_intentOverlayAddDeferred = true;
			intentLayer.CallDeferred(Node.MethodName.AddChild, _intentOverlay);
		}
	}

	private CombatIntentOverlay CreateIntentOverlay()
	{
		var overlay = new CombatIntentOverlay
		{
			Name = IntentOverlayName,
			Combat = this,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			TopLevel = true,
			ZAsRelative = false,
			ZIndex = IntentZIndex
		};
		overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		return overlay;
	}

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

		int beforeBlock = target.Block;
		int hpDamage = target.TakeAttackDamage(amount);
		int blockedDamage = beforeBlock - target.Block;
		unblocked = hpDamage > 0;
		if (blockedDamage > 0)
			SpawnIconPopupAt(target, blockedDamage, "block", PopupBlockLossColor, positive: false);
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

	private void GainBlockWithPopup(IDamageable target, int amount)
	{
		if (target == null || !target.Alive || amount <= 0) return;
		target.GainBlock(amount);
		SpawnIconPopupAt(target, amount, "block", PopupBuffColor);
	}

	private void ApplyStatusWithPopup(IDamageable target, string id, int amount)
	{
		if (target == null || !target.Alive || amount <= 0) return;
		target.ApplyStatus(id, amount);
		bool buff = id is "regen" or "protector";
		SpawnIconPopupAt(target, amount, id, buff ? PopupBuffColor : PopupCurseColor);
	}

	private int GetAttackAmountAfterWeak(IDamageable source, int amount, bool pulse = false)
	{
		if (source == null || source.GetStatusAmount("weak") <= 0)
			return amount;

		if (pulse)
			PlayStatusPulse(source, "weak", negative: true);
		return Mathf.Max(1, Mathf.CeilToInt(amount * 0.5f));
	}

	private void PlayStatusPulse(IDamageable unit, string id, bool negative)
	{
		switch (unit)
		{
			case PlayerUnit player when GodotObject.IsInstanceValid(player):
				player.PlayStatusPulse(id, negative);
				break;
			case Enemy enemy when GodotObject.IsInstanceValid(enemy):
				enemy.PlayStatusPulse(id, negative);
				break;
		}
	}

	private List<IDamageable> ResolveHarmfulTargets(string targetId, List<IDamageable> opponents, IDamageable singleOpponent)
	{
		if (IsAllTarget(targetId))
			return opponents.Where(t => t != null && t.Alive).ToList();

		return ResolveSingleHarmfulTarget(opponents, singleOpponent);
	}

	private List<IDamageable> ResolveAttackTargets(string targetId, List<IDamageable> opponents, IDamageable singleOpponent)
	{
		if (IsAllTarget(targetId))
			return opponents.Where(t => t != null && t.Alive).ToList();

		return ResolveSingleHarmfulTarget(opponents, singleOpponent);
	}

	private List<IDamageable> ResolveSingleHarmfulTarget(List<IDamageable> opponents, IDamageable singleOpponent)
	{
		var protector = opponents.FirstOrDefault(t => t != null && t.Alive && t.GetStatusAmount("protector") > 0);
		if (protector != null)
			return new List<IDamageable> { protector };

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

	private List<IDamageable> ResolvePlayTopCardsIntentTargets(IDamageable source, IDamageable chosenTarget, List<IDamageable> allies, string targetId)
	{
		var validAllies = allies
			.Where(unit => unit != null && unit.Alive && unit != source)
			.ToList();

		if (IsAllTarget(targetId))
			return validAllies;

		if (chosenTarget != null && chosenTarget.Alive && chosenTarget != source && validAllies.Contains(chosenTarget))
			return new List<IDamageable> { chosenTarget };

		return validAllies.Count > 0 ? new List<IDamageable> { validAllies[0] } : new List<IDamageable>();
	}

	private bool IsAllTarget(string targetId)
		=> targetId is "all" or "multiple" or "all_enemies" or "all_allies";

	private async Task PlayAllyTopCards(IDamageable source, IDamageable chosenTarget, List<IDamageable> allies, string targetId, int count)
	{
		if (source == null || count <= 0)
			return;

		var validAllies = allies
			.Where(unit => unit != null && unit.Alive && unit != source)
			.ToList();

		if (IsAllTarget(targetId))
		{
			foreach (var ally in validAllies)
				await PlayTopCardsOnUnit(ally, count);
			return;
		}

		var target = chosenTarget != null && chosenTarget.Alive && chosenTarget != source
			? chosenTarget
			: PickRandom(validAllies);
		await PlayTopCardsOnUnit(target, count);
	}

	private async Task PlayTopCardsOnUnit(IDamageable unit, int count)
	{
		switch (unit)
		{
			case Enemy enemy:
				await enemy.PlayTopCardsAsync(count);
				break;
			case PlayerUnit player:
				await player.PlayTopCardsAsync(count);
				break;
		}
	}

	private async Task ApplyStartOfTurnStatusesAsync(IDamageable unit)
	{
		if (unit == null || !unit.Alive)
			return;

		int bleed = unit.GetStatusAmount("bleed");
		if (bleed > 0)
		{
			PlayStatusPulse(unit, "bleed", negative: true);
			DealDirectDamage(unit, bleed);
			await WaitForStatusEffectPresentation();
		}

		if (!unit.Alive)
			return;

		int regen = unit.GetStatusAmount("regen");
		if (regen > 0)
		{
			PlayStatusPulse(unit, "regen", negative: false);
			HealWithPopup(unit, regen);
			unit.ReduceStatus("regen", 1);
			await WaitForStatusEffectPresentation();
		}

		unit.ReduceStatus("weak", 1);
		unit.ReduceStatus("protector", 1);
	}

	private async Task WaitForStatusEffectPresentation()
	{
		if (StartOfTurnStatusEffectDelaySec <= 0f)
			return;

		await ToSignal(GetTree().CreateTimer(StartOfTurnStatusEffectDelaySec), "timeout");
	}

	private void SpawnDamagePopupAt(IDamageable target, int amount, bool isHeal)
	{
		if (DamagePopupScene == null) { GD.PushWarning("DamagePopupScene not set."); return; }
		var node = target as Node;
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		var popup = DamagePopupScene.Instantiate<DamagePopup>();
		if (popup.Size == Vector2.Zero)
		{
			popup.CustomMinimumSize = new Vector2(160, 56);
			popup.Size = popup.CustomMinimumSize;
		}

		Node parent = GetPopupParent();
		parent.AddChild(popup);
		popup.ZAsRelative = false;
		popup.ZIndex = PopupZIndex;

		var anchor = GetPopupAnchorCanvas(target, node);
		var pos = (anchor - new Vector2(popup.Size.X * 0.5f, popup.Size.Y) + GetPopupSlotOffset(node, popup.Size.Y)).Floor();
		popup.GlobalPosition = pos;

		GD.Print($"SpawnDamagePopup(high-layer) -> pos={pos} amount={amount}");
		popup.ShowNumber(amount, isHeal);
	}

	private void SpawnIconPopupAt(IDamageable target, int amount, string iconId, Color color, bool positive = true)
	{
		if (DamagePopupScene == null) { GD.PushWarning("DamagePopupScene not set."); return; }
		var node = target as Node;
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		var popup = DamagePopupScene.Instantiate<DamagePopup>();
		if (popup.Size == Vector2.Zero)
		{
			popup.CustomMinimumSize = new Vector2(160, 56);
			popup.Size = popup.CustomMinimumSize;
		}

		Node parent = GetPopupParent();
		parent.AddChild(popup);
		popup.ZAsRelative = false;
		popup.ZIndex = PopupZIndex;

		var anchor = GetPopupAnchorCanvas(target, node);
		var pos = (anchor - new Vector2(popup.Size.X * 0.5f, popup.Size.Y) + GetPopupSlotOffset(node, popup.Size.Y)).Floor();
		popup.GlobalPosition = pos;
		popup.ShowIconValue(amount, GetPopupIcon(iconId), color, positive);
	}

	private Node GetPopupParent()
	{
		if (_vfx is CanvasLayer configuredLayer && GodotObject.IsInstanceValid(configuredLayer))
		{
			configuredLayer.Layer = Mathf.Max(configuredLayer.Layer, PopupCanvasLayer);
			return configuredLayer;
		}

		if (_popupLayer != null && GodotObject.IsInstanceValid(_popupLayer))
			return _popupLayer;

		var root = GetTree()?.Root;
		_popupLayer = root?.GetNodeOrNull<CanvasLayer>(PopupLayerName);
		if (_popupLayer == null || !GodotObject.IsInstanceValid(_popupLayer))
		{
			_popupLayer = new CanvasLayer
			{
				Name = PopupLayerName,
				Layer = PopupCanvasLayer
			};
			Node parent = root ?? GetTree()?.CurrentScene ?? this;
			parent.AddChild(_popupLayer);
		}
		else
		{
			_popupLayer.Layer = PopupCanvasLayer;
		}

		return _popupLayer;
	}

	private Vector2 GetPopupAnchorCanvas(IDamageable target, Node targetNode)
	{
		var anchor = target.GetPopupAnchorGlobal();
		if (targetNode is Node2D node2D && node2D.GetViewport() != null)
			return node2D.GetViewport().GetCanvasTransform() * anchor;

		return anchor;
	}

	private Vector2 GetPopupSlotOffset(Node targetNode, float popupHeight)
	{
		int slot = GetPopupSlot(targetNode);
		if (slot <= 0)
			return Vector2.Zero;

		return new Vector2(0f, -slot * Mathf.Max(1f, popupHeight));
	}

	private int GetPopupSlot(Node targetNode)
	{
		ulong now = Time.GetTicksMsec();
		if (!_popupBursts.TryGetValue(targetNode, out PopupBurstState state))
		{
			state = new PopupBurstState();
			_popupBursts[targetNode] = state;
		}

		if (now - state.LastSpawnMsec > PopupBurstWindowMsec)
			state.NextSlot = 0;

		int slot = state.NextSlot;
		state.NextSlot++;
		state.LastSpawnMsec = now;
		return slot;
	}

	private Texture2D GetPopupIcon(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		if (_popupIcons.TryGetValue(id, out Texture2D icon))
			return icon;

		if (!PopupIconPaths.TryGetValue(id, out string path))
			return null;

		icon = LoadPopupTexture(path);
		_popupIcons[id] = icon;
		return icon;
	}

	private Texture2D LoadPopupTexture(string path)
	{
		var texture = ResourceLoader.Load<Texture2D>(path);
		if (texture != null)
			return texture;

		var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
		return image == null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
	}
}
