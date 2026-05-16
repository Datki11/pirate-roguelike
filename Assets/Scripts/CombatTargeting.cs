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
	private CombatManager _combat;                      // NEW
	private EnergyManager _energy;

	private enum TargetingMode
	{
		SingleEnemy,
		AllEnemies
	}

	private bool _targeting = false;
	private CardData _pendingCard;
	private Deck _pendingDeck;
	private CanvasLayer _targetingVisualLayer;
	private TargetingArrowOverlay _targetingArrow;
	private Enemy _lockedTarget;
	private TargetingMode _targetingMode;

	public override void _Ready()
	{
		_combat = GetNodeOrNull<CombatManager>(CombatPath);
		_energy = GetNodeOrNull<EnergyManager>(EnergyPath);
		GD.Print($"CombatTargeting ready | combat? {(_combat != null)}");

		_enemiesRoot = GetNode<Control>(EnemiesPath);
		_enemies = _enemiesRoot.GetChildren().OfType<Enemy>().ToArray();

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
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
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
		else
		{
			if (TrySpendCardEnergy())
			{
				if (GetDeckOwner(deck) is PlayerUnit playerUnit) playerUnit.PlayCardAnimation();
				await deck.AdvanceTopToDiscardWithPresentation(card);  // non-attack or self/ally effects later
			}
		}
	}

	private bool IsSingleTargetAttack(CardData c)
	{
		var tid = (c.TargetDef as TargetDef)?.Id;
		bool hasAttack = c.Effects != null && c.Effects.Any(e => e?.Def?.Id == "attack");
		return tid == "single" && hasAttack;
	}

	private bool IsAllEnemiesAttack(CardData c)
	{
		var tid = (c.TargetDef as TargetDef)?.Id;
		bool hasAttack = c.Effects != null && c.Effects.Any(e => e?.Def?.Id == "attack");
		return (tid == "multiple" || tid == "all_enemies") && hasAttack;
	}

	private int GetAttackAmount(CardData c)
		=> c.Effects.Where(e => e?.Def?.Id == "attack").Select(e => e.Amount).FirstOrDefault();

	private void BeginTargeting(TargetingMode mode)
	{
		GD.Print("BeginTargeting");
		_targeting = true;
		_targetingMode = mode;
		_lockedTarget = null;
		foreach (var e in _enemies)
		{
			if (e == null || !IsInstanceValid(e) || !e.Alive) continue;
			bool singleTarget = mode == TargetingMode.SingleEnemy;
			e.SetTargetable(singleTarget);
			if (singleTarget)
				e.Clicked += OnEnemyClicked;
		}
		ApplyTargetingVisuals(true);
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
			e.Clicked -= OnEnemyClicked;
		}
		_pendingCard = null;
		_pendingDeck = null;
		_targeting = false;
		_lockedTarget = null;
		_targetingArrow?.ClearArrow();
		SetProcess(false);
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
			}
		}

		if (_enemies != null)
		{
			foreach (var enemy in _enemies)
			{
				if (enemy == null || !IsInstanceValid(enemy))
					continue;

				enemy.SetDeckTargetingDimmed(enabled);
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
			bool locked = IsInAllEnemiesTargetZone(allMouse);
			Vector2 allEndPoint = locked ? GetAllEnemiesTargetAnchor() : allMouse;
			arrow.SetAllEnemiesZone(GetAllEnemiesZoneStartX(), locked);
			arrow.SetArrow(_pendingDeck.GetTopCardCanvasRect(), allEndPoint, locked);
			return;
		}

		arrow.ClearAllEnemiesZone();
		Vector2 mouse = GetViewport().GetMousePosition();
		if (_lockedTarget == null || !IsInstanceValid(_lockedTarget) || !_lockedTarget.Alive || !_lockedTarget.GetTargetingCanvasRect().HasPoint(mouse))
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

	private TargetingArrowOverlay GetTargetingArrow()
	{
		if (_targetingArrow != null && IsInstanceValid(_targetingArrow))
			return _targetingArrow;

		_targetingVisualLayer = new CanvasLayer { Layer = 80 };
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

	private float GetAllEnemiesZoneStartX()
	{
		var viewport = GetViewport().GetVisibleRect();
		return Mathf.Round(viewport.Size.X * 0.6f);
	}

	private Vector2 GetAllEnemiesTargetAnchor()
	{
		var viewport = GetViewport().GetVisibleRect();
		return new Vector2(GetAllEnemiesZoneStartX(), viewport.Size.Y * 0.5f).Floor();
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

	public override void _Input(InputEvent e)
	{
		if (Deck.IsDrawPileModalOpen || !_targeting)
			return;

		if (e is not InputEventMouseButton mb || !mb.Pressed)
			return;

		if (mb.ButtonIndex == MouseButton.Right)
		{
			EndTargeting();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mb.ButtonIndex != MouseButton.Left)
			return;

		if (_targetingMode == TargetingMode.AllEnemies && IsInAllEnemiesTargetZone(mb.Position))
		{
			ConfirmAllEnemiesTarget();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_targetingMode == TargetingMode.SingleEnemy && GetTargetUnderMouse(mb.Position) != null)
			return;

		EndTargeting();
		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Deck.IsDrawPileModalOpen)
			return;

		if (_targeting && e.IsActionPressed("ui_cancel"))
			EndTargeting(); // cancel: keep top card
	}
}

public partial class TargetingArrowOverlay : Control
{
	private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.78f);
	private static readonly Color AimColor = new(0.94f, 0.67f, 0.14f, 1f);
	private static readonly Color LockedColor = new(1f, 0.96f, 0.68f, 1f);
	private static readonly Color ZoneBaseColor = new(0.9f, 0.2f, 0.2f, 1f);

	private Rect2 _cardRect;
	private Vector2 _endPoint;
	private bool _hasArrow;
	private bool _locked;
	private bool _hasAllEnemiesZone;
	private bool _allEnemiesZoneActive;
	private float _allEnemiesZoneStartX;

	public void SetArrow(Rect2 cardRect, Vector2 endPoint, bool locked)
	{
		_cardRect = cardRect;
		_endPoint = endPoint;
		_locked = locked;
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
		_allEnemiesZoneStartX = startX;
		_allEnemiesZoneActive = active;
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
		Color lineColor = _locked ? LockedColor : AimColor;

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
		Color color = _allEnemiesZoneActive ? ZoneBaseColor.Lightened(0.18f) : ZoneBaseColor;
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
