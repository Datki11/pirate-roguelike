using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CardRewardMenu : Control
{
	private const string SmallCardScenePath = "res://Assets/Components/SmallCard.tscn";
	private const string CardCountIconPath = "res://Assets/Sprites/Icons/placeholder-icons/1-bit_Pixel_Icons/Sprites_Cropped/Boardgames_Cards_Deck_Pile.png";
	private const string EyeIconPath = "res://Assets/Sprites/Icons/placeholder-icons/1-bit_Pixel_Icons/Sprites_Cropped/Media_Eyeball_Vision_Shown.png";
	private const string EyeHiddenIconPath = "res://Assets/Sprites/Icons/placeholder-icons/1-bit_Pixel_Icons/Sprites_Cropped/Media_Eyeball_Vision_Hidden_Disabled_Off.png";

	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }

	private PackedScene _smallCardScene;
	private CardData _selectedCard;
	private string _selectedCardPath = "";
	private RunState _run;
	private VBoxContainer _contentStack;
	private VBoxContainer _crewDetail;
	private VBoxContainer _deckViewerRoot;
	private Control _assignmentTopSpacer;
	private MarginContainer _crewDetailOffset;
	private Button _confirmCrewButton;
	private Button _backButton;
	private RunState.CrewMember _selectedMember;
	private HeroDef _selectedHero;
	private bool _deckExpanded;
	private HBoxContainer _deckCardRow;
	private Button _deckLeftButton;
	private Button _deckRightButton;
	private List<CardData> _previewDeckCards = new();
	private int _deckScrollIndex;
	private readonly List<CardChoice> _cardChoices = new();
	private readonly List<CrewChoice> _crewChoices = new();
	private const int MaxVisibleDeckCards = 6;
	private const int SmallCardWidth = 140;
	private const int DeckCardGap = 10;
	private const float CollapsedCrewAssignmentHeight = 414f;

	private sealed class CardChoice
	{
		public CardData Card;
		public PanelContainer Panel;
	}

	private sealed class CrewChoice
	{
		public HeroDef Hero;
		public RunState.CrewMember Member;
		public CrewSpriteChoice Button;
	}

	private sealed partial class CrewSpriteChoice : Button
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

		public CrewSpriteChoice()
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

		public void SetHero(HeroDef hero, RunState.CrewMember member, int deckCount)
		{
			_frames.Clear();
			_frameIndex = 0;
			_frameTime = 0f;
			_stableTextureRectValid = false;
			FaceRight = hero?.FaceRight ?? true;
			_heroName = hero?.HeroName?.ToUpperInvariant() ?? "";
			_hp = Mathf.Max(0, member?.HP ?? 0);
			_maxHp = Mathf.Max(1, member?.MaxHP ?? 1);
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
		List<CardData> cards = _run.GetCardRewardCards();
		if (!_run.CardRewardPending || cards.Count == 0)
		{
			_run.ChooseCardReward("", "");
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
		stack.AddThemeConstantOverride("separation", 8);
		margin.AddChild(stack);

		_contentStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		_contentStack.AddThemeConstantOverride("separation", 10);
		stack.AddChild(_contentStack);

		ShowCardPickView(cards);
	}

	private void ShowCardPickView(List<CardData> cards)
	{
		ClearChildren(_contentStack);
		ClearCrewNavButtons();
		_cardChoices.Clear();
		_crewChoices.Clear();
		_selectedCard = null;
		_selectedCardPath = "";
		_selectedMember = null;
		_selectedHero = null;
		_deckExpanded = false;

		var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		_contentStack.AddChild(center);

		var rewardStack = new VBoxContainer();
		rewardStack.AddThemeConstantOverride("separation", 20);
		center.AddChild(rewardStack);

		var prompt = CreateCenteredLabel("CHOOSE A CARD REWARD", 20, BoldFont);
		rewardStack.AddChild(prompt);

		var cardRow = new HBoxContainer();
		cardRow.AddThemeConstantOverride("separation", 16);
		rewardStack.AddChild(cardRow);

		foreach (CardData card in cards)
		{
			PanelContainer cardPanel = CreateCardChoice(card);
			_cardChoices.Add(new CardChoice { Card = card, Panel = cardPanel });
			cardRow.AddChild(cardPanel);
		}

		var skipCenter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_contentStack.AddChild(skipCenter);

		var skip = CreateButton("SKIP");
		skip.CustomMinimumSize = new Vector2(180, 42);
		skip.Pressed += () =>
		{
			_run.ChooseCardReward("", "");
			_run.GoToMap();
		};
		skipCenter.AddChild(skip);
	}

	private void ShowCrewAssignView()
	{
		ClearChildren(_contentStack);
		_crewChoices.Clear();
		_selectedMember = null;
		_selectedHero = null;
		_deckExpanded = false;
		_deckViewerRoot = null;
		_assignmentTopSpacer = null;
		_crewDetailOffset = null;
		ClearCrewNavButtons();

		_backButton = CreateButton("BACK");
		_backButton.CustomMinimumSize = new Vector2(140, 42);
		_backButton.Size = _backButton.CustomMinimumSize;
		_backButton.SetAnchorsPreset(LayoutPreset.TopLeft);
		_backButton.OffsetLeft = 28f;
		_backButton.OffsetTop = 16f;
		_backButton.OffsetRight = 168f;
		_backButton.OffsetBottom = 58f;
		_backButton.Pressed += () => ShowCardPickView(_run.GetCardRewardCards());
		AddChild(_backButton);

		_assignmentTopSpacer = new Control { CustomMinimumSize = new Vector2(0, GetCrewAssignmentTopGap(false)) };
		_contentStack.AddChild(_assignmentTopSpacer);

		var assignmentOuter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_contentStack.AddChild(assignmentOuter);

		var assignmentStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		assignmentStack.AddThemeConstantOverride("separation", 6);
		assignmentOuter.AddChild(assignmentStack);

		var prompt = CreateCenteredLabel("CHOOSE A CREW MEMBER", 20, BoldFont);
		assignmentStack.AddChild(prompt);

		var selectedCenter = new CenterContainer();
		assignmentStack.AddChild(selectedCenter);
		var selectedCard = _smallCardScene?.Instantiate<SmallCard>();
		if (selectedCard != null)
		{
			selectedCard.SetData(_selectedCard);
			selectedCenter.AddChild(selectedCard);
		}

		var crewStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		crewStack.AddThemeConstantOverride("separation", 0);
		assignmentStack.AddChild(crewStack);

		var crewRowCenter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		crewStack.AddChild(crewRowCenter);

		var crewRow = new HBoxContainer();
		crewRow.AddThemeConstantOverride("separation", 22);
		crewRowCenter.AddChild(crewRow);

		foreach (RunState.CrewMember member in _run.Crew)
		{
			HeroDef hero = _run.LoadHero(member.HeroId);
			if (hero != null)
			{
				CrewSpriteChoice choice = CreateCrewChoice(hero, member);
				_crewChoices.Add(new CrewChoice { Hero = hero, Member = member, Button = choice });
				crewRow.AddChild(choice);
			}
		}

		_crewDetailOffset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		crewStack.AddChild(_crewDetailOffset);

		_crewDetail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_crewDetail.AddThemeConstantOverride("separation", 0);
		_crewDetailOffset.AddChild(_crewDetail);

		_contentStack.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

		_deckViewerRoot = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_deckViewerRoot.AddThemeConstantOverride("separation", 0);
		_contentStack.AddChild(_deckViewerRoot);

		CreateAnchoredConfirmButton();
	}

	private PanelContainer CreateCardChoice(CardData card)
	{
		var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		panel.AddThemeStyleboxOverride("panel", PanelStyle(new Color(0.06f, 0.13f, 0.17f)));

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		panel.AddChild(margin);

		var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		stack.AddThemeConstantOverride("separation", 8);
		margin.AddChild(stack);

		var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		stack.AddChild(center);

		var cardView = _smallCardScene?.Instantiate<SmallCard>();
		if (cardView != null)
		{
			cardView.SetData(card);
			center.AddChild(cardView);
		}

		var button = CreateButton("SELECT");
		button.Pressed += () => SelectCard(card);
		stack.AddChild(button);

		return panel;
	}

	private CrewSpriteChoice CreateCrewChoice(HeroDef hero, RunState.CrewMember member)
	{
		var choice = new CrewSpriteChoice();
		DeckList deck = _run.CreateDeckListForMember(member);
		choice.SetHero(hero, member, deck?.Cards?.Count ?? 0);
		choice.TooltipText = $"{hero.HeroName}\n{Mathf.Max(0, member.HP)}/{Mathf.Max(1, member.MaxHP)} HP";
		choice.Pressed += () => SelectCrew(hero, member);
		return choice;
	}

	private void SelectCrew(HeroDef hero, RunState.CrewMember member)
	{
		_selectedMember = member;
		_selectedHero = hero;
		foreach (CrewChoice choice in _crewChoices)
		{
			if (choice.Button != null)
				choice.Button.Selected = choice.Member == _selectedMember;
		}

		if (_confirmCrewButton != null)
			_confirmCrewButton.Disabled = _selectedMember == null;

		ShowCrewDetails(hero, member);
	}

	private void ConfirmCrewSelection()
	{
		if (_selectedMember == null || string.IsNullOrWhiteSpace(_selectedCardPath))
			return;

		_run.ChooseCardReward(_selectedCardPath, _selectedMember.HeroId);
		_run.GoToMap();
	}

	private void ShowCrewDetails(HeroDef hero, RunState.CrewMember member)
	{
		if (_crewDetail == null)
			return;

		ClearChildren(_crewDetail);
		ClearChildren(_deckViewerRoot);
		UpdateCrewAssignmentOffset();
		UpdateCrewDetailOffset(member);

		AddDeckToggle(hero, member);
		if (!_deckExpanded)
			return;

		var deckColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		deckColumn.AddThemeConstantOverride("separation", 0);
		_deckViewerRoot.AddChild(deckColumn);

		DeckList deck = _run.CreateDeckListForMember(member);
		_previewDeckCards = deck.Cards?.ToList() ?? new List<CardData>();
		_deckScrollIndex = 0;

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

	private void AddDeckToggle(HeroDef hero, RunState.CrewMember member)
	{
		var toggleCenter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_crewDetail.AddChild(toggleCenter);

		var toggle = CreateIconTextButton(_deckExpanded ? "HIDE" : "SHOW", ResourceLoader.Load<Texture2D>(_deckExpanded ? EyeHiddenIconPath : EyeIconPath));
		toggle.Pressed += () =>
		{
			_deckExpanded = !_deckExpanded;
			ShowCrewDetails(hero, member);
		};
		toggleCenter.AddChild(toggle);
	}

	private Button CreateIconTextButton(string text, Texture2D icon)
	{
		var button = CreateButton("");
		button.CustomMinimumSize = new Vector2(126, 36);

		var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		button.AddChild(center);

		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 8);
		center.AddChild(row);

		var iconWrap = new MarginContainer { CustomMinimumSize = new Vector2(30, 30), MouseFilter = MouseFilterEnum.Ignore };
		iconWrap.AddThemeConstantOverride("margin_top", 2);
		row.AddChild(iconWrap);

		var iconCenter = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		iconWrap.AddChild(iconCenter);

		var iconView = new TextureRect
		{
			Texture = icon,
			CustomMinimumSize = new Vector2(28, 28),
			Size = new Vector2(28, 28),
			TextureFilter = TextureFilterEnum.Nearest,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore
		};
		iconCenter.AddChild(iconView);

		var label = CreateLabel(text, 16, BoldFont);
		label.VerticalAlignment = VerticalAlignment.Center;
		label.CustomMinimumSize = new Vector2(54, 30);
		label.MouseFilter = MouseFilterEnum.Ignore;
		row.AddChild(label);

		return button;
	}

	private Button CreateSmallButton(string text)
	{
		var button = CreateButton(text);
		button.CustomMinimumSize = new Vector2(142, 32);
		button.AddThemeFontSizeOverride("font_size", 16);
		return button;
	}

	private void UpdateCrewAssignmentOffset()
	{
		if (_assignmentTopSpacer == null)
			return;

		_assignmentTopSpacer.CustomMinimumSize = new Vector2(0, GetCrewAssignmentTopGap(_deckExpanded));
		_assignmentTopSpacer.Size = _assignmentTopSpacer.CustomMinimumSize;
		_contentStack?.QueueSort();
	}

	private float GetCrewAssignmentTopGap(bool deckExpanded)
	{
		if (deckExpanded)
			return 0f;

		float viewportHeight = GetViewportRect().Size.Y;
		return Mathf.Clamp((viewportHeight - CollapsedCrewAssignmentHeight) * 0.5f - 18f, 84f, 164f);
	}

	private void UpdateCrewDetailOffset(RunState.CrewMember selectedMember)
	{
		if (_crewDetailOffset == null)
			return;

		int selectedIndex = _crewChoices.FindIndex(choice => choice.Member == selectedMember);
		int crewCount = _crewChoices.Count;
		if (selectedIndex < 0 || crewCount <= 1)
		{
			_crewDetailOffset.AddThemeConstantOverride("margin_left", 0);
			_crewDetailOffset.AddThemeConstantOverride("margin_right", 0);
			return;
		}

		float slotWidth = 150f + 22f;
		float offset = (selectedIndex - (crewCount - 1) * 0.5f) * slotWidth;
		if (offset > 0f)
		{
			_crewDetailOffset.AddThemeConstantOverride("margin_left", Mathf.RoundToInt(offset * 2f));
			_crewDetailOffset.AddThemeConstantOverride("margin_right", 0);
		}
		else
		{
			_crewDetailOffset.AddThemeConstantOverride("margin_left", 0);
			_crewDetailOffset.AddThemeConstantOverride("margin_right", Mathf.RoundToInt(-offset * 2f));
		}
	}

	private void CreateAnchoredConfirmButton()
	{
		if (_confirmCrewButton != null && IsInstanceValid(_confirmCrewButton))
			_confirmCrewButton.QueueFree();

		_confirmCrewButton = CreateButton("CONFIRM");
		_confirmCrewButton.CustomMinimumSize = new Vector2(180, 42);
		_confirmCrewButton.Disabled = _selectedMember == null;
		_confirmCrewButton.Pressed += ConfirmCrewSelection;
		_confirmCrewButton.SetAnchorsPreset(LayoutPreset.BottomWide);
		_confirmCrewButton.AnchorLeft = 0.5f;
		_confirmCrewButton.AnchorRight = 0.5f;
		_confirmCrewButton.OffsetLeft = -90f;
		_confirmCrewButton.OffsetRight = 90f;
		_confirmCrewButton.OffsetTop = -54f;
		_confirmCrewButton.OffsetBottom = -12f;
		AddChild(_confirmCrewButton);
	}

	private void ClearCrewNavButtons()
	{
		if (_confirmCrewButton != null && IsInstanceValid(_confirmCrewButton))
			_confirmCrewButton.QueueFree();
		_confirmCrewButton = null;

		if (_backButton != null && IsInstanceValid(_backButton))
			_backButton.QueueFree();
		_backButton = null;
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

	private void SelectCard(CardData card)
	{
		_selectedCard = card;
		_selectedCardPath = card?.ResourcePath ?? "";
		foreach (CardChoice choice in _cardChoices)
		{
			Color color = choice.Card == _selectedCard && choice.Panel != null ? new Color(0.17f, 0.38f, 0.56f) : new Color(0.06f, 0.13f, 0.17f);
			choice.Panel?.AddThemeStyleboxOverride("panel", PanelStyle(color));
		}

		if (!string.IsNullOrWhiteSpace(_selectedCardPath))
			ShowCrewAssignView();
	}

	private TextureRect CreateHeroPortrait(HeroDef hero)
	{
		var portrait = new TextureRect
		{
			CustomMinimumSize = new Vector2(180, 112),
			ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = TextureFilterEnum.Nearest
		};

		string path = $"{hero.SpriteRootPath}/{hero.IdleFolderName}/1.png";
		portrait.Texture = ResourceLoader.Load<Texture2D>(path);
		return portrait;
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
		button.AddThemeStyleboxOverride("normal", PanelStyle(new Color(0.11f, 0.27f, 0.42f)));
		button.AddThemeStyleboxOverride("hover", PanelStyle(new Color(0.17f, 0.38f, 0.56f)));
		button.AddThemeStyleboxOverride("pressed", PanelStyle(new Color(0.07f, 0.18f, 0.30f)));
		button.AddThemeStyleboxOverride("disabled", PanelStyle(new Color(0.08f, 0.09f, 0.10f)));
		return button;
	}

	private StyleBoxFlat PanelStyle(Color color)
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
