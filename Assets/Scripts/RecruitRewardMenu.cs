using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class RecruitRewardMenu : Control
{
	private const string SmallCardScenePath = "res://Assets/Components/SmallCard.tscn";
	private const string CardCountIconPath = "res://Assets/Sprites/Icons/placeholder-icons/1-bit_Pixel_Icons/Sprites_Cropped/Boardgames_Cards_Deck_Pile.png";

	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }

	private PackedScene _smallCardScene;
	private RunState _run;
	private Button _confirmButton;
	private HeroDef _selectedHero;
	private VBoxContainer _deckViewerRoot;
	private HBoxContainer _deckCardRow;
	private Button _deckLeftButton;
	private Button _deckRightButton;
	private List<CardData> _previewDeckCards = new();
	private int _deckScrollIndex;
	private readonly List<HeroOffer> _heroOffers = new();
	private const int MaxVisibleDeckCards = 6;
	private const int SmallCardWidth = 140;
	private const int DeckCardGap = 10;
	private const float RecruitContentHeight = 398f;

	private sealed class HeroOffer
	{
		public HeroDef Hero;
		public RecruitSpriteChoice Button;
	}

	private sealed partial class RecruitSpriteChoice : Button
	{
		private const float FrameSeconds = 0.08f;
		private const float UnitGroundY = 76f;
		private readonly List<Texture2D> _frames = new();
		private TargetRing _targetRing;
		private TextureRect _sprite;
		private Label _nameLabel;
		private HPBar _hpBar;
		private Label _cardCountLabel;
		private TextureRect _cardIcon;
		private string _heroName = "";
		private int _hp;
		private int _maxHp = 1;
		private int _deckCount;
		private bool _stableTextureRectValid;
		private Rect2 _stableTextureRect;
		private float _frameTime;
		private int _frameIndex;
		private bool _selected;

		public bool FaceRight { get; set; } = true;

		public bool Selected
		{
			get => _selected;
			set
			{
				_selected = value;
				if (_targetRing != null)
				{
					_targetRing.Visible = _selected;
					_targetRing.SetHover(_selected);
				}
				QueueRedraw();
			}
		}

		public RecruitSpriteChoice()
		{
			CustomMinimumSize = new Vector2(150, 118);
			FocusMode = FocusModeEnum.All;
			MouseFilter = MouseFilterEnum.Stop;
			TextureFilter = TextureFilterEnum.Nearest;
			AddThemeStyleboxOverride("normal", EmptyStyle());
			AddThemeStyleboxOverride("hover", EmptyStyle());
			AddThemeStyleboxOverride("pressed", EmptyStyle());
			AddThemeStyleboxOverride("focus", EmptyStyle());
		}

		public override void _Ready()
		{
			_targetRing = new TargetRing
			{
				Name = "SelectedTargetRing",
				Visible = _selected,
				MouseFilter = MouseFilterEnum.Ignore,
				BaseColor = new Color(0.93f, 0.66f, 0.12f, 1f),
				FillColor = new Color(0.93f, 0.66f, 0.12f, 0.18f),
				ShadowColor = new Color(0.18f, 0.10f, 0f, 0.9f),
				Thickness = 2,
				Pulse = true,
				HoverFill = true,
				ZIndex = 0
			};
			AddChild(_targetRing);
			_targetRing.SetHover(_selected);

			_sprite = new TextureRect
			{
				MouseFilter = MouseFilterEnum.Ignore,
				TextureFilter = TextureFilterEnum.Nearest,
				StretchMode = TextureRect.StretchModeEnum.Keep,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				ZIndex = 5
			};
			AddChild(_sprite);

			_nameLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
			_nameLabel.AddThemeFontOverride("font", ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf"));
			_nameLabel.AddThemeFontSizeOverride("font_size", 16);
			_nameLabel.AddThemeColorOverride("font_color", Colors.White);
			_nameLabel.MouseFilter = MouseFilterEnum.Ignore;
			_nameLabel.ZIndex = 10;
			AddChild(_nameLabel);

			_cardCountLabel = new Label { Text = "0", VerticalAlignment = VerticalAlignment.Center };
			_cardCountLabel.AddThemeFontOverride("font", ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf"));
			_cardCountLabel.AddThemeFontSizeOverride("font_size", 16);
			_cardCountLabel.AddThemeColorOverride("font_color", Colors.White);
			_cardCountLabel.MouseFilter = MouseFilterEnum.Ignore;
			_cardCountLabel.ZIndex = 10;
			AddChild(_cardCountLabel);

			_cardIcon = new TextureRect
			{
				Texture = ResourceLoader.Load<Texture2D>(CardCountIconPath),
				CustomMinimumSize = new Vector2(16, 16),
				TextureFilter = TextureFilterEnum.Nearest,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepCentered,
				MouseFilter = MouseFilterEnum.Ignore,
				ZIndex = 10
			};
			AddChild(_cardIcon);

			_hpBar = new HPBar
			{
				CustomMinimumSize = new Vector2(96, 20),
				Size = new Vector2(96, 20),
				ShowText = true,
				ZIndex = 10
			};
			AddChild(_hpBar);

			ApplyOverlayData();
			UpdateRingLayout();
		}

		public override void _Process(double delta)
		{
			if (_frames.Count <= 1)
				return;

			_frameTime += (float)delta;
			while (_frameTime >= FrameSeconds)
			{
				_frameTime -= FrameSeconds;
				_frameIndex = (_frameIndex + 1) % _frames.Count;
				UpdateRingLayout();
				QueueRedraw();
			}
		}

		public void SetHero(HeroDef hero, int deckCount)
		{
			_frames.Clear();
			_frameIndex = 0;
			_frameTime = 0f;
			_stableTextureRectValid = false;
			FaceRight = hero?.FaceRight ?? true;
			_heroName = hero?.HeroName?.ToUpperInvariant() ?? "";
			_hp = Mathf.Max(1, hero?.StartingMaxHP ?? 1);
			_maxHp = _hp;
			_deckCount = deckCount;
			ApplyOverlayData();

			if (hero != null && !string.IsNullOrWhiteSpace(hero.SpriteRootPath) && !string.IsNullOrWhiteSpace(hero.IdleFolderName))
			{
				string folderPath = $"{hero.SpriteRootPath.TrimEnd('/')}/{hero.IdleFolderName}";
				using var dir = DirAccess.Open(folderPath);
				if (dir != null)
				{
					foreach (string file in dir.GetFiles().Where(f => f.EndsWith(".png")).OrderBy(ParseFrameNumber))
					{
						var texture = ResourceLoader.Load<Texture2D>($"{folderPath}/{file}");
						if (texture != null)
							_frames.Add(texture);
					}
				}
			}

			BuildStableSpriteRect();
			UpdateRingLayout();
			QueueRedraw();
		}

		private Texture2D CurrentFrame()
			=> _frames.Count == 0 ? null : _frames[Mathf.Clamp(_frameIndex, 0, _frames.Count - 1)];

		private void UpdateRingLayout()
		{
			if (_targetRing == null)
				return;

			Texture2D frame = CurrentFrame();
			if (frame == null)
				return;

			Vector2 textureSize = frame.GetSize();
			Rect2 stableTextureRect = _stableTextureRectValid ? _stableTextureRect : new Rect2(Vector2.Zero, textureSize);
			Vector2 drawPosition = new(
				Mathf.Round(Size.X * 0.5f - (stableTextureRect.Position.X + stableTextureRect.Size.X * 0.5f)),
				Mathf.Round(UnitGroundY - stableTextureRect.End.Y)
			);
			if (_sprite != null)
			{
				_sprite.Texture = frame;
				_sprite.Position = drawPosition;
				_sprite.CustomMinimumSize = textureSize;
				_sprite.Size = textureSize;
				_sprite.FlipH = !FaceRight;
			}

			Rect2 spriteRect = SpriteBounds.TryGetSprite2DRect(frame, drawPosition, Vector2.Zero, Vector2.One, false, !FaceRight, out Rect2 opaqueRect)
				? opaqueRect
				: new Rect2(drawPosition, textureSize);
			if (_stableTextureRectValid)
				spriteRect = new Rect2(drawPosition + _stableTextureRect.Position, _stableTextureRect.Size);

			if (_nameLabel != null && _cardCountLabel != null && _cardIcon != null)
			{
				float nameWidth = GetLabelWidth(_nameLabel);
				float countWidth = GetLabelWidth(_cardCountLabel);
				float totalWidth = nameWidth + 14f + countWidth + 2f + 16f;
				float centerX = spriteRect.Position.X + spriteRect.Size.X * 0.5f;
				float x = Mathf.Round(centerX - totalWidth * 0.5f);
				float y = Mathf.Round(spriteRect.End.Y - 14f);
				_nameLabel.Size = new Vector2(nameWidth, 20);
				_nameLabel.Position = new Vector2(x, y);
				_cardCountLabel.Size = new Vector2(countWidth, 20);
				_cardCountLabel.Position = new Vector2(Mathf.Round(x + nameWidth + 14f), y);
				_cardIcon.Size = new Vector2(16, 16);
				_cardIcon.Position = new Vector2(Mathf.Round(x + nameWidth + 14f + countWidth + 2f), y + 2f);
			}

			if (_hpBar != null)
			{
				_hpBar.Size = new Vector2(96, 20);
				float centerX = spriteRect.Position.X + spriteRect.Size.X * 0.5f;
				_hpBar.Position = new Vector2(
					Mathf.Round(centerX - 48f),
					Mathf.Round(spriteRect.End.Y + 4f)
				);
			}

			float ringWidth = Mathf.Max(42f, spriteRect.Size.X * 1.8f);
			float ringHeight = Mathf.Max(18f, ringWidth * 0.62f);
			_targetRing.CustomMinimumSize = new Vector2(ringWidth, ringHeight);
			_targetRing.Size = new Vector2(ringWidth, ringHeight);
			_targetRing.Position = new Vector2(
				Mathf.Round(spriteRect.Position.X + (spriteRect.Size.X - ringWidth) * 0.5f),
				Mathf.Round(spriteRect.End.Y - ringHeight * 0.5f - 2f)
			);
		}

		private static int ParseFrameNumber(string file)
			=> int.TryParse(file.GetBaseName(), out int number) ? number : int.MaxValue;

		private void BuildStableSpriteRect()
		{
			_stableTextureRectValid = false;
			foreach (Texture2D frame in _frames)
			{
				if (!SpriteBounds.TryGetSprite2DRect(frame, Vector2.Zero, Vector2.Zero, Vector2.One, false, !FaceRight, out Rect2 frameRect))
					continue;

				if (!_stableTextureRectValid)
				{
					_stableTextureRect = frameRect;
					_stableTextureRectValid = true;
				}
				else
				{
					_stableTextureRect = _stableTextureRect.Merge(frameRect);
				}
			}
		}

		private void ApplyOverlayData()
		{
			if (_nameLabel != null)
				_nameLabel.Text = _heroName;
			if (_hpBar != null)
				_hpBar.Set(_hp, _maxHp);
			if (_cardCountLabel != null)
				_cardCountLabel.Text = _deckCount.ToString();
		}

		private static float GetLabelWidth(Label label)
		{
			Font font = label.GetThemeFont("font");
			int fontSize = label.GetThemeFontSize("font_size");
			if (font == null || fontSize <= 0)
				return Mathf.Max(1f, label.Text.Length * 10f);

			return Mathf.Ceil(font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, fontSize).X);
		}

		private static StyleBoxFlat EmptyStyle()
			=> new() { BgColor = Colors.Transparent, BorderColor = Colors.Transparent, AntiAliasing = false };
	}

	public override void _Ready()
	{
		BodyFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/Minecraft.ttf");
		BoldFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf");
		_smallCardScene = ResourceLoader.Load<PackedScene>(SmallCardScenePath);
		Build();
	}

	private void Build()
	{
		_run = GetNode<RunState>("/root/RunState");
		List<HeroDef> offers = _run.GetRecruitOffers();
		if (offers.Count == 0)
		{
			_run.ChooseRecruit("");
			_run.CallDeferred(nameof(RunState.GoToMap));
			return;
		}

		var bg = new ColorRect { Color = new Color(0.03f, 0.07f, 0.09f) };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		AddChild(margin);

		var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		stack.AddThemeConstantOverride("separation", 10);
		margin.AddChild(stack);

		float viewportHeight = GetViewportRect().Size.Y;
		float topGap = Mathf.Clamp((viewportHeight - RecruitContentHeight) * 0.5f - 18f, 44f, 132f);
		var topSpacer = new Control { CustomMinimumSize = new Vector2(0, topGap) };
		stack.AddChild(topSpacer);

		var offerCenter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		stack.AddChild(offerCenter);

		var offerStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		offerStack.AddThemeConstantOverride("separation", 6);
		offerCenter.AddChild(offerStack);

		var title = CreateCenteredLabel("Choose someone to add to your crew", 20, BoldFont);
		offerStack.AddChild(title);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 22);
		offerStack.AddChild(row);

		foreach (HeroDef hero in offers)
		{
			RecruitSpriteChoice offer = CreateHeroOffer(hero);
			_heroOffers.Add(new HeroOffer { Hero = hero, Button = offer });
			row.AddChild(offer);
		}

		_deckViewerRoot = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_deckViewerRoot.AddThemeConstantOverride("separation", 0);
		stack.AddChild(_deckViewerRoot);

		CreateAnchoredConfirmButton();
		SelectHero(offers[0]);
	}

	private RecruitSpriteChoice CreateHeroOffer(HeroDef hero)
	{
		var choice = new RecruitSpriteChoice();
		DeckList deck = GetHeroDeck(hero);
		int deckCount = deck?.Cards?.Count ?? 0;
		choice.SetHero(hero, deckCount);
		choice.TooltipText = $"{hero.HeroName}\n{Mathf.Max(1, hero.StartingMaxHP)} HP";
		choice.Pressed += () => SelectHero(hero);
		return choice;
	}

	private void SelectHero(HeroDef hero)
	{
		_selectedHero = hero;
		foreach (HeroOffer offer in _heroOffers)
			if (offer.Button != null)
				offer.Button.Selected = offer.Hero == _selectedHero;

		if (_confirmButton != null)
			_confirmButton.Disabled = _selectedHero == null;

		ShowSelectedHeroDeck();
	}

	private void ConfirmRecruit()
	{
		if (_selectedHero == null)
			return;

		_run.ChooseRecruit(_selectedHero.Id);
		_run.GoToMap();
	}

	private void ShowSelectedHeroDeck()
	{
		if (_deckViewerRoot == null)
			return;

		ClearChildren(_deckViewerRoot);
		DeckList deck = GetHeroDeck(_selectedHero);
		_previewDeckCards = deck?.Cards != null ? deck.Cards.ToList() : new List<CardData>();
		_deckScrollIndex = 0;

		var deckColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		deckColumn.AddThemeConstantOverride("separation", 0);
		_deckViewerRoot.AddChild(deckColumn);

		var deckArea = new Control
		{
			CustomMinimumSize = new Vector2(0, 210),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		deckColumn.AddChild(deckArea);

		var deckCenter = new CenterContainer();
		deckCenter.SetAnchorsPreset(LayoutPreset.FullRect);
		deckArea.AddChild(deckCenter);

		int visibleDeckCards = Mathf.Min(GetVisibleDeckCardCount(), Mathf.Max(1, _previewDeckCards.Count));
		float deckScrollerWidth = 42f + 10f + visibleDeckCards * SmallCardWidth + Mathf.Max(0, visibleDeckCards - 1) * DeckCardGap + 10f + 42f;
		var deckScroller = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(deckScrollerWidth, 210),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter
		};
		deckScroller.AddThemeConstantOverride("separation", 10);
		deckCenter.AddChild(deckScroller);

		_deckLeftButton = CreateButton("<");
		_deckLeftButton.CustomMinimumSize = new Vector2(42, 210);
		_deckLeftButton.Pressed += () => ScrollDeck(-1);
		deckScroller.AddChild(_deckLeftButton);

		_deckCardRow = new HBoxContainer();
		_deckCardRow.AddThemeConstantOverride("separation", 10);
		deckScroller.AddChild(_deckCardRow);

		_deckRightButton = CreateButton(">");
		_deckRightButton.CustomMinimumSize = new Vector2(42, 210);
		_deckRightButton.Pressed += () => ScrollDeck(1);
		deckScroller.AddChild(_deckRightButton);

		deckColumn.AddChild(new Control { CustomMinimumSize = new Vector2(0, 58) });
		RefreshDeckPreview();
	}

	private void CreateAnchoredConfirmButton()
	{
		_confirmButton = CreateButton("ADD TO CREW");
		_confirmButton.CustomMinimumSize = new Vector2(180, 42);
		_confirmButton.Disabled = _selectedHero == null;
		_confirmButton.Pressed += ConfirmRecruit;
		_confirmButton.SetAnchorsPreset(LayoutPreset.BottomWide);
		_confirmButton.AnchorLeft = 0.5f;
		_confirmButton.AnchorRight = 0.5f;
		_confirmButton.OffsetLeft = -90f;
		_confirmButton.OffsetRight = 90f;
		_confirmButton.OffsetTop = -54f;
		_confirmButton.OffsetBottom = -12f;
		AddChild(_confirmButton);
	}

	private void ScrollDeck(int direction)
	{
		int visibleDeckCards = GetVisibleDeckCardCount();
		int maxIndex = Mathf.Max(0, _previewDeckCards.Count - visibleDeckCards);
		_deckScrollIndex = Mathf.Clamp(_deckScrollIndex + direction, 0, maxIndex);
		RefreshDeckPreview();
	}

	private void RefreshDeckPreview()
	{
		if (_deckCardRow == null)
			return;

		int visibleDeckCards = GetVisibleDeckCardCount();
		int maxIndex = Mathf.Max(0, _previewDeckCards.Count - visibleDeckCards);
		_deckScrollIndex = Mathf.Clamp(_deckScrollIndex, 0, maxIndex);
		ClearChildren(_deckCardRow);
		int count = Mathf.Min(visibleDeckCards, Mathf.Max(0, _previewDeckCards.Count - _deckScrollIndex));
		for (int i = 0; i < count; i++)
		{
			var cardView = _smallCardScene?.Instantiate<SmallCard>();
			if (cardView == null)
				continue;
			cardView.SetData(_previewDeckCards[_deckScrollIndex + i]);
			_deckCardRow.AddChild(cardView);
		}

		if (_deckLeftButton != null)
			_deckLeftButton.Disabled = _deckScrollIndex <= 0;
		if (_deckRightButton != null)
			_deckRightButton.Disabled = _deckScrollIndex >= maxIndex;
	}

	private int GetVisibleDeckCardCount()
	{
		float viewportWidth = GetViewportRect().Size.X;
		float availableWidth = viewportWidth - 56f - 84f - 20f - 64f;
		int count = Mathf.FloorToInt((availableWidth + DeckCardGap) / (SmallCardWidth + DeckCardGap));
		return Mathf.Clamp(count, 1, MaxVisibleDeckCards);
	}

	private static DeckList GetHeroDeck(HeroDef hero)
	{
		if (hero?.Deck is DeckList deck)
			return deck;

		string deckPath = hero?.Deck?.ResourcePath ?? "";
		return string.IsNullOrWhiteSpace(deckPath) ? null : ResourceLoader.Load<DeckList>(deckPath);
	}

	private Label CreateLabel(string text, int size, FontFile font)
	{
		var label = new Label { Text = text };
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", Colors.White);
		return label;
	}

	private Label CreateCenteredLabel(string text, int size, FontFile font)
	{
		var label = CreateLabel(text, size, font);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		return label;
	}

	private Button CreateButton(string text)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(220, 42), FocusMode = FocusModeEnum.All };
		button.AddThemeFontOverride("font", BoldFont);
		button.AddThemeFontSizeOverride("font_size", 20);
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.11f, 0.27f, 0.42f)));
		button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.17f, 0.38f, 0.56f)));
		button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.07f, 0.18f, 0.30f)));
		button.AddThemeStyleboxOverride("disabled", ButtonStyle(new Color(0.08f, 0.09f, 0.10f)));
		return button;
	}

	private StyleBoxFlat ButtonStyle(Color color)
		=> new() { BgColor = color, BorderColor = Colors.Black, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2, AntiAliasing = false };

	private static void ClearChildren(Node node)
	{
		if (node == null)
			return;

		foreach (Node child in node.GetChildren())
		{
			node.RemoveChild(child);
			child.QueueFree();
		}
	}
}
