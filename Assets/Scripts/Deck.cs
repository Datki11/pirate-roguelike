// Deck.cs
using Godot;
using System.Collections.Generic;

public partial class Deck : Control
{
	public enum DeckSide { Player, Enemy }

	[Export] public DeckList DeckList { get; set; }
	[Export] public PackedScene CardViewScene { get; set; }   // SmallCard.tscn now, Card.tscn later
	[Export] public Texture2D CardBack { get; set; }          // 48x72 PNG (nearest)

	[Export] public NodePath PilePath { get; set; }           // Control (draw pile origin)
	[Export] public NodePath TopHolderPath { get; set; }      // Control (face-up card anchor)

	[Export] public int MaxBacksShown = 5;
	[Export] public Vector2I BackOffset = new Vector2I(4, -4);

	[Export] public DeckSide Side { get; set; } = DeckSide.Player;
	[Export] public bool EnableInput { get; set; } = true;        // player decks true, enemy decks false
	[Export] public bool DiscardOnTopClick { get; set; } = true;  // usually true for player

	// Auto place the top card next to the pile
	[Export] public bool AutoPlaceTop { get; set; } = true;
	[Export] public int TopGap { get; set; } = 4;                 // pixels between pile and top card

	private Control _pile, _top;
	private Vector2 _origTopPos;

	private readonly List<CardData> _draw = new();
	private readonly List<CardData> _discard = new();
	private readonly RandomNumberGenerator _rng = new();

	[Signal] public delegate void TopChangedEventHandler(CardData newTop);
	[Signal] public delegate void TopClickedEventHandler();
	[Signal] public delegate void PlayRequestedEventHandler(Deck deck, CardData card);

	public override void _Ready()
	{
		_pile = GetNode<Control>(PilePath);
		_top  = GetNode<Control>(TopHolderPath);
		_origTopPos = _top?.Position ?? Vector2.Zero;

		ApplySideLayout(); // set stack direction and provisional top position

		if (EnableInput && _top != null)
		{
			_top.MouseFilter = MouseFilterEnum.Stop;
			_top.GuiInput += (InputEvent e) =>
			{
				if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
				{
					EmitSignal(SignalName.PlayRequested, this, Peek());
					if (DiscardOnTopClick) AdvanceTopToDiscard();
				}
			};
		}
		else if (_top != null)
		{
			_top.MouseFilter = MouseFilterEnum.Ignore;
		}

		BuildDeck();
		Shuffle(_draw);
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
	}

	// Let AI/enemy “click” the deck
	public void RequestPlay()
	{
		EmitSignal(SignalName.PlayRequested, this, Peek());
	}

	// ---------- Build / shuffle ----------
	private void BuildDeck()
	{
		_draw.Clear(); _discard.Clear();
		if (DeckList == null) return;
		foreach (var c in DeckList.Cards)
			if (c != null) _draw.Add(c);
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

	public new CardData Draw()
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
		if (_draw.Count == 0)
		{
			if (!EnsureTop()) return null;
			return null; // stop here; next click/RequestPlay will play new top
		}

		var c = _draw[^1];
		_draw.RemoveAt(_draw.Count - 1);
		_discard.Add(c);

		if (_draw.Count == 0)
			EnsureTop();
		else
		{
			RefreshView();
			EmitSignal(SignalName.TopChanged, Peek());
		}

		return c;
	}

	// ---------- View ----------
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
			tr.Position = tr.Position.Floor();
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

			// Reposition top holder using the ACTUAL face-up width
			if (AutoPlaceTop)
			{
				float faceW = node.GetRect().Size.X;
				if (faceW <= 1f)
				{
					// fallbacks if size isn't ready yet
					faceW = Mathf.Max(node.Size.X, node.CustomMinimumSize.X);
					if (faceW <= 1f) faceW = CardBack?.GetSize().X ?? 48f;
				}
				PlaceTopHolder(faceW);
			}
		}
	}

	// ---------- Layout helpers ----------
	private void ApplySideLayout()
	{
		if (_pile == null || _top == null) return;

		// Make the pile stack left for enemies, right for players
		BackOffset = (Side == DeckSide.Enemy)
			? new Vector2I(-Mathf.Abs(BackOffset.X), BackOffset.Y)
			: new Vector2I( Mathf.Abs(BackOffset.X), BackOffset.Y);

		// Provisional positioning using back width; we'll refine after we know face width
		if (AutoPlaceTop)
		{
			float w = CardBack?.GetSize().X ?? 48f;
			PlaceTopHolder(w);
		}
	}

	private void PlaceTopHolder(float faceW)
	{
		if (_pile == null || _top == null) return;

		float gap = Mathf.Max(0, TopGap);
		float y = _top.Position.Y;

		float x = (Side == DeckSide.Enemy)
			? _pile.Position.X - faceW - gap                      // LEFT of pile by face width
			: _pile.Position.X + (CardBack?.GetSize().X ?? faceW) + gap; // RIGHT of pile

		_top.Position = new Vector2(Mathf.Floor(x), Mathf.Floor(y));
	}
}
