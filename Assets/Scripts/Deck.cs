// Deck.cs
using Godot;
using System.Collections.Generic;

[Tool]
public partial class Deck : Control
{
	public enum DeckSide { Player, Enemy }

	[Export] public Resource DeckList { get; set; }
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
	private readonly Color _previewCard = new(1f, 1f, 1f, 1f);
	private readonly Color _previewInk = Colors.Black;

	[Signal] public delegate void TopChangedEventHandler(CardData newTop);
	[Signal] public delegate void TopClickedEventHandler();
	[Signal] public delegate void PlayRequestedEventHandler(Deck deck, CardData card);
	[Signal] public delegate void ShuffledEventHandler(Deck deck, CardData newTop);

	public override void _Ready()
	{
		_pile = GetNodeOrNull<Control>(PilePath);
		_top  = GetNodeOrNull<Control>(TopHolderPath);
		_origTopPos = _top?.Position ?? Vector2.Zero;

		ApplySideLayout(); // set stack direction and provisional top position
		BuildDeck();

		if (Engine.IsEditorHint())
		{
			QueueRedraw();
			return;
		}

		if (EnableInput && _top != null)
		{
			_top.MouseFilter = MouseFilterEnum.Stop;
			_top.GuiInput += OnTopGuiInput;
		}
		else if (_top != null)
		{
			_top.MouseFilter = MouseFilterEnum.Ignore;
		}

		Shuffle(_draw);
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
	}

	public override void _Input(InputEvent e)
	{
		if (Engine.IsEditorHint())
			return;

		if (!EnableInput || e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
			return;

		if (!GetPlayableCardRect().HasPoint(GetGlobalMousePosition()))
			return;

		RequestTopCardPlay();
		GetViewport().SetInputAsHandled();
	}

	public override void _Draw()
	{
		if (!Engine.IsEditorHint())
			return;

		DrawEditorPreview();
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
		if (DeckList is not global::DeckList deckList) return;
		foreach (var c in deckList.Cards)
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
			EmitSignal(SignalName.Shuffled, this, Peek());
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
			EmitSignal(SignalName.Shuffled, this, Peek());
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
		if (_pile == null || _top == null) return;

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

			Vector2 faceSize = node.CustomMinimumSize;
			if (faceSize.X <= 1f || faceSize.Y <= 1f)
				faceSize = new Vector2(Mathf.Max(node.Size.X, CardBack?.GetSize().X ?? 48f), Mathf.Max(node.Size.Y, CardBack?.GetSize().Y ?? 72f));
			node.Size = faceSize;
			_top.CustomMinimumSize = faceSize;
			_top.Size = faceSize;

			_top.AddChild(node);
			if (EnableInput)
			{
				node.MouseFilter = MouseFilterEnum.Stop;
				node.GuiInput += OnTopGuiInput;
			}
			else
			{
				node.MouseFilter = MouseFilterEnum.Ignore;
			}

			// Reposition top holder using the ACTUAL face-up width
			if (AutoPlaceTop)
			{
				PlaceTopHolder(faceSize.X);
			}
		}
	}

	private void DrawEditorPreview()
	{
		if (_pile == null || _top == null) return;

		int backs = Mathf.Max(1, Mathf.Min(MaxBacksShown, _draw.Count));
		Vector2 backSize = CardBack?.GetSize() ?? new Vector2(48, 72);
		for (int i = 0; i < backs; i++)
		{
			Vector2 pos = (_pile.Position + new Vector2(i * BackOffset.X, i * BackOffset.Y)).Floor();
			if (CardBack != null)
				DrawTexture(CardBack, pos);
			else
			{
				DrawRect(new Rect2(pos, backSize), Side == DeckSide.Enemy ? new Color(0.55f, 0.12f, 0.16f) : new Color(0.1f, 0.28f, 0.55f), true);
				DrawRect(new Rect2(pos, backSize), _previewInk, false, 2);
			}
		}

		Vector2 faceSize = new(112, 168);
		Vector2 topPosition = _top.Position;
		if (AutoPlaceTop)
		{
			float gap = Mathf.Max(0, TopGap);
			float x = (Side == DeckSide.Enemy)
				? _pile.Position.X - faceSize.X - gap
				: _pile.Position.X + (CardBack?.GetSize().X ?? faceSize.X) + gap;
			topPosition = new Vector2(Mathf.Floor(x), Mathf.Floor(_top.Position.Y));
		}

		var face = new Rect2(topPosition.Floor(), faceSize);
		DrawRect(face, _previewCard, true);
		DrawRect(face, _previewInk, false, 2);
		DrawLine(face.Position + new Vector2(0, 18), face.Position + new Vector2(face.Size.X, 18), _previewInk, 2);
		DrawLine(face.Position + new Vector2(0, 70), face.Position + new Vector2(face.Size.X, 70), _previewInk, 2);
	}

	private void OnTopGuiInput(InputEvent e)
	{
		if (e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
			return;

		RequestTopCardPlay();
		AcceptEvent();
	}

	private void RequestTopCardPlay()
	{
		EmitSignal(SignalName.PlayRequested, this, Peek());
		if (DiscardOnTopClick) AdvanceTopToDiscard();
	}

	private Rect2 GetPlayableCardRect()
	{
		if (_top == null) return new Rect2();
		var rect = _top.GetGlobalRect();
		foreach (var child in _top.GetChildren())
		{
			if (child is Control c)
				rect = rect.Merge(c.GetGlobalRect());
		}
		return rect;
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
