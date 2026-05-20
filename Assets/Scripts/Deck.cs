// Deck.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
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
	[Export] public int BaseDrawPriority { get; set; }
	[Export] public int FaceUpDrawPriority { get; set; } = 1800;
	[Export] public int HoverDrawPriority { get; set; } = 10000;
	[Export] public Color DimmedModulate { get; set; } = new(0.46f, 0.46f, 0.46f, 0.86f);
	[Export] public Color ControllerUnfocusedModulate { get; set; } = new(0.68f, 0.68f, 0.68f, 0.95f);

	// Auto place the top card next to the pile
	[Export] public bool AutoPlaceTop { get; set; } = true;
	[Export] public int DrawForwardOffset { get; set; } = 18;
	[Export] public int DiscardBehindOffset { get; set; } = 44;
	[Export] public int PileVerticalOffset { get; set; } = 16;
	[Export] public int VisibleTopCardSpacing { get; set; } = 64;
	[Export] public int MinVisibleTopCardSpacing { get; set; } = 24;
	[Export] public string ExtraVisibleCardsStatusId { get; set; } = "strategist";
	[Export] public float PlayMoveDurationSec { get; set; } = 0.265f;
	[Export] public float PlayHoldDurationSec { get; set; } = 0.335f;
	[Export] public float PlayFadeDurationSec { get; set; } = 0.30f;

	private Control _pile, _top;
	private Control _controllerPromptRoot;
	private Vector2 _origTopPos;
	private CanvasItem _drawPriorityRoot;
	private int _rootNormalZIndex;
	private bool _rootNormalZAsRelative;

	private readonly List<CardData> _draw = new();
	private readonly List<CardData> _discard = new();
	private readonly RandomNumberGenerator _rng = new();
	private readonly Color _previewCard = new(1f, 1f, 1f, 1f);
	private readonly Color _previewInk = Colors.Black;
	private readonly Color _discardCard = new(1f, 1f, 1f, 1f);

	private Control _playedCard;
	private CanvasLayer _presentationLayer;
	private CanvasLayer _drawPileModalLayer;
	private Control _hoverPreviewCard;
	private bool _playPresentationRunning;
	private bool _targetingDimmed;
	private bool _turnDimmed;
	private bool _energyDimmed;
	private bool _deadDimmed;
	private bool _controllerFocused;
	private bool _controllerUnfocused;
	private bool _isActiveHover;
	private bool _ownerHovering;
	private bool _hoveringTopCard;
	private bool _hoveringPile;
	private bool _topCardTooltipSuppressed;
	private bool _effectiveTooltipSuppressed;
	private int _hoverSerial;
	private int _hoveredVisibleCardIndex = -1;
	private int _controllerVisibleCardIndex;
	private int _pendingPlayCardIndex = -1;
	private int _visibleTopCardCount = 1;
	private Color _normalModulate = Colors.White;
	private bool _topCardsDimmed;
	private static int _openDrawPileModalCount;
	private static int _nextHoverSerial;
	private const int HoverCardCanvasLayer = 900;
	private const int EnemyIntentHoverCardCanvasLayer = 1995;
	private static readonly List<Deck> _hoverDecks = new();
	private static Deck _activeHoverDeck;
	private static CanvasLayer _hoverCardLayer;
	private static Texture2D _drawPilePromptIcon;
	private static Texture2D _discardPilePromptIcon;
	public static bool IsDrawPileModalOpen => _openDrawPileModalCount > 0;

	private sealed class PileModalSnapshot
	{
		public string Title { get; }
		public List<CardData> OrderedCards { get; }
		public List<CardData> UnorderedCards { get; }
		public int Count => OrderedCards.Count + UnorderedCards.Count;

		public PileModalSnapshot(string title, List<CardData> orderedCards, List<CardData> unorderedCards = null)
		{
			Title = title;
			OrderedCards = orderedCards ?? new List<CardData>();
			UnorderedCards = unorderedCards ?? new List<CardData>();
		}
	}

	[Signal] public delegate void TopChangedEventHandler(CardData newTop);
	[Signal] public delegate void TopClickedEventHandler();
	[Signal] public delegate void PlayRequestedEventHandler(Deck deck, CardData card);
	[Signal] public delegate void ShuffledEventHandler(Deck deck, CardData newTop);
	[Signal] public delegate void ActiveHoverChangedEventHandler(bool active);

	public override void _Ready()
	{
		_pile = GetNodeOrNull<Control>(PilePath);
		_top  = GetNodeOrNull<Control>(TopHolderPath);
		_origTopPos = _top?.Position ?? Vector2.Zero;
		_normalModulate = Modulate;
		_drawPriorityRoot = GetParent() as CanvasItem;
		if (_drawPriorityRoot != null)
		{
			_rootNormalZIndex = _drawPriorityRoot.ZIndex;
			_rootNormalZAsRelative = _drawPriorityRoot.ZAsRelative;
		}
		ZAsRelative = false;
		if (BaseDrawPriority == 0)
			BaseDrawPriority = ZIndex;
		ApplyDrawPriority();

		ApplySideLayout(); // set stack direction and provisional top position
		BuildDeck();

		if (Engine.IsEditorHint())
		{
			SetEditorPreviewChildrenVisible(false);
			QueueRedraw();
			return;
		}

		if (!_hoverDecks.Contains(this))
			_hoverDecks.Add(this);
		MouseFilter = MouseFilterEnum.Stop;

		if (_pile != null)
		{
			_pile.MouseFilter = MouseFilterEnum.Stop;
			_pile.GuiInput += OnPileGuiInput;
			_pile.MouseEntered += OnPileMouseEntered;
			_pile.MouseExited += OnPileMouseExited;
		}

		if (_top != null)
		{
			_top.MouseFilter = MouseFilterEnum.Stop;
			_top.GuiInput += OnTopGuiInput;
		}

		Shuffle(_draw);
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
			return;

		RefreshVisibleTopCardCount();
		UpdateHoverStateFromMousePosition();
		RefreshActiveHoverDeck();
		ApplyEffectiveVisualState();
	}

	public override void _Input(InputEvent e)
	{
		if (Engine.IsEditorHint())
			return;

		ControlSchemeFocus.UpdateFromInput(e);

		if (_openDrawPileModalCount > 0 || e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
			return;

		if (CanOpenPileModal(_discard) && GetDiscardMarkerCanvasRect().HasPoint(mb.Position))
		{
			OpenPileModal(new PileModalSnapshot("Discard Pile", new List<CardData>(_discard)));
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!CanPlayTopCard() || !TryGetVisibleCardIndexAtGlobalPosition(GetGlobalMousePosition(), out int visibleIndex))
			return;

		RequestVisibleCardPlay(visibleIndex);
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

	public bool CanControllerFocus()
		=> EnableInput && !_deadDimmed && !_turnDimmed && !_targetingDimmed && Peek() != null && !Deck.IsDrawPileModalOpen;

	public bool CanControllerInspect()
		=> !_deadDimmed && !_targetingDimmed && Peek() != null && !Deck.IsDrawPileModalOpen;

	public bool TryRequestControllerPlay()
	{
		if (!CanPlayTopCard())
			return false;

		RequestVisibleCardPlay(_controllerVisibleCardIndex);
		return true;
	}

	public bool TryMoveControllerCardSelection(int direction)
	{
		if (!_controllerFocused || direction == 0)
			return false;

		int count = GetVisibleTopCardCount();
		if (count <= 1)
			return false;

		_controllerVisibleCardIndex = PosMod(_controllerVisibleCardIndex + direction, count);
		ApplyControllerCardFocus();
		RefreshVisibleCardStacking();
		return true;
	}

	public void OpenDrawPileModal()
	{
		if (CanOpenPileModal(_draw))
			OpenPileModal(CreateDrawPileModalSnapshot());
	}

	public void OpenDiscardPileModal()
	{
		if (CanOpenPileModal(_discard))
			OpenPileModal(new PileModalSnapshot("Discard Pile", new List<CardData>(_discard)));
	}

	public void CloseOpenPileModal()
	{
		CloseDrawPileModal();
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

	public CardData PeekVisible(int visibleIndex)
	{
		EnsureVisibleTopCards(refresh: false);
		int drawIndex = GetDrawIndexForVisibleIndex(visibleIndex);
		return drawIndex >= 0 && drawIndex < _draw.Count ? _draw[drawIndex] : null;
	}

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

	public void AddCardToDrawPile(CardData card)
	{
		if (card == null)
			return;

		int insertIndex = _draw.Count == 0 ? 0 : (int)_rng.RandiRange(0, _draw.Count);
		_draw.Insert(insertIndex, card);
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
	}

	public async Task PeekAndDiscardFromTopAsync(int count)
	{
		if (count <= 0)
			return;

		EnsureVisibleTopCards(refresh: false, minimumVisibleCards: count);
		int visibleCount = Mathf.Min(count, _draw.Count);
		if (visibleCount <= 0)
			return;

		var completion = new TaskCompletionSource<bool>();
		OpenPeekModal(visibleCount, completion);
		await completion.Task;
	}

	public bool EnsureTop()
	{
		EnsureVisibleTopCards(refresh: false, minimumVisibleCards: 1);
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
		return _draw.Count > 0;
	}

	public void RebuildFromDeckList(Resource deckList = null)
	{
		if (deckList != null)
			DeckList = deckList;

		BuildDeck();
		Shuffle(_draw);
		RefreshView();
		EmitSignal(SignalName.TopChanged, Peek());
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

	private CardData AdvanceVisibleCard(int visibleIndex, bool exhaust)
	{
		if (_draw.Count == 0)
		{
			if (!EnsureTop()) return null;
			return null;
		}

		int drawIndex = GetDrawIndexForVisibleIndex(visibleIndex);
		if (drawIndex < 0 || drawIndex >= _draw.Count)
			drawIndex = _draw.Count - 1;

		var c = _draw[drawIndex];
		_draw.RemoveAt(drawIndex);
		if (!exhaust)
			_discard.Add(c);

		_pendingPlayCardIndex = -1;
		ClampControllerVisibleCardIndex();
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
		int presentationIndex = GetPresentationCardIndex(card);
		SetVisibleCardVisible(presentationIndex, false);

		_playedCard?.QueueFree();
		_playedCard = CreateCardView(card);
		if (_playedCard == null)
			return;

		var parent = GetPresentationLayer();
		parent.AddChild(_playedCard);
		_playedCard.ZAsRelative = false;
		_playedCard.ZIndex = 1000;
		_playedCard.MouseFilter = MouseFilterEnum.Ignore;
		var sourceControl = GetVisibleCardControl(presentationIndex);
		_playedCard.GlobalPosition = (sourceControl?.GetGlobalTransformWithCanvas().Origin ?? _top.GetGlobalTransformWithCanvas().Origin).Floor();

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
		AdvanceVisibleCard(GetPresentationCardIndex(card), card?.Exhaust == true);
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
		int focusedIndex = GetFocusedPlayableVisibleIndex();
		foreach (var card in GetTopCardViews())
			card.SetTargetingFocus(focused && GetVisibleIndexForCardControl(card) == focusedIndex);
		RefreshVisibleCardStacking();
	}

	public void ClearPendingVisibleCardSelection()
	{
		_pendingPlayCardIndex = -1;
		RefreshVisibleCardStacking();
	}

	public void SetControllerFocus(bool focused)
	{
		if (_controllerFocused == focused)
			return;

		_controllerFocused = focused;
		if (focused)
			ClampControllerVisibleCardIndex();
		ApplyControllerCardFocus();
		RefreshVisibleCardStacking();
		UpdateControllerPilePrompts();
		ApplyEffectiveVisualState();
	}

	public void SetControllerUnfocused(bool unfocused)
	{
		if (_controllerUnfocused == unfocused)
			return;

		_controllerUnfocused = unfocused;
		ApplyEffectiveVisualState();
	}

	public void SetTargetingDimmed(bool dimmed)
	{
		if (_targetingDimmed == dimmed)
			return;

		_targetingDimmed = dimmed;
		ApplyEffectiveVisualState();
	}

	public void SetTurnDimmed(bool dimmed)
	{
		if (_turnDimmed == dimmed)
			return;

		_turnDimmed = dimmed;
		ApplyEffectiveVisualState();
	}

	public void SetEnergyDimmed(bool dimmed)
	{
		if (_energyDimmed == dimmed)
			return;

		_energyDimmed = dimmed;
		ApplyEffectiveVisualState();
	}

	public void SetDeadDimmed(bool dimmed)
	{
		if (_deadDimmed == dimmed)
			return;

		_deadDimmed = dimmed;
		ApplyEffectiveVisualState();
	}

	public void SetBaseDrawPriority(int priority)
	{
		if (BaseDrawPriority == priority)
			return;

		BaseDrawPriority = priority;
		ApplyDrawPriority();
		RefreshOwnerStackOrder();
	}

	public void SetTopCardTooltipSuppressed(bool suppressed)
	{
		_topCardTooltipSuppressed = suppressed;
		ApplyEffectiveTooltipSuppression();
	}

	public bool IsActiveHover => _isActiveHover;

	public void SetOwnerHovering(bool hovering)
	{
		if (_ownerHovering == hovering)
			return;

		_ownerHovering = hovering;
		if (hovering)
			_hoverSerial = ++_nextHoverSerial;
		RefreshActiveHoverDeck();
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
		if (_hoverPreviewCard != null && GodotObject.IsInstanceValid(_hoverPreviewCard))
			rect = rect.Merge(GetControlCanvasRect(_hoverPreviewCard));

		return rect;
	}

	public Rect2 GetPlayedCardCanvasRect()
	{
		if (_playedCard == null || !GodotObject.IsInstanceValid(_playedCard))
			return GetTopCardCanvasRect();

		return GetControlCanvasRect(_playedCard);
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
		_hoverDecks.Remove(this);
		if (_activeHoverDeck == this)
			_activeHoverDeck = null;
		if (_drawPriorityRoot != null && GodotObject.IsInstanceValid(_drawPriorityRoot))
		{
			_drawPriorityRoot.ZAsRelative = _rootNormalZAsRelative;
			_drawPriorityRoot.ZIndex = _rootNormalZIndex;
		}
		ClearHoverPreviewCard();
		CloseDrawPileModal();
		_controllerPromptRoot?.QueueFree();
		_controllerPromptRoot = null;
	}

	// ---------- View ----------
	private void RefreshView()
	{
		if (_pile == null || _top == null) return;
		EnsureVisibleTopCards(refresh: false);

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

		// visible face-up draw-pile cards
		if (_hoveringTopCard)
			SetHoveringTopCard(false);
		_hoveredVisibleCardIndex = -1;
		ClearHoverPreviewCard();
		foreach (var n in _top.GetChildren()) n.QueueFree();
		int visibleCount = GetVisibleTopCardCount();
		ClampControllerVisibleCardIndex();
		if (visibleCount > 0 && CardViewScene != null)
		{
			Vector2 faceSize = Vector2.Zero;
			for (int i = visibleCount - 1; i >= 0; i--)
			{
				int visibleIndex = i;
				var card = PeekVisible(i);
				if (card == null)
					continue;

				var node = CardViewScene.Instantiate<Control>();
				if (node is BaseCardView view) view.SetData(card);
				else GD.PushError("CardViewScene must inherit BaseCardView.");
				if (node is BaseCardView topCardView)
					topCardView.SetTooltipSuppressed(_topCardTooltipSuppressed);

				faceSize = GetControlSize(node);
				node.Size = faceSize;
				node.Position = GetVisibleCardLocalPosition(visibleIndex).Floor();
				node.SetMeta("visible_index", visibleIndex);
				node.ZAsRelative = true;

				_top.AddChild(node);
				node.MouseEntered += () => OnTopCardMouseEntered(visibleIndex);
				node.MouseExited += OnTopCardMouseExited;
				node.MouseFilter = MouseFilterEnum.Stop;
				node.GuiInput += input => OnVisibleCardGuiInput(input, visibleIndex);
			}

			Vector2 holderSize = new(
				faceSize.X + Mathf.Max(0, visibleCount - 1) * GetVisibleCardSpacing(),
				faceSize.Y);
			_top.CustomMinimumSize = holderSize;
			_top.Size = holderSize;
			ApplyTopCardModulate(force: true);
			ApplyControllerCardFocus();
			RefreshVisibleCardStacking();

			if (AutoPlaceTop)
			{
				PlaceTopHolder(faceSize);
			}

			ApplyEffectiveTooltipSuppression(force: true);
		}

		UpdateControllerPilePrompts();
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

		if (!CanOpenPileModal(_draw))
		{
			AcceptEvent();
			return;
		}

		OpenPileModal(CreateDrawPileModalSnapshot());
		AcceptEvent();
	}

	private void OnDiscardMarkerGuiInput(InputEvent e)
	{
		if (Engine.IsEditorHint() || e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
			return;

		if (CanOpenPileModal(_discard))
			OpenPileModal(new PileModalSnapshot("Discard Pile", new List<CardData>(_discard)));

		AcceptEvent();
	}

	private Rect2 GetDiscardMarkerCanvasRect()
	{
		if (_pile == null)
			return new Rect2();

		Vector2 size = CardBack?.GetSize() ?? new Vector2(48, 72);
		int facing = FacingSign();
		float x = -facing * (Mathf.Max(0, DiscardBehindOffset) + Mathf.Max(0, DrawForwardOffset));
		Rect2 local = new(_pile.Position + new Vector2(Mathf.Floor(x), 0), size);
		Transform2D transform = GetGlobalTransformWithCanvas();
		Vector2 a = transform * local.Position;
		Vector2 b = transform * new Vector2(local.End.X, local.Position.Y);
		Vector2 c = transform * local.End;
		Vector2 d = transform * new Vector2(local.Position.X, local.End.Y);
		return new Rect2(a, Vector2.Zero).Expand(b).Expand(c).Expand(d);
	}

	private void OpenPileModal(PileModalSnapshot snapshot)
	{
		if (!IsInsideTree() || CardViewScene == null || snapshot == null || snapshot.Count <= 0)
			return;

		CloseDrawPileModal();

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
		LayoutPileModal(overlay, snapshot);
		TooltipDisplay.ModalTooltipScope = overlay;
	}

	private void OpenPeekModal(int count, TaskCompletionSource<bool> completion)
	{
		if (!IsInsideTree() || CardViewScene == null || count <= 0)
		{
			completion.TrySetResult(true);
			return;
		}

		CloseDrawPileModal();

		_drawPileModalLayer = new CanvasLayer { Layer = 200 };
		_openDrawPileModalCount++;

		var overlay = new Control
		{
			Name = "PeekModalOverlay",
			MouseFilter = MouseFilterEnum.Stop,
			Size = GetViewportRect().Size
		};
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.GuiInput += e =>
		{
			if (e is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
			{
				CloseDrawPileModal();
				completion.TrySetResult(true);
				AcceptEvent();
				return;
			}

			AcceptEvent();
		};

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
		LayoutPeekModal(overlay, count, completion);
		TooltipDisplay.ModalTooltipScope = overlay;
	}

	private void LayoutPeekModal(Control overlay, int count, TaskCompletionSource<bool> completion)
	{
		Vector2 viewport = GetViewportRect().Size;
		const float cardGap = 12f;
		const float modalGap = 16f;
		Vector2 cardSize = GetCardViewSize();
		float rowWidth = count * cardSize.X + Mathf.Max(0, count - 1) * cardGap;
		float startX = Mathf.Round((viewport.X - rowWidth) * 0.5f);
		float cardY = Mathf.Round((viewport.Y - cardSize.Y) * 0.5f - 24f);
		var selectedDrawIndexes = new HashSet<int>();
		var selectionFrames = new List<Panel>();

		var heading = CreateModalLabel("Peek - choose cards to discard", 20);
		heading.HorizontalAlignment = HorizontalAlignment.Center;
		heading.Size = new Vector2(viewport.X, 28);
		heading.Position = new Vector2(0, cardY - heading.Size.Y - modalGap).Floor();
		overlay.AddChild(heading);

		for (int i = 0; i < count; i++)
		{
			int drawIndex = _draw.Count - 1 - i;
			var cardView = CreateCardView(_draw[drawIndex]);
			if (cardView == null)
				continue;

			cardView.MouseFilter = MouseFilterEnum.Stop;
			cardView.Position = new Vector2(startX + i * (cardSize.X + cardGap), cardY).Floor();
			overlay.AddChild(cardView);

			var frame = new Panel
			{
				MouseFilter = MouseFilterEnum.Ignore,
				Position = cardView.Position,
				Size = cardSize,
				Visible = false
			};
			frame.AddThemeStyleboxOverride("panel", CreateButtonStyle(new Color(0.98f, 0.78f, 0.12f, 0.28f)));
			overlay.AddChild(frame);
			selectionFrames.Add(frame);

			int capturedIndex = drawIndex;
			int capturedFrame = selectionFrames.Count - 1;
			cardView.GuiInput += e =>
			{
				if (e is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
					return;

				if (!selectedDrawIndexes.Add(capturedIndex))
					selectedDrawIndexes.Remove(capturedIndex);

				selectionFrames[capturedFrame].Visible = selectedDrawIndexes.Contains(capturedIndex);
				AcceptEvent();
			};
		}

		var discard = CreateModalActionButton("DISCARD SELECTED", new Color(0.82f, 0.12f, 0.10f));
		discard.Position = new Vector2(Mathf.Round((viewport.X - discard.CustomMinimumSize.X) * 0.5f), cardY + cardSize.Y + modalGap).Floor();
		discard.Size = discard.CustomMinimumSize;
		discard.Pressed += () =>
		{
			foreach (int drawIndex in selectedDrawIndexes.OrderByDescending(i => i))
			{
				if (drawIndex < 0 || drawIndex >= _draw.Count)
					continue;
				var card = _draw[drawIndex];
				_draw.RemoveAt(drawIndex);
				_discard.Add(card);
			}
			CloseDrawPileModal();
			RefreshView();
			EmitSignal(SignalName.TopChanged, Peek());
			completion.TrySetResult(true);
		};
		overlay.AddChild(discard);
	}

	private void LayoutPileModal(Control overlay, PileModalSnapshot snapshot)
	{
		Vector2 viewport = GetViewportRect().Size;
		const float cardGap = 12f;
		const float modalGap = 16f;
		Vector2 cardSize = GetCardViewSize();
		int cardCount = snapshot.Count;
		int orderedCount = snapshot.OrderedCards.Count;
		int unorderedCount = snapshot.UnorderedCards.Count;
		float rowWidth = cardCount * cardSize.X + Mathf.Max(0, cardCount - 1) * cardGap;
		float startX = Mathf.Round((viewport.X - rowWidth) * 0.5f);
		float cardY = Mathf.Round((viewport.Y - cardSize.Y) * 0.5f - 24f);

		var heading = CreateModalLabel($"{snapshot.Title} ({cardCount})", 20);
		heading.HorizontalAlignment = HorizontalAlignment.Center;
		heading.Size = new Vector2(viewport.X, 28);
		heading.Position = new Vector2(0, cardY - heading.Size.Y - modalGap).Floor();
		overlay.AddChild(heading);

		for (int i = 0; i < snapshot.OrderedCards.Count; i++)
		{
			var cardView = CreateCardView(snapshot.OrderedCards[i]);
			if (cardView == null)
				continue;

			cardView.MouseFilter = MouseFilterEnum.Pass;
			cardView.Position = new Vector2(startX + i * (cardSize.X + cardGap), cardY).Floor();
			overlay.AddChild(cardView);
		}

		for (int i = 0; i < snapshot.UnorderedCards.Count; i++)
		{
			int cardIndex = orderedCount + i;
			var cardView = CreateCardView(snapshot.UnorderedCards[i]);
			if (cardView == null)
				continue;

			cardView.MouseFilter = MouseFilterEnum.Pass;
			cardView.Position = new Vector2(startX + cardIndex * (cardSize.X + cardGap), cardY).Floor();
			overlay.AddChild(cardView);
		}

		float closeY = cardY + cardSize.Y + modalGap;
		if (unorderedCount > 0)
		{
			float unorderedStartX = startX + orderedCount * (cardSize.X + cardGap);
			float unorderedWidth = unorderedCount * cardSize.X + Mathf.Max(0, unorderedCount - 1) * cardGap;
			AddUnorderedMarker(overlay, unorderedStartX, closeY, unorderedWidth);
			closeY += 40f;
		}

		var close = CreateCloseButton();
		close.Position = new Vector2(Mathf.Round((viewport.X - close.CustomMinimumSize.X) * 0.5f), closeY).Floor();
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

	private PileModalSnapshot CreateDrawPileModalSnapshot()
	{
		EnsureVisibleTopCards(refresh: false);

		int orderedCount = Mathf.Clamp(GetVisibleTopCardCount(), 1, _draw.Count);
		var orderedCards = new List<CardData>(orderedCount);
		for (int i = 0; i < orderedCount; i++)
			orderedCards.Add(_draw[_draw.Count - 1 - i]);

		var unorderedCards = new List<CardData>();
		for (int i = 0; i < _draw.Count - orderedCount; i++)
			unorderedCards.Add(_draw[i]);

		ShuffleDisplayCards(unorderedCards);
		return new PileModalSnapshot("Draw Pile", orderedCards, unorderedCards);
	}

	private void ShuffleDisplayCards(List<CardData> cards)
	{
		var displayRng = new RandomNumberGenerator();
		displayRng.Randomize();

		for (int i = cards.Count - 1; i > 0; i--)
		{
			int j = (int)displayRng.RandiRange(0, i);
			(cards[i], cards[j]) = (cards[j], cards[i]);
		}
	}

	private void AddUnorderedMarker(Control overlay, float x, float y, float width)
	{
		var bracketColor = new Color(1f, 0.86f, 0.12f);
		const float lineThickness = 2f;
		const float tipHeight = 8f;
		var line = new ColorRect
		{
			Color = bracketColor,
			MouseFilter = MouseFilterEnum.Ignore,
			Position = new Vector2(Mathf.Round(x), Mathf.Round(y)).Floor(),
			Size = new Vector2(Mathf.Max(lineThickness, width), lineThickness)
		};
		overlay.AddChild(line);

		var leftTip = new ColorRect
		{
			Color = bracketColor,
			MouseFilter = MouseFilterEnum.Ignore,
			Position = new Vector2(line.Position.X, line.Position.Y - tipHeight + lineThickness).Floor(),
			Size = new Vector2(lineThickness, tipHeight)
		};
		overlay.AddChild(leftTip);

		var rightTip = new ColorRect
		{
			Color = bracketColor,
			MouseFilter = MouseFilterEnum.Ignore,
			Position = new Vector2(line.Position.X + line.Size.X - lineThickness, line.Position.Y - tipHeight + lineThickness).Floor(),
			Size = new Vector2(lineThickness, tipHeight)
		};
		overlay.AddChild(rightTip);

		var label = CreateModalLabel("Unordered", 20);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.Size = new Vector2(Mathf.Max(96f, width), 28);
		label.Position = new Vector2(Mathf.Round(x + (width - label.Size.X) * 0.5f), y + tipHeight + 4f).Floor();
		overlay.AddChild(label);
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

	private Button CreateModalActionButton(string text, Color color)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(220, 38),
			MouseFilter = MouseFilterEnum.Stop
		};
		button.AddThemeFontSizeOverride("font_size", 20);
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		button.AddThemeColorOverride("font_pressed_color", Colors.White);

		var font = LoadPixelFont();
		if (font != null)
			button.AddThemeFontOverride("font", font);

		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(color));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(color.Lightened(0.12f)));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(color.Darkened(0.22f)));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		ConfigurePixelFont(button.GetThemeFont("font"));
		return button;
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
			MouseFilter = MouseFilterEnum.Stop
		};
		marker.GuiInput += OnDiscardMarkerGuiInput;

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
		if (_hoverPreviewCard != null && GodotObject.IsInstanceValid(_hoverPreviewCard))
			_hoverPreviewCard.Visible = visible;
	}

	private void SetVisibleCardVisible(int visibleIndex, bool visible)
	{
		var control = GetVisibleCardControl(visibleIndex);
		if (control is CanvasItem item)
			item.Visible = visible;
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
		OnVisibleCardGuiInput(e, 0);
	}

	private void OnVisibleCardGuiInput(InputEvent e, int visibleIndex)
	{
		if (e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
			return;

		if (TryGetVisibleCardIndexAtGlobalPosition(GetGlobalMousePosition(), out int interactiveIndex) && interactiveIndex != visibleIndex)
		{
			AcceptEvent();
			return;
		}

		if (!CanPlayTopCard())
		{
			if (!EnableInput && CanOpenDrawPile())
			{
				OpenPileModal(CreateDrawPileModalSnapshot());
			}
			AcceptEvent();
			return;
		}

		RequestVisibleCardPlay(visibleIndex);
		AcceptEvent();
	}

	private void RequestTopCardPlay()
	{
		RequestVisibleCardPlay(0);
	}

	private void RequestVisibleCardPlay(int visibleIndex)
	{
		EnsureVisibleTopCards(refresh: false);
		visibleIndex = Mathf.Clamp(visibleIndex, 0, Mathf.Max(0, GetVisibleTopCardCount() - 1));
		_pendingPlayCardIndex = visibleIndex;
		EmitSignal(SignalName.PlayRequested, this, PeekVisible(visibleIndex));
		if (DiscardOnTopClick) AdvanceTopToDiscard();
	}

	private void OnTopCardMouseEntered(int visibleIndex)
	{
		if (TryGetVisibleCardIndexAtGlobalPosition(GetGlobalMousePosition(), out int interactiveIndex))
			_hoveredVisibleCardIndex = interactiveIndex;
		else
			_hoveredVisibleCardIndex = visibleIndex;
		SetHoveringTopCard(true);
		RefreshVisibleCardStacking();
		ApplyEffectiveTooltipSuppression(force: true);
	}

	private void OnTopCardMouseExited()
	{
		CallDeferred(nameof(RefreshTopCardHoverAfterExit));
	}

	private void RefreshTopCardHoverAfterExit()
	{
		bool hovering = TryGetVisibleCardIndexAtGlobalPosition(GetGlobalMousePosition(), out int hoveredIndex);
		_hoveredVisibleCardIndex = hovering ? hoveredIndex : -1;
		SetHoveringTopCard(hovering);
		RefreshVisibleCardStacking();
		ApplyEffectiveTooltipSuppression(force: true);
	}

	private void OnPileMouseEntered()
	{
		SetHoveringPile(true);
	}

	private void OnPileMouseExited()
	{
		SetHoveringPile(false);
	}

	private void SetHoveringTopCard(bool hovering)
	{
		if (_hoveringTopCard == hovering)
			return;

		_hoveringTopCard = hovering;
		if (hovering)
			_hoverSerial = ++_nextHoverSerial;
		RefreshActiveHoverDeck();
	}

	private void SetHoveringPile(bool hovering)
	{
		if (_hoveringPile == hovering)
			return;

		_hoveringPile = hovering;
		if (hovering)
			_hoverSerial = ++_nextHoverSerial;
		RefreshActiveHoverDeck();
	}

	private void UpdateHoverStateFromMousePosition()
	{
		if (!_hoveringTopCard && !_hoveringPile)
			return;

		if (Deck.IsDrawPileModalOpen || _targetingDimmed || _topCardTooltipSuppressed || !IsVisibleInTree())
		{
			SetHoveringTopCard(false);
			SetHoveringPile(false);
			return;
		}

		if (_hoveringTopCard)
		{
			if (TryGetVisibleCardIndexAtGlobalPosition(GetGlobalMousePosition(), out int hoveredIndex))
			{
				if (_hoveredVisibleCardIndex != hoveredIndex)
				{
					_hoveredVisibleCardIndex = hoveredIndex;
					RefreshVisibleCardStacking();
					ApplyEffectiveTooltipSuppression(force: true);
				}
			}
			else
			{
				_hoveredVisibleCardIndex = -1;
				SetHoveringTopCard(false);
				RefreshVisibleCardStacking();
				ApplyEffectiveTooltipSuppression(force: true);
			}
		}

		if (_hoveringPile && (_pile == null || !_pile.Visible || !_pile.GetGlobalRect().HasPoint(GetGlobalMousePosition())))
			SetHoveringPile(false);
	}

	private bool CanOpenPileModal(List<CardData> cards)
		=> !_targetingDimmed && !Deck.IsDrawPileModalOpen && cards != null && cards.Count > 0;

	private bool CanOpenDrawPile()
		=> CanOpenPileModal(_draw);

	private void UpdateControllerPilePrompts()
	{
		if (Engine.IsEditorHint() || !IsInsideTree())
			return;

		bool show = ControlSchemeFocus.IsXboxFocused && _controllerFocused && !_targetingDimmed && !Deck.IsDrawPileModalOpen && IsVisibleInTree();
		bool showDraw = show && _draw.Count > 0;
		bool showDiscard = show && _discard.Count > 0;
		if (!showDraw && !showDiscard)
		{
			ClearControllerPilePrompts();
			return;
		}

		_controllerPromptRoot ??= new Control
		{
			Name = "ControllerPilePrompts",
			MouseFilter = MouseFilterEnum.Ignore,
			ZAsRelative = false,
			ZIndex = HoverDrawPriority + 10
		};
		if (_controllerPromptRoot.GetParent() == null)
			AddChild(_controllerPromptRoot);

		foreach (var child in _controllerPromptRoot.GetChildren())
			child.QueueFree();

		if (showDraw)
			_controllerPromptRoot.AddChild(CreateControllerPrompt(LoadDrawPilePromptIcon(), GetDrawPilePromptPosition()));
		if (showDiscard)
			_controllerPromptRoot.AddChild(CreateControllerPrompt(LoadDiscardPilePromptIcon(), GetDiscardPilePromptPosition()));
	}

	private void ClearControllerPilePrompts()
	{
		if (_controllerPromptRoot == null)
			return;

		foreach (var child in _controllerPromptRoot.GetChildren())
			child.QueueFree();
	}

	private TextureRect CreateControllerPrompt(Texture2D icon, Vector2 position)
	{
		Vector2 size = icon?.GetSize() ?? new Vector2(20, 16);
		return new TextureRect
		{
			Texture = icon,
			StretchMode = TextureRect.StretchModeEnum.Keep,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			CustomMinimumSize = size,
			Size = size,
			Position = position.Floor(),
			MouseFilter = MouseFilterEnum.Ignore
		};
	}

	private Vector2 GetDrawPilePromptPosition()
	{
		Vector2 iconSize = LoadDrawPilePromptIcon()?.GetSize() ?? new Vector2(20, 16);
		Vector2 pileSize = CardBack?.GetSize() ?? new Vector2(48, 72);
		Vector2 pilePos = _pile?.Position ?? Vector2.Zero;
		return pilePos + new Vector2(pileSize.X - iconSize.X, pileSize.Y - iconSize.Y);
	}

	private Vector2 GetDiscardPilePromptPosition()
	{
		Vector2 iconSize = LoadDiscardPilePromptIcon()?.GetSize() ?? new Vector2(20, 16);
		Vector2 cardSize = CardBack?.GetSize() ?? new Vector2(48, 72);
		Vector2 discardPos = _pile?.Position ?? Vector2.Zero;
		int facing = FacingSign();
		float x = -facing * (Mathf.Max(0, DiscardBehindOffset) + Mathf.Max(0, DrawForwardOffset));
		return discardPos + new Vector2(Mathf.Floor(x) + cardSize.X - iconSize.X, cardSize.Y - iconSize.Y);
	}

	private static Texture2D LoadDrawPilePromptIcon()
		=> _drawPilePromptIcon ??= ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Icons/ui_buttons_20x16/xbox-LT-16.png");

	private static Texture2D LoadDiscardPilePromptIcon()
		=> _discardPilePromptIcon ??= ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Icons/ui_buttons_20x16/xbox-RT-16.png");

	private void RefreshVisibleTopCardCount()
	{
		EnsureVisibleTopCards(refresh: false);
		int next = GetVisibleTopCardCount();
		if (next == _visibleTopCardCount)
			return;

		_visibleTopCardCount = next;
		ClampControllerVisibleCardIndex();
		RefreshView();
	}

	private int GetVisibleTopCardCount()
		=> Mathf.Clamp(1 + GetExtraVisibleCardsAmount(), 0, _draw.Count);

	private int GetDesiredVisibleTopCardCount(int minimumVisibleCards = 0)
		=> Mathf.Max(minimumVisibleCards, 1 + GetExtraVisibleCardsAmount());

	private int GetExtraVisibleCardsAmount()
		=> GetParent() is IDamageable owner ? Mathf.Max(0, owner.GetStatusAmount(ExtraVisibleCardsStatusId)) : 0;

	private bool EnsureVisibleTopCards(bool refresh = true, int minimumVisibleCards = 0)
	{
		int desired = GetDesiredVisibleTopCardCount(minimumVisibleCards);
		if (_draw.Count >= desired || _discard.Count == 0)
			return _draw.Count > 0;

		bool topWillChange = _draw.Count == 0;
		var recycled = new List<CardData>(_discard);
		_discard.Clear();
		Shuffle(recycled);
		if (_draw.Count == 0)
			_draw.AddRange(recycled);
		else
			_draw.InsertRange(0, recycled);

		if (topWillChange)
			EmitSignal(SignalName.Shuffled, this, Peek());
		if (refresh)
		{
			RefreshView();
			EmitSignal(SignalName.TopChanged, Peek());
		}

		return _draw.Count > 0;
	}

	private int GetDrawIndexForVisibleIndex(int visibleIndex)
		=> _draw.Count - 1 - Mathf.Max(0, visibleIndex);

	private int GetPresentationCardIndex(CardData card)
	{
		if (_pendingPlayCardIndex >= 0 && PeekVisible(_pendingPlayCardIndex) == card)
			return _pendingPlayCardIndex;

		for (int i = 0; i < GetVisibleTopCardCount(); i++)
			if (PeekVisible(i) == card)
				return i;

		return 0;
	}

	private int GetFocusedPlayableVisibleIndex()
	{
		if (_pendingPlayCardIndex >= 0)
			return Mathf.Clamp(_pendingPlayCardIndex, 0, Mathf.Max(0, GetVisibleTopCardCount() - 1));

		return _controllerFocused ? _controllerVisibleCardIndex : Mathf.Max(0, _hoveredVisibleCardIndex);
	}

	private Vector2 GetVisibleCardLocalPosition(int visibleIndex)
		=> new(Mathf.Max(0, visibleIndex) * GetVisibleCardSpacing(), 0f);

	private int GetVisibleCardSpacing()
	{
		int extraCards = Mathf.Max(0, GetVisibleTopCardCount() - 1);
		if (extraCards <= 1)
			return Mathf.Max(1, VisibleTopCardSpacing);

		int squeezed = Mathf.RoundToInt(96f / (extraCards + 1));
		return Mathf.Max(Mathf.Max(1, MinVisibleTopCardSpacing), squeezed);
	}

	private int GetVisibleIndexForCardControl(Control control)
	{
		if (control != null && control.HasMeta("visible_index"))
			return control.GetMeta("visible_index").AsInt32();

		return 0;
	}

	private bool TryGetVisibleCardIndexAtGlobalPosition(Vector2 globalPosition, out int visibleIndex)
	{
		visibleIndex = -1;
		if (_top == null)
			return false;

		for (int i = _top.GetChildCount() - 1; i >= 0; i--)
		{
			if (_top.GetChild(i) is not Control control || !control.Visible)
				continue;

			if (!control.GetGlobalRect().HasPoint(globalPosition))
				continue;

			visibleIndex = GetVisibleIndexForCardControl(control);
			return true;
		}

		return false;
	}

	private void ClampControllerVisibleCardIndex()
	{
		int count = GetVisibleTopCardCount();
		_controllerVisibleCardIndex = count <= 0 ? 0 : Mathf.Clamp(_controllerVisibleCardIndex, 0, count - 1);
	}

	private void ApplyControllerCardFocus()
	{
		foreach (var card in GetTopCardViews())
			card.SetControllerFocus(_controllerFocused && GetVisibleIndexForCardControl(card) == _controllerVisibleCardIndex);
	}

	private void RefreshVisibleCardStacking()
	{
		if (_top == null)
			return;

		int visibleCount = GetVisibleTopCardCount();
		foreach (Node child in _top.GetChildren())
		{
			if (child is not Control control)
				continue;

			int visibleIndex = GetVisibleIndexForCardControl(control);
			bool raised = visibleIndex == _pendingPlayCardIndex
				|| visibleIndex == _hoveredVisibleCardIndex
				|| (_controllerFocused && visibleIndex == _controllerVisibleCardIndex);
			control.ZIndex = raised
				? visibleCount + 100
				: visibleCount - visibleIndex;
		}
	}

	private int PosMod(int value, int length)
		=> length <= 0 ? 0 : ((value % length) + length) % length;

	private bool CanPlayTopCard()
		=> EnableInput && !_deadDimmed && !_targetingDimmed && !_turnDimmed && !_energyDimmed && !Deck.IsDrawPileModalOpen;

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

	private Rect2 GetTopHoverCanvasRect()
	{
		if (_top == null || !_top.Visible)
			return new Rect2();

		bool hasRect = false;
		Rect2 rect = default;
		MergeHoverRect(_top, ref rect, ref hasRect);
		return hasRect ? rect : new Rect2();
	}

	private Rect2 GetDeckHoverCanvasRect()
	{
		Rect2 topRect = GetTopHoverCanvasRect();
		if (topRect.Size.X > 0f && topRect.Size.Y > 0f)
			return topRect;

		bool hasRect = false;
		Rect2 rect = default;
		MergeHoverRect(_pile, ref rect, ref hasRect);
		return hasRect ? rect : new Rect2();
	}

	private void MergeHoverRect(Control control, ref Rect2 rect, ref bool hasRect)
	{
		if (control == null || !control.Visible)
			return;

		MergeCanvasRect(GetControlCanvasRect(control), ref rect, ref hasRect);
		foreach (Node child in control.GetChildren())
		{
			if (child is Control childControl && childControl.Visible)
				MergeCanvasRect(GetControlCanvasRect(childControl), ref rect, ref hasRect);
		}
	}

	private void MergeCanvasRect(Rect2 next, ref Rect2 rect, ref bool hasRect)
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

	private bool IsHoverCandidate()
	{
		return IsInsideTree()
			&& IsVisibleInTree()
			&& !Deck.IsDrawPileModalOpen
			&& !_targetingDimmed
			&& !_topCardTooltipSuppressed
			&& (_hoveringTopCard || _hoveringPile || _ownerHovering);
	}

	private static void RefreshActiveHoverDeck()
	{
		Deck next = null;
		int bestSerial = int.MinValue;
		int bestPriority = int.MinValue;

		foreach (Deck deck in _hoverDecks.ToArray())
		{
			if (deck == null || !GodotObject.IsInstanceValid(deck))
				continue;

			if (!deck.IsHoverCandidate())
				continue;

			int priority = deck.GetEffectiveDrawPriority();
			if (next == null
				|| deck._hoverSerial > bestSerial
				|| (deck._hoverSerial == bestSerial && priority > bestPriority))
			{
				next = deck;
				bestSerial = deck._hoverSerial;
				bestPriority = priority;
			}
		}

		if (_activeHoverDeck == next)
			return;

		_activeHoverDeck = next;
		foreach (Deck deck in _hoverDecks.ToArray())
		{
			if (deck == null || !GodotObject.IsInstanceValid(deck))
				continue;

			deck.SetActiveHover(deck == _activeHoverDeck);
		}

		RefreshOwnerStackOrder();
	}

	private void SetActiveHover(bool active)
	{
		if (_isActiveHover == active)
			return;

		_isActiveHover = active;
		ApplyEffectiveVisualState();
		EmitSignal(SignalName.ActiveHoverChanged, active);
	}

	private void ApplyEffectiveVisualState()
	{
		bool hoverRestoresTurnDim = !_deadDimmed && _isActiveHover && _turnDimmed && !_energyDimmed && !_targetingDimmed;
		bool wholeDeckDimmed = !_deadDimmed && (_targetingDimmed || _turnDimmed) && !hoverRestoresTurnDim;
		Modulate = wholeDeckDimmed ? DimmedModulate : _normalModulate;
		ApplyTopCardModulate();
		ApplyDrawPriority();
		ApplyTopCardDrawPriority();
		ApplyEffectiveTooltipSuppression();
		UpdateControllerPilePrompts();
	}

	private void ApplyTopCardModulate(bool force = false)
	{
		bool topCardsDimmed = _deadDimmed || (_energyDimmed && !_targetingDimmed && !_turnDimmed) || (_controllerUnfocused && !_controllerFocused && !_targetingDimmed);
		if (!force && _topCardsDimmed == topCardsDimmed)
			return;

		_topCardsDimmed = topCardsDimmed;
		Color topModulate = _controllerUnfocused && !_controllerFocused && !_deadDimmed && !_energyDimmed && !_targetingDimmed
			? ControllerUnfocusedModulate
			: topCardsDimmed ? DimmedModulate : _normalModulate;
		if (_top != null)
		{
			foreach (Node child in _top.GetChildren())
			{
				if (child is CanvasItem item)
					item.Modulate = topModulate;
			}
		}

		if (_hoverPreviewCard != null && GodotObject.IsInstanceValid(_hoverPreviewCard))
			_hoverPreviewCard.Modulate = topModulate;
	}

	private void ApplyDrawPriority()
	{
		ZAsRelative = false;
		ZIndex = GetEffectiveDrawPriority();
		if (_top != null)
		{
			_top.ZAsRelative = false;
			_top.ZIndex = Mathf.Max(ZIndex, FaceUpDrawPriority);
		}

		if (_drawPriorityRoot == null || !GodotObject.IsInstanceValid(_drawPriorityRoot))
			return;

		_drawPriorityRoot.ZAsRelative = false;
		_drawPriorityRoot.ZIndex = ZIndex;
	}

	private void ApplyTopCardDrawPriority()
	{
		ClearHoverPreviewCard();
		RefreshVisibleCardStacking();
	}

	private void ShowHoverPreviewCard()
	{
		int visibleIndex = Mathf.Max(0, _hoveredVisibleCardIndex);
		var source = GetVisibleCardControl(visibleIndex);
		var card = PeekVisible(visibleIndex);
		if (source == null || !GodotObject.IsInstanceValid(source) || card == null || CardViewScene == null)
			return;

		if (_hoverPreviewCard == null || !GodotObject.IsInstanceValid(_hoverPreviewCard))
		{
			_hoverPreviewCard = CreateCardView(card);
			_hoverPreviewCard.Name = "HoveredTopCardPreview";
			_hoverPreviewCard.MouseFilter = MouseFilterEnum.Ignore;
			_hoverPreviewCard.ZAsRelative = false;
			_hoverPreviewCard.ZIndex = HoverDrawPriority;
			if (_hoverPreviewCard is BaseCardView previewView)
				previewView.SetTooltipSuppressed(true);
			GetHoverCardLayer().AddChild(_hoverPreviewCard);
		}
		else
		{
			ApplyHoverCardLayerPriority();
		}

		_hoverPreviewCard.Size = source.Size;
		_hoverPreviewCard.Position = source.GetGlobalTransformWithCanvas().Origin.Floor();
		_hoverPreviewCard.Modulate = _topCardsDimmed ? (_controllerUnfocused && !_controllerFocused ? ControllerUnfocusedModulate : DimmedModulate) : _normalModulate;
		_hoverPreviewCard.Visible = true;
		if (_hoverPreviewCard.GetParent() is Node parent)
			parent.MoveChild(_hoverPreviewCard, parent.GetChildCount() - 1);
	}

	private void ClearHoverPreviewCard()
	{
		if (_hoverPreviewCard == null)
			return;

		if (GodotObject.IsInstanceValid(_hoverPreviewCard))
			_hoverPreviewCard.QueueFree();

		_hoverPreviewCard = null;
	}

	private bool IsMouseOverTopCard()
	{
		return TryGetVisibleCardIndexAtGlobalPosition(GetGlobalMousePosition(), out _);
	}

	private Control GetCurrentTopCardControl()
		=> GetVisibleCardControl(0);

	private Control GetVisibleCardControl(int visibleIndex)
	{
		if (_top == null)
			return null;

		foreach (Node child in _top.GetChildren())
		{
			if (child is not Control control)
				continue;

			if (GetVisibleIndexForCardControl(control) == visibleIndex)
				return control;
		}

		return null;
	}

	private CanvasLayer GetHoverCardLayer()
	{
		if (_hoverCardLayer != null && GodotObject.IsInstanceValid(_hoverCardLayer))
		{
			ApplyHoverCardLayerPriority();
			return _hoverCardLayer;
		}

		_hoverCardLayer = new CanvasLayer
		{
			Name = "HoveredCardLayer",
			Layer = GetHoverCardLayerPriority()
		};
		(GetTree().CurrentScene as Node ?? GetTree().Root).AddChild(_hoverCardLayer);
		return _hoverCardLayer;
	}

	private void ApplyHoverCardLayerPriority()
	{
		if (_hoverCardLayer != null && GodotObject.IsInstanceValid(_hoverCardLayer))
			_hoverCardLayer.Layer = GetHoverCardLayerPriority();
	}

	private int GetHoverCardLayerPriority()
		=> Side == DeckSide.Enemy && _isActiveHover ? EnemyIntentHoverCardCanvasLayer : HoverCardCanvasLayer;

	private int GetEffectiveDrawPriority()
		=> _isActiveHover ? HoverDrawPriority : BaseDrawPriority;

	private static void RefreshOwnerStackOrder()
	{
		var parents = new List<Node>();
		foreach (Deck deck in _hoverDecks.ToArray())
		{
			if (deck == null || !GodotObject.IsInstanceValid(deck))
				continue;

			deck.ApplyDrawPriority();
			if (deck._drawPriorityRoot == null || !GodotObject.IsInstanceValid(deck._drawPriorityRoot))
				continue;

			Node parent = deck._drawPriorityRoot.GetParent();
			if (parent != null && !parents.Contains(parent))
				parents.Add(parent);
		}

		foreach (Node parent in parents)
		{
			var siblings = new List<Deck>();
			foreach (Deck siblingDeck in _hoverDecks)
			{
				if (siblingDeck == null || !GodotObject.IsInstanceValid(siblingDeck) || siblingDeck._drawPriorityRoot == null || !GodotObject.IsInstanceValid(siblingDeck._drawPriorityRoot))
					continue;
				if (siblingDeck._drawPriorityRoot.GetParent() != parent)
					continue;

				siblings.Add(siblingDeck);
			}

			siblings.Sort((a, b) => a.GetEffectiveDrawPriority().CompareTo(b.GetEffectiveDrawPriority()));
			foreach (Deck siblingDeck in siblings)
				parent.CallDeferred(Node.MethodName.MoveChild, siblingDeck._drawPriorityRoot, parent.GetChildCount() - 1);
		}
	}

	private void ApplyEffectiveTooltipSuppression(bool force = false)
	{
		bool suppressed = _topCardTooltipSuppressed || _activeHoverDeck != this || !_hoveringTopCard || _hoveredVisibleCardIndex < 0;
		if (!force && _effectiveTooltipSuppressed == suppressed)
			return;

		_effectiveTooltipSuppressed = suppressed;
		foreach (var card in GetTopCardViews())
			card.SetTooltipSuppressed(suppressed || GetVisibleIndexForCardControl(card) != _hoveredVisibleCardIndex);
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
		Vector2 holderSize = new(
			faceSize.X + Mathf.Max(0, GetVisibleTopCardCount() - 1) * GetVisibleCardSpacing(),
			faceSize.Y);
		_top.CustomMinimumSize = holderSize;
		_top.Size = holderSize;
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
