// Deck.cs
using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

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
	[Export] public int DrawForwardOffset { get; set; } = 18;
	[Export] public int DiscardBehindOffset { get; set; } = 44;
	[Export] public int PileVerticalOffset { get; set; } = 16;
	[Export] public float PlayMoveDurationSec { get; set; } = 0.16f;
	[Export] public float PlayHoldDurationSec { get; set; } = 0.20f;
	[Export] public float PlayFadeDurationSec { get; set; } = 0.18f;

	private Control _pile, _top;
	private Vector2 _origTopPos;

	private readonly List<CardData> _draw = new();
	private readonly List<CardData> _discard = new();
	private readonly RandomNumberGenerator _rng = new();
	private readonly Color _previewCard = new(1f, 1f, 1f, 1f);
	private readonly Color _previewInk = Colors.Black;
	private readonly Color _discardCard = new(1f, 1f, 1f, 1f);

	private Control _playedCard;
	private CanvasLayer _presentationLayer;
	private CanvasLayer _drawPileModalLayer;
	private bool _playPresentationRunning;
	private bool _targetingDimmed;
	private bool _topCardTooltipSuppressed;
	private Color _normalModulate = Colors.White;
	private static int _openDrawPileModalCount;
	public static bool IsDrawPileModalOpen => _openDrawPileModalCount > 0;

	[Signal] public delegate void TopChangedEventHandler(CardData newTop);
	[Signal] public delegate void TopClickedEventHandler();
	[Signal] public delegate void PlayRequestedEventHandler(Deck deck, CardData card);
	[Signal] public delegate void ShuffledEventHandler(Deck deck, CardData newTop);

	public override void _Ready()
	{
		_pile = GetNodeOrNull<Control>(PilePath);
		_top  = GetNodeOrNull<Control>(TopHolderPath);
		_origTopPos = _top?.Position ?? Vector2.Zero;
		_normalModulate = Modulate;

		ApplySideLayout(); // set stack direction and provisional top position
		BuildDeck();

		if (Engine.IsEditorHint())
		{
			SetEditorPreviewChildrenVisible(false);
			QueueRedraw();
			return;
		}

		if (_pile != null)
		{
			_pile.MouseFilter = MouseFilterEnum.Stop;
			_pile.GuiInput += OnPileGuiInput;
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

		if (_openDrawPileModalCount > 0 || !EnableInput || e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
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

	public async Task BeginCardPlayPresentation(CardData card)
	{
		if (card == null || CardViewScene == null || _top == null || !IsInsideTree())
			return;

		_playPresentationRunning = true;
		SetTopCardVisible(false);

		_playedCard?.QueueFree();
		_playedCard = CreateCardView(card);
		if (_playedCard == null)
			return;

		var parent = GetPresentationLayer();
		parent.AddChild(_playedCard);
		_playedCard.ZAsRelative = false;
		_playedCard.ZIndex = 1000;
		_playedCard.MouseFilter = MouseFilterEnum.Ignore;
		_playedCard.GlobalPosition = _top.GetGlobalTransformWithCanvas().Origin.Floor();

		var viewportSize = GetViewportRect().Size;
		var target = ((viewportSize - _playedCard.Size) * 0.5f).Floor();

		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_playedCard, "global_position", target, Mathf.Max(0.01f, PlayMoveDurationSec));
		await ToSignal(tween, "finished");
	}

	public async Task AdvanceTopToDiscardWithPresentation(CardData card = null)
	{
		card ??= Peek();
		if (!_playPresentationRunning)
			await BeginCardPlayPresentation(card);

		if (PlayHoldDurationSec > 0f)
			await ToSignal(GetTree().CreateTimer(PlayHoldDurationSec), "timeout");

		await FadePlayedCard();

		_playPresentationRunning = false;
		AdvanceTopToDiscard();
	}

	public async Task FinishCardPlayPresentationWithoutDiscard()
	{
		if (PlayHoldDurationSec > 0f)
			await ToSignal(GetTree().CreateTimer(PlayHoldDurationSec), "timeout");

		await FadePlayedCard();
		_playPresentationRunning = false;
		SetTopCardVisible(true);
	}

	public void CancelCardPlayPresentation()
	{
		_playPresentationRunning = false;
		_playedCard?.QueueFree();
		_playedCard = null;
		SetTopCardVisible(true);
	}

	public void SetTopCardTargetingFocus(bool focused)
	{
		foreach (var card in GetTopCardViews())
			card.SetTargetingFocus(focused);
	}

	public void SetTargetingDimmed(bool dimmed)
	{
		if (_targetingDimmed == dimmed)
			return;

		_targetingDimmed = dimmed;
		Modulate = dimmed ? new Color(0.58f, 0.58f, 0.58f, 0.92f) : _normalModulate;
	}

	public void SetTopCardTooltipSuppressed(bool suppressed)
	{
		_topCardTooltipSuppressed = suppressed;
		foreach (var card in GetTopCardViews())
			card.SetTooltipSuppressed(suppressed);
	}

	public Rect2 GetTopCardCanvasRect()
	{
		if (_top == null)
			return new Rect2();

		Rect2 rect = GetControlCanvasRect(_top);
		foreach (var child in _top.GetChildren())
		{
			if (child is Control control)
				rect = rect.Merge(GetControlCanvasRect(control));
		}

		return rect;
	}

	public Rect2 GetLocalContentRect()
	{
		bool hasRect = false;
		Rect2 rect = default;
		Transform2D toLocal = GetGlobalTransformWithCanvas().AffineInverse();

		MergeControlAndChildrenLocalRect(_pile, toLocal, ref rect, ref hasRect);
		MergeControlAndChildrenLocalRect(_top, toLocal, ref rect, ref hasRect);

		if (hasRect)
			return rect;

		Vector2 fallbackSize = Size;
		if (fallbackSize.X <= 0f || fallbackSize.Y <= 0f)
			fallbackSize = CustomMinimumSize;
		return new Rect2(Vector2.Zero, fallbackSize);
	}

	public override void _ExitTree()
	{
		CloseDrawPileModal();
	}

	// ---------- View ----------
	private void RefreshView()
	{
		if (_pile == null || _top == null) return;

		// discard placeholder and backs
		foreach (var n in _pile.GetChildren()) n.QueueFree();
		if (_discard.Count > 0)
			_pile.AddChild(CreateDiscardMarker());

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
			if (node is BaseCardView topCardView)
				topCardView.SetTooltipSuppressed(_topCardTooltipSuppressed);

			Vector2 faceSize = GetControlSize(node);
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

			if (AutoPlaceTop)
			{
				PlaceTopHolder(faceSize);
			}
		}
	}

	private Control CreateCardView(CardData card)
	{
		var node = CardViewScene.Instantiate<Control>();
		if (node is BaseCardView view) view.SetData(card);
		else GD.PushError("CardViewScene must inherit BaseCardView.");

		Vector2 faceSize = GetControlSize(node);
		node.CustomMinimumSize = faceSize;
		node.Size = faceSize;
		return node;
	}

	private void OnPileGuiInput(InputEvent e)
	{
		if (Engine.IsEditorHint() || e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
			return;

		OpenDrawPileModal();
		AcceptEvent();
	}

	private void OpenDrawPileModal()
	{
		if (!IsInsideTree() || CardViewScene == null)
			return;

		CloseDrawPileModal();

		var cards = GetRandomizedDrawPileSnapshot();
		_drawPileModalLayer = new CanvasLayer { Layer = 200 };
		_openDrawPileModalCount++;

		var overlay = new Control
		{
			Name = "DrawPileModalOverlay",
			MouseFilter = MouseFilterEnum.Stop,
			Size = GetViewportRect().Size
		};
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.GuiInput += OnDrawPileOverlayGuiInput;

		var shade = new ColorRect
		{
			Color = new Color(0f, 0f, 0f, 0.72f),
			MouseFilter = MouseFilterEnum.Stop,
			Size = GetViewportRect().Size
		};
		shade.SetAnchorsPreset(LayoutPreset.FullRect);
		shade.GuiInput += EatModalInput;
		overlay.AddChild(shade);

		_drawPileModalLayer.AddChild(overlay);
		(GetTree().CurrentScene as Node ?? GetTree().Root).AddChild(_drawPileModalLayer);
		LayoutDrawPileModal(overlay, cards);
		TooltipDisplay.ModalTooltipScope = overlay;
	}

	private void LayoutDrawPileModal(Control overlay, List<CardData> cards)
	{
		Vector2 viewport = GetViewportRect().Size;
		const float cardGap = 12f;
		const float modalGap = 16f;
		Vector2 cardSize = GetCardViewSize();
		float rowWidth = cards.Count * cardSize.X + Mathf.Max(0, cards.Count - 1) * cardGap;
		float startX = Mathf.Round((viewport.X - rowWidth) * 0.5f);
		float cardY = Mathf.Round((viewport.Y - cardSize.Y) * 0.5f - 24f);

		for (int i = 0; i < cards.Count; i++)
		{
			var cardView = CreateCardView(cards[i]);
			if (cardView == null)
				continue;

			cardView.MouseFilter = MouseFilterEnum.Pass;
			cardView.Position = new Vector2(startX + i * (cardSize.X + cardGap), cardY).Floor();
			overlay.AddChild(cardView);
		}

		var disclaimer = CreateModalLabel("Cards are not shown in any particular order", 20);
		disclaimer.HorizontalAlignment = HorizontalAlignment.Center;
		disclaimer.Size = new Vector2(viewport.X, 28);
		disclaimer.Position = new Vector2(0, cardY + cardSize.Y + modalGap).Floor();
		overlay.AddChild(disclaimer);

		var close = CreateCloseButton();
		close.Position = new Vector2(Mathf.Round((viewport.X - close.CustomMinimumSize.X) * 0.5f), disclaimer.Position.Y + disclaimer.Size.Y + 10f).Floor();
		close.Size = close.CustomMinimumSize;
		overlay.AddChild(close);
	}

	private Vector2 GetCardViewSize()
	{
		var probe = CardViewScene.Instantiate<Control>();
		Vector2 size = probe.CustomMinimumSize;
		if (size.X <= 1f || size.Y <= 1f)
			size = new Vector2(Mathf.Max(probe.Size.X, CardBack?.GetSize().X ?? 48f), Mathf.Max(probe.Size.Y, CardBack?.GetSize().Y ?? 72f));
		probe.Free();
		return size;
	}

	private List<CardData> GetRandomizedDrawPileSnapshot()
	{
		var cards = new List<CardData>(_draw);
		var displayRng = new RandomNumberGenerator();
		displayRng.Randomize();

		for (int i = cards.Count - 1; i > 0; i--)
		{
			int j = (int)displayRng.RandiRange(0, i);
			(cards[i], cards[j]) = (cards[j], cards[i]);
		}

		return cards;
	}

	private Label CreateModalLabel(string text, int fontSize)
	{
		var label = new Label
		{
			Text = text,
			MouseFilter = MouseFilterEnum.Ignore
		};
		label.AddThemeColorOverride("font_color", Colors.White);
		label.AddThemeFontSizeOverride("font_size", fontSize);

		var font = LoadPixelFont();
		if (font != null)
			label.AddThemeFontOverride("font", font);

		ConfigurePixelFont(label.GetThemeFont("font"));
		return label;
	}

	private Button CreateCloseButton()
	{
		var close = new Button
		{
			Text = "CLOSE",
			CustomMinimumSize = new Vector2(112, 38),
			MouseFilter = MouseFilterEnum.Stop
		};
		close.Pressed += CloseDrawPileModal;
		close.AddThemeFontSizeOverride("font_size", 20);
		close.AddThemeColorOverride("font_color", Colors.White);
		close.AddThemeColorOverride("font_hover_color", Colors.White);
		close.AddThemeColorOverride("font_pressed_color", Colors.White);

		var font = LoadPixelFont();
		if (font != null)
			close.AddThemeFontOverride("font", font);

		close.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.82f, 0.12f, 0.10f)));
		close.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.95f, 0.18f, 0.14f)));
		close.AddThemeStyleboxOverride("pressed", CreateButtonStyle(new Color(0.58f, 0.07f, 0.06f)));
		close.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		ConfigurePixelFont(close.GetThemeFont("font"));
		return close;
	}

	private StyleBoxFlat CreateButtonStyle(Color color)
	{
		return new StyleBoxFlat
		{
			BgColor = color,
			BorderColor = Colors.Black,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			AntiAliasing = false
		};
	}

	private FontFile LoadPixelFont()
	{
		return ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf");
	}

	private void ConfigurePixelFont(Font font)
	{
		if (font is FontFile fontFile)
		{
			fontFile.Antialiasing = TextServer.FontAntialiasing.None;
			fontFile.GenerateMipmaps = false;
			fontFile.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
			fontFile.AllowSystemFallback = false;
		}
	}

	private void OnDrawPileOverlayGuiInput(InputEvent e)
	{
		if (e is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
		{
			CloseDrawPileModal();
			AcceptEvent();
			return;
		}

		AcceptEvent();
	}

	private void EatModalInput(InputEvent e)
	{
		AcceptEvent();
	}

	private void CloseDrawPileModal()
	{
		if (_drawPileModalLayer == null)
			return;

		if (GodotObject.IsInstanceValid(_drawPileModalLayer))
			_drawPileModalLayer.QueueFree();

		_drawPileModalLayer = null;
		_openDrawPileModalCount = Mathf.Max(0, _openDrawPileModalCount - 1);
		if (_openDrawPileModalCount == 0)
			TooltipDisplay.ModalTooltipScope = null;
	}

	private CanvasLayer GetPresentationLayer()
	{
		if (_presentationLayer != null && GodotObject.IsInstanceValid(_presentationLayer))
			return _presentationLayer;

		_presentationLayer = new CanvasLayer { Layer = 100 };
		var parent = GetTree().CurrentScene as Node ?? GetTree().Root;
		parent.AddChild(_presentationLayer);
		return _presentationLayer;
	}

	private async Task FadePlayedCard()
	{
		if (_playedCard == null || !GodotObject.IsInstanceValid(_playedCard))
			return;

		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.In);
		tween.TweenProperty(_playedCard, "modulate:a", 0f, Mathf.Max(0.01f, PlayFadeDurationSec));
		await ToSignal(tween, "finished");
		_playedCard.QueueFree();
		_playedCard = null;
	}

	private Control CreateDiscardMarker()
	{
		Vector2 size = CardBack?.GetSize() ?? new Vector2(48, 72);
		int facing = FacingSign();
		float x = -facing * (Mathf.Max(0, DiscardBehindOffset) + Mathf.Max(0, DrawForwardOffset));

		var marker = new Control
		{
			CustomMinimumSize = size,
			Size = size,
			Position = new Vector2(Mathf.Floor(x), 0),
			MouseFilter = MouseFilterEnum.Ignore
		};

		var style = new StyleBoxFlat
		{
			BgColor = _discardCard,
			BorderColor = Colors.Black,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			AntiAliasing = false
		};

		var card = new Panel
		{
			CustomMinimumSize = size,
			Size = size,
			MouseFilter = MouseFilterEnum.Ignore
		};
		card.AddThemeStyleboxOverride("panel", style);
		marker.AddChild(card);
		return marker;
	}

	private void SetTopCardVisible(bool visible)
	{
		if (_top == null) return;
		foreach (var child in _top.GetChildren())
		{
			if (child is CanvasItem item)
				item.Visible = visible;
		}
	}

	private void DrawEditorPreview()
	{
		if (_pile == null || _top == null) return;

		int backs = Mathf.Max(1, Mathf.Min(MaxBacksShown, Mathf.Max(0, _draw.Count - 1)));
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

		Vector2 faceSize = GetFaceUpCardSize();
		Vector2 topPosition = AutoPlaceTop ? GetFaceUpPosition(faceSize) : _top.Position;

		var face = new Rect2(topPosition.Floor(), faceSize);
		DrawRect(face, _previewCard, true);
		DrawRect(face, _previewInk, false, 2);
		float titleBottom = Mathf.Round(face.Size.Y * (36f / 210f));
		float artBottom = Mathf.Round(face.Size.Y * (106f / 210f));
		DrawLine(face.Position + new Vector2(0, titleBottom), face.Position + new Vector2(face.Size.X, titleBottom), _previewInk, 2);
		DrawLine(face.Position + new Vector2(0, artBottom), face.Position + new Vector2(face.Size.X, artBottom), _previewInk, 2);
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

	private IEnumerable<BaseCardView> GetTopCardViews()
	{
		if (_top == null)
			yield break;

		foreach (var child in _top.GetChildren())
		{
			if (child is BaseCardView card)
				yield return card;
		}
	}

	private Rect2 GetControlCanvasRect(Control control)
	{
		if (control == null)
			return new Rect2();

		Vector2 size = control.Size;
		if (size.X <= 0f || size.Y <= 0f)
			size = control.CustomMinimumSize;

		return new Rect2(control.GetGlobalTransformWithCanvas().Origin, size);
	}

	private void MergeControlAndChildrenLocalRect(Control control, Transform2D toLocal, ref Rect2 rect, ref bool hasRect)
	{
		if (control == null)
			return;

		MergeLocalRect(ToLocalRect(GetControlCanvasRect(control), toLocal), ref rect, ref hasRect);
		foreach (Node child in control.GetChildren())
		{
			if (child is Control childControl)
				MergeControlAndChildrenLocalRect(childControl, toLocal, ref rect, ref hasRect);
		}
	}

	private void MergeLocalRect(Rect2 next, ref Rect2 rect, ref bool hasRect)
	{
		if (next.Size.X <= 0f || next.Size.Y <= 0f)
			return;

		if (!hasRect)
		{
			rect = next;
			hasRect = true;
			return;
		}

		rect = rect.Merge(next);
	}

	private Rect2 ToLocalRect(Rect2 canvasRect, Transform2D toLocal)
	{
		Vector2 topLeft = toLocal * canvasRect.Position;
		Vector2 topRight = toLocal * new Vector2(canvasRect.End.X, canvasRect.Position.Y);
		Vector2 bottomRight = toLocal * canvasRect.End;
		Vector2 bottomLeft = toLocal * new Vector2(canvasRect.Position.X, canvasRect.End.Y);

		float minX = Mathf.Min(Mathf.Min(topLeft.X, topRight.X), Mathf.Min(bottomRight.X, bottomLeft.X));
		float minY = Mathf.Min(Mathf.Min(topLeft.Y, topRight.Y), Mathf.Min(bottomRight.Y, bottomLeft.Y));
		float maxX = Mathf.Max(Mathf.Max(topLeft.X, topRight.X), Mathf.Max(bottomRight.X, bottomLeft.X));
		float maxY = Mathf.Max(Mathf.Max(topLeft.Y, topRight.Y), Mathf.Max(bottomRight.Y, bottomLeft.Y));

		return new Rect2(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
	}

	// ---------- Layout helpers ----------
	private void ApplySideLayout()
	{
		if (_pile == null || _top == null) return;

		// Make the pile stack left for enemies, right for players
		int stepX = Mathf.Max(1, Mathf.Min(2, Mathf.Abs(BackOffset.X)));
		int stepY = -Mathf.Max(1, Mathf.Min(2, Mathf.Abs(BackOffset.Y)));
		BackOffset = new Vector2I(FacingSign() * stepX, stepY);
		_pile.Position = new Vector2(FacingSign() * Mathf.Max(0, DrawForwardOffset), Mathf.Max(0, PileVerticalOffset)).Floor();

		if (AutoPlaceTop)
		{
			PlaceTopHolder();
		}
	}

	private void PlaceTopHolder(Vector2? faceSizeOverride = null)
	{
		if (_pile == null || _top == null) return;

		Vector2 faceSize = NormalizeFaceUpSize(faceSizeOverride ?? GetFaceUpCardSize());
		_top.CustomMinimumSize = faceSize;
		_top.Size = faceSize;
		_top.Position = GetFaceUpPosition(faceSize);
	}

	private Vector2 GetFaceUpPosition(Vector2 faceSize)
	{
		Vector2 backSize = CardBack?.GetSize() ?? new Vector2(48, 72);
		faceSize = NormalizeFaceUpSize(faceSize);
		int backs = Mathf.Min(MaxBacksShown, Mathf.Max(0, _draw.Count - 1));
		var stackStep = new Vector2(BackOffset.X, BackOffset.Y);
		int topBackIndex = Mathf.Max(backs - 1, 0);
		Vector2 topBackCenter = _pile.Position + stackStep * topBackIndex + backSize * 0.5f;
		Vector2 faceCornerOffset = Side == DeckSide.Enemy
			? new Vector2(faceSize.X, faceSize.Y)
			: new Vector2(0, faceSize.Y);

		return (topBackCenter - faceCornerOffset).Floor();
	}

	private Vector2 GetFaceUpCardSize()
	{
		if (CardViewScene != null)
			return NormalizeFaceUpSize(GetCardViewSize());

		return NormalizeFaceUpSize(_top?.Size ?? Vector2.Zero);
	}

	private Vector2 NormalizeFaceUpSize(Vector2 size)
	{
		if (size.X > 1f && size.Y > 1f)
			return size;

		Vector2 topSize = _top?.Size ?? Vector2.Zero;
		if (topSize.X > 1f && topSize.Y > 1f)
			return topSize;

		return new Vector2(88, 131);
	}

	private Vector2 GetControlSize(Control control)
	{
		Vector2 size = control.CustomMinimumSize;
		if (size.X <= 1f || size.Y <= 1f)
			size = new Vector2(Mathf.Max(control.Size.X, CardBack?.GetSize().X ?? 48f), Mathf.Max(control.Size.Y, CardBack?.GetSize().Y ?? 72f));
		return NormalizeFaceUpSize(size);
	}

	private void SetEditorPreviewChildrenVisible(bool visible)
	{
		SetNamedPreviewChildrenVisible(_pile, visible);
		SetNamedPreviewChildrenVisible(_top, visible);
	}

	private void SetNamedPreviewChildrenVisible(Node parent, bool visible)
	{
		if (parent == null)
			return;

		foreach (Node child in parent.GetChildren())
		{
			if (child is CanvasItem item && child.Name.ToString().StartsWith("Editor Preview"))
				item.Visible = visible;
		}
	}

	private int FacingSign()
		=> Side == DeckSide.Enemy ? -1 : 1;
}
