// Deck.cs
using Godot;
using System.Collections.Generic;

public partial class Deck : Control
{
	// ---- Side (who owns this deck) -------------------------------------------
	public enum DeckSide { Player, Enemy }
	[Export] public DeckSide Side { get; set; } = DeckSide.Player;
	public bool IsPlayer => Side == DeckSide.Player;
	public bool IsEnemy  => Side == DeckSide.Enemy;

	// ---- Data / visuals -------------------------------------------------------
	[Export] public DeckList DeckList { get; set; }
	[Export] public PackedScene CardViewScene { get; set; }   // SmallCard.tscn now, Card.tscn later
	[Export] public Texture2D CardBack { get; set; }          // 24x36 PNG (nearest)

	// ---- Scene wiring ---------------------------------------------------------
	[Export] public NodePath PilePath { get; set; }           // Control
	[Export] public NodePath TopHolderPath { get; set; }      // Control

	// ---- Layout ---------------------------------------------------------------
	[Export] public int MaxBacksShown = 5;
	[Export] public Vector2I BackOffset = new Vector2I(2, -2);

	// ---- Interaction toggles --------------------------------------------------
	[Export] public bool EnableInput { get; set; } = true;    // player=true, enemy=false
	[Export] public bool DiscardOnTopClick { get; set; } = true;

	// ---- Signals --------------------------------------------------------------
	[Signal] public delegate void TopChangedEventHandler(CardData newTop);
	[Signal] public delegate void PlayRequestedEventHandler(Deck deck, CardData card);

	// ---- State ----------------------------------------------------------------
	private Control _pile, _top;
	private readonly List<CardData> _draw    = new();
	private readonly List<CardData> _discard = new();
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		_pile = GetNode<Control>(PilePath);
		_top  = GetNode<Control>(TopHolderPath);

		// Input only if enabled (players)
		if (_top != null)
		{
			_top.MouseFilter = EnableInput ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
			if (EnableInput)
			{
				_top.GuiInput += (InputEvent e) =>
				{
					if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
						RequestPlay();
				};
			}
		}

		BuildDeck();
		Shuffle(_draw);
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
	}

	/// Single entry-point to attempt to play the current top card (UI or AI).
	public void RequestPlay()
	{
		EmitSignal(SignalName.PlayRequested, this, Peek());
		if (DiscardOnTopClick)
			AdvanceTopToDiscard();
	}

	public void SetBack(Texture2D tex) { CardBack = tex; RefreshView(); }

	// ---------------- Deck ops ----------------

	private void BuildDeck()
	{
		_draw.Clear();
		_discard.Clear();
		if (DeckList == null) return;
		foreach (var c in DeckList.Cards)
			if (c != null) _draw.Add(c); // duplicates allowed
	}

	private void Shuffle(List<CardData> list)
	{
		_rng.Randomize();
		for (int i = list.Count - 1; i > 0; i--)
		{
			int j = (int)_rng.RandiRange(0, i);
			(list[i], list[j]) = (list[j], list[i]);
		}
	}

	public CardData Peek() => _draw.Count > 0 ? _draw[^1] : null;

	// Renamed to avoid hiding CanvasItem.Draw()
	public CardData DrawCard()
	{
		if (_draw.Count == 0)
		{
			if (_discard.Count == 0) return null;
			_draw.AddRange(_discard);
			_discard.Clear();
			Shuffle(_draw);
		}
		var c = _draw[^1];
		_draw.RemoveAt(_draw.Count - 1);
		RefreshView();
		return c;
	}

	public void Discard(CardData c)
	{
		if (c != null) _discard.Add(c);
		RefreshView();
	}

	public bool EnsureTop()
	{
		if (_draw.Count == 0 && _discard.Count > 0)
		{
			_draw.AddRange(_discard);
			_discard.Clear();
			Shuffle(_draw);
		}
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
		return _draw.Count > 0;
	}

	public CardData AdvanceTopToDiscard()
	{
		// No top? Refill and just reveal; don't discard on this click.
		if (_draw.Count == 0)
		{
			if (!EnsureTop()) return null;
			return null;
		}

		// Discard current top
		var c = _draw[^1];
		_draw.RemoveAt(_draw.Count - 1);
		_discard.Add(c);

		// If that was the last card, auto-refill to immediately reveal next
		if (_draw.Count == 0)
			EnsureTop();
		else
		{
			RefreshView();
			EmitSignal(SignalName.TopChanged, Peek());
		}

		return c;
	}

	// ---------------- Visuals ----------------

	private void RefreshView()
	{
		// backs
		foreach (var n in _pile.GetChildren()) n.QueueFree();
		int backs = Mathf.Min(MaxBacksShown, Mathf.Max(0, _draw.Count - 1));
		for (int i = 0; i < backs; i++)
		{
			var tr = new TextureRect
			{
				Texture = CardBack,
				StretchMode = TextureRect.StretchModeEnum.Keep,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				Position = new Vector2(i * BackOffset.X, i * BackOffset.Y),
				MouseFilter = MouseFilterEnum.Ignore
			};
			tr.Position = tr.Position.Floor(); // pixel-snap
			_pile.AddChild(tr);
		}

		// top face-up
		foreach (var n in _top.GetChildren()) n.QueueFree();
		var top = Peek();
		if (top != null && CardViewScene != null)
		{
			var node = CardViewScene.Instantiate<Control>();
			if (node is BaseCardView view) view.SetData(top);
			else GD.PushError("CardViewScene must inherit BaseCardView.");
			_top.AddChild(node);
		}
	}
}
