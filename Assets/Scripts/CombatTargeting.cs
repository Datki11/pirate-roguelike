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

	private bool _targeting = false;
	private CardData _pendingCard;
	private Deck _pendingDeck;

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
	}

	private async void OnPlayRequested(Deck deck, CardData card)
	{
		GD.Print("OnPlayRequested");
		if (_targeting || card == null) return;

		if (IsAllEnemiesAttack(card))
		{
			if (!TrySpendCardEnergy()) return;
			if (_combat != null) await _combat.PlayCardAuto(deck, card, Deck.DeckSide.Player, GetDeckOwner(deck));
			else GD.PushWarning("CombatManager missing; AoE skipped.");
			return;
		}

		if (IsSingleTargetAttack(card))
		{
			if (!CanSpendCardEnergy()) return;
			_pendingCard = card;
			_pendingDeck = deck;
			BeginTargeting();
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

	private void BeginTargeting()
	{
		GD.Print("BeginTargeting");
		_targeting = true;
		foreach (var e in _enemies)
		{
			if (e == null || !IsInstanceValid(e) || !e.Alive) continue;
			e.SetTargetable(true);
			e.Clicked += OnEnemyClicked;
		}
	}

	private void EndTargeting()
	{
		foreach (var e in _enemies)
		{
			if (e == null || !IsInstanceValid(e)) continue;
			e.SetTargetable(false);
			e.Clicked -= OnEnemyClicked;
		}
		_pendingCard = null;
		_pendingDeck = null;
		_targeting = false;
	}

	private async void OnEnemyClicked(Enemy who)
	{
		if (!_targeting || _pendingDeck == null || _pendingCard == null) return;

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

	public override void _UnhandledInput(InputEvent e)
	{
		if (_targeting && e.IsActionPressed("ui_cancel"))
			EndTargeting(); // cancel: keep top card
	}
}
