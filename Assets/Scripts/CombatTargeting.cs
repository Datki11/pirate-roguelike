// CombatTargeting.cs
using Godot;
using System.Linq;
using Godot.Collections;

public partial class CombatTargeting : Node
{
	[Export] public Array<NodePath> DeckPaths { get; set; } = new();
	[Export] public NodePath EnemiesPath { get; set; }

	private Deck[] _decks;
	private Control _enemiesRoot;
	private Enemy[] _enemies;

	private bool _targeting = false;
	private CardData _pendingCard;
	private Deck _pendingDeck;

	public override void _Ready()
	{
		_enemiesRoot = GetNode<Control>(EnemiesPath);
		_enemies = _enemiesRoot.GetChildren().OfType<Enemy>().ToArray();

		var tmp = new System.Collections.Generic.List<Deck>();
		foreach (var p in DeckPaths)
		{
			var d = GetNodeOrNull<Deck>(p);
			if (d == null) continue;
			d.DiscardOnTopClick = false;                 // centralize discard logic here
			d.PlayRequested += OnPlayRequested;          // listen to all decks
			tmp.Add(d);
		}
		_decks = tmp.ToArray();
	}

	private void OnPlayRequested(Deck deck, CardData card)
	{
		GD.Print("OnPlayRequested Reached");
		if (_targeting) return;          // ignore spam while choosing a target
		if (card == null) return;        // deck was empty and EnsureTop found nothing

		if (IsSingleTargetAttack(card))
		{
			_pendingCard = card;
			_pendingDeck = deck;
			BeginTargeting();
		}
		else
		{
			deck.AdvanceTopToDiscard();  // no targeting needed
		}
	}

	private bool IsSingleTargetAttack(CardData c)
	{
		var single = (c.TargetDef as TargetDef)?.Id == "single";
		bool hasAttack = c.Effects != null && c.Effects.Any(e => e?.Def?.Id == "attack");
		return single && hasAttack;
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

	private void OnEnemyClicked(Enemy who)
	{
		if (!_targeting || _pendingDeck == null || _pendingCard == null) return;

		int dmg = GetAttackAmount(_pendingCard);
		if (dmg > 0 && who != null && who.Alive) who.TakeDamage(dmg);

		_pendingDeck.AdvanceTopToDiscard();     // reveal next top in that deck
		EndTargeting();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (_targeting && e.IsActionPressed("ui_cancel"))
			EndTargeting(); // cancel: keep top card
	}
}
