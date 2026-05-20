// CombatTargeting.cs
using Godot;
using System.Linq;
using Godot.Collections;

public partial class CombatTargeting : Node
{
	[Export] public Array<NodePath> DeckPaths { get; set; } = new();
	[Export] public NodePath EnemiesPath { get; set; }
	[Export] public NodePath CombatPath { get; set; }   // NEW: hook to CombatManager
	[Export] public NodePath EnergyPath { get; set; }

	private Deck[] _decks;
	private Control _enemiesRoot;
	private Enemy[] _enemies;
	private PlayerUnit[] _players;
	private CombatManager _combat;                      // NEW
	private EnergyManager _energy;

	private enum TargetingMode
	{
		SingleEnemy,
		AllEnemies,
		SinglePlayer,
		SelfPlayer,
		AllPlayers
	}

	private bool _targeting = false;
	private CardData _pendingCard;
	private Deck _pendingDeck;
	private CanvasLayer _targetingVisualLayer;
	private TargetingArrowOverlay _targetingArrow;
	private Enemy _lockedTarget;
	private PlayerUnit _lockedPlayerTarget;
	private TargetingMode _targetingMode;
	private bool _allEnemiesGroupTargetingActive;
	private bool _allPlayersGroupTargetingActive;
	private int _controllerPlayerFocusIndex = -1;
	private int _controllerTargetFocusIndex = -1;
	private ulong _nextControllerMoveAtMs;
	private ulong _lastControlSchemeRevision;
	private bool _controllerTargeting;
	private const float ControllerAxisThreshold = 0.55f;
	private const ulong ControllerMoveCooldownMs = 180;
	private const int TargetingArrowCanvasLayer = 1990;
	public bool IsTargetingActive => _targeting;

	private sealed class ControllerFocusEntry
	{
		public PlayerUnit Player;
		public Enemy Enemy;
		public Deck Deck;
		public float X;
		public bool IsPlayer => Player != null;
	}

	public override void _Ready()
	{
		_combat = GetNodeOrNull<CombatManager>(CombatPath);
		_energy = GetNodeOrNull<EnergyManager>(EnergyPath);
		GD.Print($"CombatTargeting ready | combat? {(_combat != null)}");

		RefreshBindings();
		SetProcess(true);
	}

	public void RefreshBindings()
	{
		if (_decks != null)
		{
			foreach (Deck deck in _decks)
			{
				if (deck == null || !GodotObject.IsInstanceValid(deck))
					continue;
				deck.PlayRequested -= OnPlayRequested;
				deck.Shuffled -= OnDeckShuffled;
			}
		}

		_enemiesRoot = GetNode<Control>(EnemiesPath);
		_enemies = _enemiesRoot.GetChildren().OfType<Enemy>().Where(e => e.Visible).ToArray();

		// make sure rings start hidden
		foreach (var e in _enemies)
			e?.SetTargetable(false);

		var tmp = new System.Collections.Generic.List<Deck>();
		foreach (var p in DeckPaths)
		{
			var d = GetNodeOrNull<Deck>(p);
			if (d == null) continue;
			d.DiscardOnTopClick = false;     // we discard here after resolving
			d.PlayRequested += OnPlayRequested;
			d.Shuffled += OnDeckShuffled;
			tmp.Add(d);
		}
		_decks = tmp.ToArray();
		_players = _decks.Select(GetDeckOwner).OfType<PlayerUnit>().Distinct().ToArray();
	}

	public override void _Process(double delta)
	{
		if (_lastControlSchemeRevision != ControlSchemeFocus.Revision)
		{
			_lastControlSchemeRevision = ControlSchemeFocus.Revision;
			if (!ControlSchemeFocus.IsXboxFocused)
			{
				ClearControllerCardFocus();
				_controllerTargeting = false;
			}
		}

		if (!_targeting)
		{
			UpdateControllerCardFocus();
			return;
		}

		if (!_targeting)
			return;

		UpdateTargetingArrow();
	}

	private async void OnPlayRequested(Deck deck, CardData card)
	{
		GD.Print("OnPlayRequested");
		if (_targeting || card == null) return;

		if (IsAllEnemiesAttack(card))
		{
			if (!CanSpendCardEnergy()) return;
			_pendingCard = card;
			_pendingDeck = deck;
			BeginTargeting(TargetingMode.AllEnemies);
			return;
		}

		if (IsSingleTargetAttack(card))
		{
			if (!CanSpendCardEnergy()) return;
			_pendingCard = card;
			_pendingDeck = deck;
			BeginTargeting(TargetingMode.SingleEnemy);
		}
		else if (IsSelfPlayerCast(card))
		{
			if (!CanSpendCardEnergy()) return;
			_pendingCard = card;
			_pendingDeck = deck;
			BeginTargeting(TargetingMode.SelfPlayer);
		}
		else if (IsSinglePlayerCast(card))
		{
			if (!CanSpendCardEnergy()) return;
			_pendingCard = card;
			_pendingDeck = deck;
			BeginTargeting(TargetingMode.SinglePlayer);
		}
		else if (IsAllPlayersCast(card))
		{
			if (!CanSpendCardEnergy()) return;
			_pendingCard = card;
			_pendingDeck = deck;
			BeginTargeting(TargetingMode.AllPlayers);
		}
		else
		{
			if (TrySpendCardEnergy())
			{
				if (_combat != null) await _combat.PlayCardAuto(deck, card, Deck.DeckSide.Player, GetDeckOwner(deck));
				else await deck.AdvanceTopToDiscardWithPresentation(card);
			}
		}
	}

	private bool IsSingleTargetAttack(CardData c)
	{
		var tid = (c.TargetDef as TargetDef)?.Id;
		return tid == "single" && HasHarmfulEffect(c);
	}

	private bool IsAllEnemiesAttack(CardData c)
	{
		var tid = (c.TargetDef as TargetDef)?.Id;
		return (tid == "multiple" || tid == "all_enemies" || tid == "all") && HasHarmfulEffect(c);
	}

	private bool IsSelfPlayerCast(CardData c)
	{
		var tid = (c.TargetDef as TargetDef)?.Id;
		return tid == "self" && HasBeneficialEffect(c) && !HasHarmfulEffect(c);
	}

	private bool IsAllPlayersCast(CardData c)
	{
		var tid = (c.TargetDef as TargetDef)?.Id;
		return (tid == "all" || tid == "all_allies") && HasBeneficialEffect(c) && !HasHarmfulEffect(c);
	}

	private bool IsSinglePlayerCast(CardData c)
	{
		var tid = (c.TargetDef as TargetDef)?.Id;
		return tid == "ally" && HasBeneficialEffect(c) && !HasHarmfulEffect(c);
	}

	private bool HasBeneficialEffect(CardData c)
		=> c?.Effects != null && c.Effects.Any(e => e?.Def?.Id is "block" or "heal" or "regen" or "protect" or "play_top_cards" or "strategist");

	private bool HasHarmfulEffect(CardData c)
		=> c?.Effects != null && c.Effects.Any(e => e?.Def?.Id is "attack" or "body_slam" or "bleed" or "weak");

	private int GetAttackAmount(CardData c)
		=> c.Effects.Where(e => e?.Def?.Id == "attack").Select(e => e.Amount).FirstOrDefault();

	private void BeginTargeting(TargetingMode mode)
	{
		GD.Print("BeginTargeting");
		_targeting = true;
		_targetingMode = mode;
		_controllerTargeting = IsControllerCardFocusActive();
		ClearControllerCardFocus();
		_combat?.RefreshEnemyIntents();
		_lockedTarget = null;
		_lockedPlayerTarget = null;
		_controllerTargetFocusIndex = -1;
		SetAllEnemiesGroupTargeting(false);
		SetAllPlayersGroupTargeting(false);
		foreach (var e in _enemies)
		{
			if (e == null || !IsInstanceValid(e) || !e.Alive) continue;
			bool singleTarget = mode == TargetingMode.SingleEnemy;
			e.SetTargetable(singleTarget);
			if (singleTarget)
				e.Clicked += OnEnemyClicked;
		}

		PlayerUnit selfTarget = GetDeckOwner(_pendingDeck) as PlayerUnit;
		foreach (var player in _players)
		{
			if (player == null || !IsInstanceValid(player) || !player.Alive)
				continue;

			bool singleFriendlyTarget = mode == TargetingMode.SelfPlayer && player == selfTarget;
			bool allyTarget = mode == TargetingMode.SinglePlayer && player != selfTarget;
			player.SetFriendlyTargetable(singleFriendlyTarget || allyTarget);
		}

		ApplyTargetingVisuals(true);
		if (_controllerTargeting)
			FocusDefaultControllerTarget();
		SetProcess(true);
		UpdateTargetingArrow();
	}

	private void EndTargeting()
	{
		ApplyTargetingVisuals(false);
		foreach (var e in _enemies)
		{
			if (e == null || !IsInstanceValid(e)) continue;
			e.SetTargetable(false);
			e.SetGroupTargetable(false);
			e.Clicked -= OnEnemyClicked;
		}
		foreach (var player in _players)
		{
			if (player == null || !IsInstanceValid(player))
				continue;

			player.SetFriendlyTargetable(false);
			player.SetGroupFriendlyTargetable(false);
		}
		_pendingCard = null;
		_pendingDeck = null;
		_targeting = false;
		_controllerTargeting = false;
		_combat?.RefreshEnemyIntents();
		_lockedTarget = null;
		_lockedPlayerTarget = null;
		_targetingArrow?.ClearArrow();
	}

	private void CancelTargeting()
	{
		_pendingDeck?.ClearPendingVisibleCardSelection();
		EndTargeting();
	}

	private async void OnEnemyClicked(Enemy who)
	{
		if (!_targeting || _targetingMode != TargetingMode.SingleEnemy || _pendingDeck == null || _pendingCard == null) return;

		GD.Print($"OnEnemyClicked -> {who?.Name} | combat? {(_combat != null)}");

		var deck = _pendingDeck;     // snapshot before clearing
		var card = _pendingCard;
		EndTargeting();            // hide rings / detach signals
		if (!TrySpendCardEnergy()) return;
		if (_combat != null) await _combat.PlayCardOnTarget(deck, card, Deck.DeckSide.Player, who, GetDeckOwner(deck));
		else await deck.AdvanceTopToDiscardWithPresentation(card);
	}

	private void OnDeckShuffled(Deck deck, CardData newTop)
	{
		if (_targeting || deck == null || newTop == null || !newTop.PlayTopCardOnShuffle)
			return;

		OnPlayRequested(deck, newTop);
	}

	private IDamageable GetDeckOwner(Deck deck)
		=> deck?.GetParent() as IDamageable;

	private bool CanSpendCardEnergy()
		=> _energy == null || _energy.CanSpend(_energy.CardEnergyCost);

	private bool TrySpendCardEnergy()
		=> _energy == null || _energy.TrySpend(_energy.CardEnergyCost);

	private void ApplyTargetingVisuals(bool enabled)
	{
		if (_decks != null)
		{
			foreach (var deck in _decks)
			{
				if (deck == null || !IsInstanceValid(deck))
					continue;

				deck.SetTargetingDimmed(enabled && deck != _pendingDeck);
				deck.SetTopCardTargetingFocus(enabled && deck == _pendingDeck);
				deck.SetTopCardTooltipSuppressed(enabled && deck != _pendingDeck);
			}
		}

		if (_enemies != null)
		{
			foreach (var enemy in _enemies)
			{
				if (enemy == null || !IsInstanceValid(enemy))
					continue;

				enemy.SetDeckTargetingDimmed(enabled);
				enemy.SetDeckTooltipSuppressed(enabled);
			}
		}

		if (enabled)
			GetTargetingArrow();
	}

	private void UpdateTargetingArrow()
	{
		if (_pendingDeck == null || !IsInstanceValid(_pendingDeck))
			return;

		var arrow = GetTargetingArrow();
		if (arrow == null)
			return;

		if (_targetingMode == TargetingMode.AllEnemies)
		{
			Vector2 allMouse = GetViewport().GetMousePosition();
			bool active = _controllerTargeting ? HasAliveEnemyTargets() : IsActiveAllEnemiesTargetZone(allMouse);
			SetAllEnemiesGroupTargeting(active);
			Vector2 allEndPoint = active ? GetAllEnemiesTargetAnchor() : allMouse;
			arrow.SetAllEnemiesZone(GetAllEnemiesZoneStartX(), active);
			arrow.SetArrow(_pendingDeck.GetTopCardCanvasRect(), allEndPoint, active);
			return;
		}

		if (_targetingMode == TargetingMode.AllPlayers)
		{
			Vector2 allMouse = GetViewport().GetMousePosition();
			bool active = _controllerTargeting ? HasAlivePlayerTargets() : IsActiveAllPlayersTargetZone(allMouse);
			SetAllPlayersGroupTargeting(active);
			Vector2 allEndPoint = active ? GetAllPlayersTargetAnchor() : allMouse;
			arrow.SetAllAlliesZone(GetAllPlayersZoneStartX(), active);
			arrow.SetArrow(_pendingDeck.GetTopCardCanvasRect(), allEndPoint, active, friendly: true);
			return;
		}

		arrow.ClearAllEnemiesZone();
		SetAllEnemiesGroupTargeting(false);
		SetAllPlayersGroupTargeting(false);
		Vector2 mouse = GetViewport().GetMousePosition();
		if (_targetingMode == TargetingMode.SelfPlayer || _targetingMode == TargetingMode.SinglePlayer)
		{
			if (!_controllerTargeting && (_lockedPlayerTarget == null || !IsInstanceValid(_lockedPlayerTarget) || !_lockedPlayerTarget.Alive || !_lockedPlayerTarget.GetTargetingCanvasRect().HasPoint(mouse)))
				_lockedPlayerTarget = GetFriendlyTargetUnderMouse(mouse);
			ApplyFriendlyTargetHover(_lockedPlayerTarget);
			Vector2 playerEndPoint = _lockedPlayerTarget != null ? _lockedPlayerTarget.GetTargetingAnchorCanvas() : mouse;
			arrow.SetArrow(_pendingDeck.GetTopCardCanvasRect(), playerEndPoint, _lockedPlayerTarget != null, friendly: true);
			return;
		}

		ApplyFriendlyTargetHover(null);

		if (!_controllerTargeting && (_lockedTarget == null || !IsInstanceValid(_lockedTarget) || !_lockedTarget.Alive || !_lockedTarget.GetTargetingCanvasRect().HasPoint(mouse)))
			_lockedTarget = GetTargetUnderMouse(mouse);
		Vector2 endPoint = _lockedTarget != null ? _lockedTarget.GetTargetingAnchorCanvas() : mouse;
		arrow.SetArrow(_pendingDeck.GetTopCardCanvasRect(), endPoint, _lockedTarget != null);
	}

	private Enemy GetTargetUnderMouse(Vector2 mouse)
	{
		foreach (var enemy in _enemies)
		{
			if (enemy == null || !IsInstanceValid(enemy) || !enemy.Alive)
				continue;

			if (enemy.GetTargetingCanvasRect().HasPoint(mouse))
				return enemy;
		}

		return null;
	}

	private PlayerUnit GetFriendlyTargetUnderMouse(Vector2 mouse)
	{
		PlayerUnit owner = GetDeckOwner(_pendingDeck) as PlayerUnit;
		if (_players == null)
			return null;

		foreach (var player in _players)
		{
			if (player == null || !IsInstanceValid(player) || !player.Alive)
				continue;

			if (_targetingMode == TargetingMode.SelfPlayer && player != owner)
				continue;
			if (_targetingMode == TargetingMode.SinglePlayer && player == owner)
				continue;

			if (player.GetTargetingCanvasRect().HasPoint(mouse))
				return player;
		}

		return null;
	}

	private void ApplyFriendlyTargetHover(PlayerUnit hovered)
	{
		if (_players == null)
			return;

		foreach (var player in _players)
		{
			if (player == null || !IsInstanceValid(player))
				continue;

			player.SetFriendlyTargetHover(player == hovered);
		}
	}

	private TargetingArrowOverlay GetTargetingArrow()
	{
		if (_targetingArrow != null && IsInstanceValid(_targetingArrow))
		{
			if (_targetingVisualLayer != null && IsInstanceValid(_targetingVisualLayer))
				_targetingVisualLayer.Layer = TargetingArrowCanvasLayer;
			return _targetingArrow;
		}

		_targetingVisualLayer = new CanvasLayer { Layer = TargetingArrowCanvasLayer };
		_targetingArrow = new TargetingArrowOverlay
		{
			Name = "TargetingArrowOverlay",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Size = GetViewport().GetVisibleRect().Size
		};
		_targetingArrow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_targetingVisualLayer.AddChild(_targetingArrow);
		(GetTree().CurrentScene as Node ?? GetTree().Root).AddChild(_targetingVisualLayer);
		return _targetingArrow;
	}

	private bool IsInAllEnemiesTargetZone(Vector2 mouse)
	{
		return mouse.X >= GetAllEnemiesZoneStartX();
	}

	private bool IsInAllPlayersTargetZone(Vector2 mouse)
	{
		return mouse.X <= GetAllPlayersZoneStartX();
	}

	private bool IsActiveAllEnemiesTargetZone(Vector2 mouse)
	{
		return IsInAllEnemiesTargetZone(mouse) && HasAliveEnemyTargets();
	}

	private bool IsActiveAllPlayersTargetZone(Vector2 mouse)
	{
		return IsInAllPlayersTargetZone(mouse) && HasAlivePlayerTargets();
	}

	private bool HasAliveEnemyTargets()
		=> _enemies != null && _enemies.Any(enemy => enemy != null && IsInstanceValid(enemy) && enemy.Alive);

	private bool HasAlivePlayerTargets()
		=> _players != null && _players.Any(player => player != null && IsInstanceValid(player) && player.Alive);

	private void SetAllEnemiesGroupTargeting(bool active)
	{
		if (_allEnemiesGroupTargetingActive == active)
			return;

		_allEnemiesGroupTargetingActive = active;
		if (_enemies == null)
			return;

		foreach (var enemy in _enemies)
		{
			if (enemy == null || !IsInstanceValid(enemy))
				continue;

			enemy.SetGroupTargetable(active && enemy.Alive);
		}
	}

	private void SetAllPlayersGroupTargeting(bool active)
	{
		if (_allPlayersGroupTargetingActive == active)
			return;

		_allPlayersGroupTargetingActive = active;
		if (_players == null)
			return;

		foreach (var player in _players)
		{
			if (player == null || !IsInstanceValid(player))
				continue;

			player.SetGroupFriendlyTargetable(active && player.Alive);
		}
	}

	private float GetAllEnemiesZoneStartX()
	{
		var viewport = GetViewport().GetVisibleRect();
		return Mathf.Round(viewport.Size.X * 0.6f);
	}

	private float GetAllPlayersZoneStartX()
	{
		var viewport = GetViewport().GetVisibleRect();
		return Mathf.Round(viewport.Size.X * 0.4f);
	}

	private Vector2 GetAllEnemiesTargetAnchor()
	{
		var viewport = GetViewport().GetVisibleRect();
		return new Vector2(GetAllEnemiesZoneStartX(), viewport.Size.Y * 0.5f).Floor();
	}

	private Vector2 GetAllPlayersTargetAnchor()
	{
		var viewport = GetViewport().GetVisibleRect();
		return new Vector2(GetAllPlayersZoneStartX(), viewport.Size.Y * 0.5f).Floor();
	}

	private async void ConfirmAllEnemiesTarget()
	{
		if (!_targeting || _targetingMode != TargetingMode.AllEnemies || _pendingDeck == null || _pendingCard == null)
			return;

		var deck = _pendingDeck;
		var card = _pendingCard;
		EndTargeting();
		if (!TrySpendCardEnergy()) return;
		if (_combat != null) await _combat.PlayCardAuto(deck, card, Deck.DeckSide.Player, GetDeckOwner(deck));
		else GD.PushWarning("CombatManager missing; AoE skipped.");
	}

	private async void ConfirmAllPlayersTarget()
	{
		if (!_targeting || _targetingMode != TargetingMode.AllPlayers || _pendingDeck == null || _pendingCard == null)
			return;

		var deck = _pendingDeck;
		var card = _pendingCard;
		EndTargeting();
		if (!TrySpendCardEnergy()) return;
		if (_combat != null) await _combat.PlayCardAuto(deck, card, Deck.DeckSide.Player, GetDeckOwner(deck));
		else await deck.AdvanceTopToDiscardWithPresentation(card);
	}

	private async void ConfirmSelfTarget()
	{
		if (!_targeting || _targetingMode != TargetingMode.SelfPlayer || _pendingDeck == null || _pendingCard == null)
			return;

		var deck = _pendingDeck;
		var card = _pendingCard;
		EndTargeting();
		if (!TrySpendCardEnergy()) return;
		if (_combat != null) await _combat.PlayCardAuto(deck, card, Deck.DeckSide.Player, GetDeckOwner(deck));
		else await deck.AdvanceTopToDiscardWithPresentation(card);
	}

	private async void ConfirmPlayerTarget(PlayerUnit target)
	{
		if (!_targeting || _targetingMode != TargetingMode.SinglePlayer || _pendingDeck == null || _pendingCard == null || target == null)
			return;

		var deck = _pendingDeck;
		var card = _pendingCard;
		EndTargeting();
		if (!TrySpendCardEnergy()) return;
		if (_combat != null) await _combat.PlayCardOnTarget(deck, card, Deck.DeckSide.Player, target, GetDeckOwner(deck));
		else await deck.AdvanceTopToDiscardWithPresentation(card);
	}

	public override void _Input(InputEvent e)
	{
		ControlSchemeFocus.UpdateFromInput(e);

		if (Deck.IsDrawPileModalOpen || !_targeting)
			return;

		if (e is not InputEventMouseButton mb || !mb.Pressed)
			return;

		if (mb.ButtonIndex == MouseButton.Right)
		{
			CancelTargeting();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mb.ButtonIndex != MouseButton.Left)
			return;

		if (_targetingMode == TargetingMode.AllEnemies && IsActiveAllEnemiesTargetZone(mb.Position))
		{
			ConfirmAllEnemiesTarget();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_targetingMode == TargetingMode.AllPlayers && IsActiveAllPlayersTargetZone(mb.Position))
		{
			ConfirmAllPlayersTarget();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_targetingMode == TargetingMode.SinglePlayer)
		{
			var target = GetFriendlyTargetUnderMouse(mb.Position);
			if (target != null)
			{
				ConfirmPlayerTarget(target);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (_targetingMode == TargetingMode.SingleEnemy && GetTargetUnderMouse(mb.Position) != null)
			return;

		if (_targetingMode == TargetingMode.SelfPlayer && GetFriendlyTargetUnderMouse(mb.Position) != null)
		{
			ConfirmSelfTarget();
			GetViewport().SetInputAsHandled();
			return;
		}

		CancelTargeting();
		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		ControlSchemeFocus.UpdateFromInput(e);

		if (Deck.IsDrawPileModalOpen)
		{
			if (IsControllerCancelPressed(e))
			{
				foreach (var deck in _decks)
					deck?.CloseOpenPileModal();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (_targeting && e.IsActionPressed("ui_cancel"))
		{
			CancelTargeting(); // cancel: keep top card
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_targeting)
		{
			if (ControlSchemeFocus.IsXboxFocused && !_controllerTargeting)
			{
				_controllerTargeting = true;
				FocusDefaultControllerTarget();
			}

			if (TryGetControllerHorizontalMove(e, out int targetDirection))
			{
				MoveControllerTargetFocus(targetDirection);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (IsControllerAcceptPressed(e))
			{
				ConfirmControllerTarget();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (IsControllerCancelPressed(e))
			{
				CancelTargeting();
				GetViewport().SetInputAsHandled();
				return;
			}

			return;
		}

		if (ControlSchemeFocus.IsXboxFocused && !IsControllerCardFocusActive())
			UpdateControllerCardFocus();

		if (TryGetControllerHorizontalMove(e, out int direction))
		{
			MoveControllerCardFocus(direction);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (TryGetControllerVerticalMove(e, out int cardDirection))
		{
			GetFocusedControllerDeck()?.TryMoveControllerCardSelection(cardDirection);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (IsControllerAcceptPressed(e))
		{
			GetFocusedControllerDeck()?.TryRequestControllerPlay();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (IsControllerDrawPilePressed(e))
		{
			GetFocusedControllerDeck()?.OpenDrawPileModal();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (IsControllerDiscardPilePressed(e))
		{
			GetFocusedControllerDeck()?.OpenDiscardPileModal();
			GetViewport().SetInputAsHandled();
		}
	}

	private void UpdateControllerCardFocus()
	{
		if (!ControlSchemeFocus.IsXboxFocused)
		{
			ClearControllerCardFocus();
			return;
		}

		if (!IsPlayerTurn())
		{
			ClearControllerCardFocus();
			return;
		}

		var focusable = GetControllerFocusableEntries();
		if (focusable.Length == 0)
		{
			ClearControllerCardFocus();
			return;
		}

		if (_controllerPlayerFocusIndex < 0 || _controllerPlayerFocusIndex >= focusable.Length)
			_controllerPlayerFocusIndex = 0;

		ApplyControllerCardFocus(focusable);
	}

	private void MoveControllerCardFocus(int direction)
	{
		if (!ControlSchemeFocus.IsXboxFocused)
			return;

		var focusable = GetControllerFocusableEntries();
		if (focusable.Length == 0)
			return;

		if (_controllerPlayerFocusIndex < 0 || _controllerPlayerFocusIndex >= focusable.Length)
			_controllerPlayerFocusIndex = 0;
		else
			_controllerPlayerFocusIndex = PosMod(_controllerPlayerFocusIndex + direction, focusable.Length);

		ApplyControllerCardFocus(focusable);
	}

	private void ApplyControllerCardFocus(ControllerFocusEntry[] focusable)
	{
		ControllerFocusEntry focused = _controllerPlayerFocusIndex >= 0 && _controllerPlayerFocusIndex < focusable.Length ? focusable[_controllerPlayerFocusIndex] : null;
		foreach (var player in _players ?? System.Array.Empty<PlayerUnit>())
		{
			if (player == null || !IsInstanceValid(player))
				continue;

			Deck deck = player.GetDeck();
			bool canFocus = focusable.Any(entry => entry.Player == player);
			bool active = focused?.Player == player;
			player.SetControllerFocus(active);
			deck?.SetControllerFocus(active);
			deck?.SetControllerUnfocused(focused != null && canFocus && !active);
		}

		foreach (var enemy in _enemies ?? System.Array.Empty<Enemy>())
		{
			if (enemy == null || !IsInstanceValid(enemy))
				continue;

			Deck deck = enemy.GetDeck();
			bool canFocus = focusable.Any(entry => entry.Enemy == enemy);
			bool active = focused?.Enemy == enemy;
			enemy.SetControllerFocus(active);
			deck?.SetControllerUnfocused(focused != null && canFocus && !active);
		}
	}

	private void ClearControllerCardFocus()
	{
		_controllerPlayerFocusIndex = -1;
		foreach (var player in _players ?? System.Array.Empty<PlayerUnit>())
		{
			if (player == null || !IsInstanceValid(player))
				continue;

			player.SetControllerFocus(false);
			player.GetDeck()?.SetControllerFocus(false);
			player.GetDeck()?.SetControllerUnfocused(false);
		}

		foreach (var enemy in _enemies ?? System.Array.Empty<Enemy>())
		{
			if (enemy == null || !IsInstanceValid(enemy))
				continue;

			enemy.SetControllerFocus(false);
			enemy.GetDeck()?.SetControllerFocus(false);
			enemy.GetDeck()?.SetControllerUnfocused(false);
		}
	}

	private ControllerFocusEntry[] GetControllerFocusableEntries()
	{
		var entries = new System.Collections.Generic.List<ControllerFocusEntry>();
		foreach (var player in _players ?? System.Array.Empty<PlayerUnit>())
		{
			Deck deck = player?.GetDeck();
			if (player == null || !IsInstanceValid(player) || !player.Alive || deck?.CanControllerInspect() != true)
				continue;

			entries.Add(new ControllerFocusEntry
			{
				Player = player,
				Deck = deck,
				X = player.GetDeckStackAnchorGlobal().X
			});
		}

		foreach (var enemy in _enemies ?? System.Array.Empty<Enemy>())
		{
			Deck deck = enemy?.GetDeck();
			if (enemy == null || !IsInstanceValid(enemy) || !enemy.Alive || deck?.CanControllerInspect() != true)
				continue;

			entries.Add(new ControllerFocusEntry
			{
				Enemy = enemy,
				Deck = deck,
				X = enemy.GetDeckStackAnchorGlobal().X
			});
		}

		return entries
			.OrderBy(entry => entry.X)
			.ThenBy(entry => entry.IsPlayer ? 0 : 1)
			.ToArray();
	}

	private bool IsControllerCardFocusActive()
		=> _controllerPlayerFocusIndex >= 0 && GetFocusedControllerDeck() != null;

	private Deck GetFocusedControllerDeck()
	{
		var focusable = GetControllerFocusableEntries();
		if (_controllerPlayerFocusIndex < 0 || _controllerPlayerFocusIndex >= focusable.Length)
			return null;

		return focusable[_controllerPlayerFocusIndex].Deck;
	}

	private bool IsPlayerTurn()
		=> _energy == null || _energy.IsPlayerTurn;

	private void FocusDefaultControllerTarget()
	{
		_controllerTargetFocusIndex = 0;
		ApplyControllerTargetFocus();
	}

	private void MoveControllerTargetFocus(int direction)
	{
		if (!_controllerTargeting)
			_controllerTargeting = true;

		var count = GetControllerTargetCount();
		if (count <= 0)
			return;

		if (_controllerTargetFocusIndex < 0 || _controllerTargetFocusIndex >= count)
			_controllerTargetFocusIndex = 0;
		else
			_controllerTargetFocusIndex = PosMod(_controllerTargetFocusIndex + direction, count);

		ApplyControllerTargetFocus();
	}

	private int GetControllerTargetCount()
	{
		return _targetingMode switch
		{
			TargetingMode.SingleEnemy => GetControllerEnemyTargets().Length,
			TargetingMode.SinglePlayer or TargetingMode.SelfPlayer => GetControllerPlayerTargets().Length,
			_ => 1
		};
	}

	private void ApplyControllerTargetFocus()
	{
		if (_targetingMode == TargetingMode.SingleEnemy)
		{
			var targets = GetControllerEnemyTargets();
			_lockedTarget = targets.Length > 0 ? targets[Mathf.Clamp(_controllerTargetFocusIndex, 0, targets.Length - 1)] : null;
			UpdateTargetingArrow();
			return;
		}

		if (_targetingMode == TargetingMode.SinglePlayer || _targetingMode == TargetingMode.SelfPlayer)
		{
			var targets = GetControllerPlayerTargets();
			_lockedPlayerTarget = targets.Length > 0 ? targets[Mathf.Clamp(_controllerTargetFocusIndex, 0, targets.Length - 1)] : null;
			UpdateTargetingArrow();
			return;
		}

		UpdateTargetingArrow();
	}

	private Enemy[] GetControllerEnemyTargets()
		=> (_enemies ?? System.Array.Empty<Enemy>())
			.Where(enemy => enemy != null && IsInstanceValid(enemy) && enemy.Alive)
			.OrderBy(enemy => enemy.GetTargetingAnchorCanvas().X)
			.ToArray();

	private PlayerUnit[] GetControllerPlayerTargets()
	{
		PlayerUnit owner = GetDeckOwner(_pendingDeck) as PlayerUnit;
		return (_players ?? System.Array.Empty<PlayerUnit>())
			.Where(player => player != null && IsInstanceValid(player) && player.Alive)
			.Where(player => _targetingMode != TargetingMode.SelfPlayer || player == owner)
			.Where(player => _targetingMode != TargetingMode.SinglePlayer || player != owner)
			.OrderBy(player => player.GetTargetingAnchorCanvas().X)
			.ToArray();
	}

	private void ConfirmControllerTarget()
	{
		if (!_targeting)
			return;

		if (_targetingMode == TargetingMode.AllEnemies)
			ConfirmAllEnemiesTarget();
		else if (_targetingMode == TargetingMode.AllPlayers)
			ConfirmAllPlayersTarget();
		else if (_targetingMode == TargetingMode.SelfPlayer)
			ConfirmSelfTarget();
		else if (_targetingMode == TargetingMode.SinglePlayer && _lockedPlayerTarget != null)
			ConfirmPlayerTarget(_lockedPlayerTarget);
		else if (_targetingMode == TargetingMode.SingleEnemy && _lockedTarget != null)
			OnEnemyClicked(_lockedTarget);
	}

	private bool TryGetControllerHorizontalMove(InputEvent e, out int direction)
	{
		direction = 0;

		if (e is InputEventJoypadButton button && button.Pressed)
		{
			if (button.ButtonIndex == JoyButton.DpadLeft)
				direction = -1;
			else if (button.ButtonIndex == JoyButton.DpadRight)
				direction = 1;
		}
		else if (e is InputEventJoypadMotion motion && motion.Axis == JoyAxis.LeftX && Mathf.Abs(motion.AxisValue) >= ControllerAxisThreshold)
		{
			ulong now = Time.GetTicksMsec();
			if (now < _nextControllerMoveAtMs)
				return false;

			_nextControllerMoveAtMs = now + ControllerMoveCooldownMs;
			direction = motion.AxisValue < 0f ? -1 : 1;
		}

		return direction != 0;
	}

	private bool TryGetControllerVerticalMove(InputEvent e, out int direction)
	{
		direction = 0;

		if (e is InputEventJoypadButton button && button.Pressed)
		{
			if (button.ButtonIndex == JoyButton.DpadUp)
				direction = -1;
			else if (button.ButtonIndex == JoyButton.DpadDown)
				direction = 1;
		}
		else if (e is InputEventJoypadMotion motion && motion.Axis == JoyAxis.LeftY && Mathf.Abs(motion.AxisValue) >= ControllerAxisThreshold)
		{
			ulong now = Time.GetTicksMsec();
			if (now < _nextControllerMoveAtMs)
				return false;

			_nextControllerMoveAtMs = now + ControllerMoveCooldownMs;
			direction = motion.AxisValue < 0f ? -1 : 1;
		}

		return direction != 0;
	}

	private bool IsControllerAcceptPressed(InputEvent e)
		=> e is InputEventJoypadButton button && button.Pressed && button.ButtonIndex == JoyButton.A;

	private bool IsControllerCancelPressed(InputEvent e)
		=> e is InputEventJoypadButton button && button.Pressed && button.ButtonIndex == JoyButton.B;

	private bool IsControllerDrawPilePressed(InputEvent e)
		=> IsControllerTriggerPressed(e, JoyAxis.TriggerLeft);

	private bool IsControllerDiscardPilePressed(InputEvent e)
		=> IsControllerTriggerPressed(e, JoyAxis.TriggerRight);

	private bool IsControllerTriggerPressed(InputEvent e, JoyAxis axis)
		=> e is InputEventJoypadMotion motion && motion.Axis == axis && motion.AxisValue >= ControllerAxisThreshold;

	private int PosMod(int value, int length)
		=> length <= 0 ? 0 : ((value % length) + length) % length;
}

public partial class TargetingArrowOverlay : Control
{
	private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.78f);
	private static readonly Color AimColor = new(0.94f, 0.67f, 0.14f, 1f);
	private static readonly Color LockedColor = new(1f, 0.96f, 0.68f, 1f);
	private static readonly Color ZoneBaseColor = new(0.9f, 0.2f, 0.2f, 1f);
	private static readonly Color FriendlyAimColor = new(0.24f, 0.72f, 1f, 1f);
	private static readonly Color FriendlyLockedColor = new(0.65f, 0.92f, 1f, 1f);
	private static readonly Color FriendlyZoneBaseColor = new(0.16f, 0.56f, 1f, 1f);

	private Rect2 _cardRect;
	private Vector2 _endPoint;
	private bool _hasArrow;
	private bool _locked;
	private bool _friendlyArrow;
	private bool _hasAllEnemiesZone;
	private bool _allEnemiesZoneActive;
	private bool _allEnemiesZoneFriendly;
	private float _allEnemiesZoneStartX;

	public void SetArrow(Rect2 cardRect, Vector2 endPoint, bool locked, bool friendly = false)
	{
		_cardRect = cardRect;
		_endPoint = endPoint;
		_locked = locked;
		_friendlyArrow = friendly;
		_hasArrow = cardRect.Size.X > 1f && cardRect.Size.Y > 1f;
		Size = GetViewport().GetVisibleRect().Size;
		QueueRedraw();
	}

	public void ClearArrow()
	{
		_hasArrow = false;
		_hasAllEnemiesZone = false;
		QueueRedraw();
	}

	public void SetAllEnemiesZone(float startX, bool active)
	{
		SetAllTargetZone(startX, active, friendly: false);
	}

	public void SetAllAlliesZone(float startX, bool active)
	{
		SetAllTargetZone(startX, active, friendly: true);
	}

	private void SetAllTargetZone(float startX, bool active, bool friendly)
	{
		_allEnemiesZoneStartX = startX;
		_allEnemiesZoneActive = active;
		_allEnemiesZoneFriendly = friendly;
		_hasAllEnemiesZone = true;
		QueueRedraw();
	}

	public void ClearAllEnemiesZone()
	{
		if (!_hasAllEnemiesZone)
			return;

		_hasAllEnemiesZone = false;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_hasAllEnemiesZone)
			DrawAllEnemiesZoneLine();

		if (!_hasArrow)
			return;

		Vector2 start = GetRectEdgePoint(_cardRect, _endPoint);
		Vector2 delta = _endPoint - start;
		if (delta.LengthSquared() < 64f)
			return;

		Vector2 dir = delta.Normalized();
		Vector2 end = _endPoint - dir * 7f;
		Vector2 shadowOffset = new(1f, 1f);
		Color lineColor = _friendlyArrow
			? (_locked ? FriendlyLockedColor : FriendlyAimColor)
			: (_locked ? LockedColor : AimColor);

		DrawLine(start + shadowOffset, end + shadowOffset, ShadowColor, 5f, false);
		DrawLine(start, end, lineColor, _locked ? 4f : 3f, false);

		DrawArrowHead(end, dir, lineColor, shadowOffset);
	}

	private void DrawAllEnemiesZoneLine()
	{
		var viewport = GetViewport().GetVisibleRect();
		float pulse = (Mathf.Sin(Time.GetTicksMsec() / 1000f * 5.5f) + 1f) * 0.5f;
		float alpha = _allEnemiesZoneActive ? 0.82f + pulse * 0.18f : 0.45f + pulse * 0.16f;
		float width = _allEnemiesZoneActive ? 4f + pulse * 3f : 3f + pulse * 1.5f;
		Color baseColor = _allEnemiesZoneFriendly ? FriendlyZoneBaseColor : ZoneBaseColor;
		Color color = _allEnemiesZoneActive ? baseColor.Lightened(0.18f) : baseColor;
		color.A = alpha;
		Color shadow = ShadowColor;
		shadow.A = alpha * 0.65f;
		Vector2 from = new(_allEnemiesZoneStartX, 0f);
		Vector2 to = new(_allEnemiesZoneStartX, viewport.Size.Y);

		DrawLine(from + new Vector2(1f, 0f), to + new Vector2(1f, 0f), shadow, width + 2f, false);
		DrawLine(from, to, color, width, false);
	}

	private void DrawArrowHead(Vector2 tip, Vector2 dir, Color color, Vector2 shadowOffset)
	{
		Vector2 perp = new(-dir.Y, dir.X);
		float length = _locked ? 17f : 14f;
		float halfWidth = _locked ? 8f : 6f;
		Vector2 back = tip - dir * length;

		Vector2[] points =
		{
			tip,
			back + perp * halfWidth,
			back - perp * halfWidth
		};

		Vector2[] shadowPoints =
		{
			points[0] + shadowOffset,
			points[1] + shadowOffset,
			points[2] + shadowOffset
		};

		DrawPolygon(shadowPoints, new[] { ShadowColor, ShadowColor, ShadowColor });
		DrawPolygon(points, new[] { color, color, color });
		DrawPolyline(new[] { points[0], points[1], points[2], points[0] }, Colors.Black, 1f, false);
	}

	private static Vector2 GetRectEdgePoint(Rect2 rect, Vector2 point)
	{
		Vector2 center = rect.Position + rect.Size * 0.5f;
		Vector2 delta = point - center;
		if (Mathf.IsZeroApprox(delta.X) && Mathf.IsZeroApprox(delta.Y))
			return center;

		float scaleX = Mathf.IsZeroApprox(delta.X) ? float.PositiveInfinity : (rect.Size.X * 0.5f) / Mathf.Abs(delta.X);
		float scaleY = Mathf.IsZeroApprox(delta.Y) ? float.PositiveInfinity : (rect.Size.Y * 0.5f) / Mathf.Abs(delta.Y);
		float scale = Mathf.Min(scaleX, scaleY);
		return (center + delta * scale).Floor();
	}
}
