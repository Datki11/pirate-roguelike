using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CardRewardMenu : Control
{
	private const string SmallCardScenePath = "res://Assets/Components/SmallCard.tscn";

	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }

	private PackedScene _smallCardScene;
	private CardData _selectedCard;
	private string _selectedCardPath = "";
	private readonly List<CardChoice> _cardChoices = new();
	private readonly List<Button> _giveButtons = new();
	private Label _selectionLabel;

	private sealed class CardChoice
	{
		public CardData Card;
		public PanelContainer Panel;
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
		RunState run = GetNode<RunState>("/root/RunState");
		List<CardData> cards = run.GetCardRewardCards();
		if (!run.CardRewardPending || cards.Count == 0)
		{
			run.ChooseCardReward("", "");
			run.CallDeferred(nameof(RunState.GoToMap));
			return;
		}

		var bg = new ColorRect { Color = new Color(0.03f, 0.07f, 0.09f) };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		AddChild(margin);

		var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		stack.AddThemeConstantOverride("separation", 12);
		margin.AddChild(stack);

		var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		header.AddThemeConstantOverride("separation", 12);
		stack.AddChild(header);
		var title = CreateLabel("OPEN CARD PACKAGE", 20, BoldFont);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		title.HorizontalAlignment = HorizontalAlignment.Left;
		header.AddChild(title);
		var skip = CreateButton("SKIP");
		skip.CustomMinimumSize = new Vector2(140, 42);
		skip.Pressed += () =>
		{
			run.ChooseCardReward("", "");
			run.GoToMap();
		};
		header.AddChild(skip);

		_selectionLabel = CreateLabel("Pick a card, then give it to a crew member.", 16, BodyFont);
		_selectionLabel.HorizontalAlignment = HorizontalAlignment.Left;
		stack.AddChild(_selectionLabel);

		var cardRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cardRow.AddThemeConstantOverride("separation", 16);
		stack.AddChild(cardRow);

		foreach (CardData card in cards)
		{
			PanelContainer cardPanel = CreateCardChoice(card);
			_cardChoices.Add(new CardChoice { Card = card, Panel = cardPanel });
			cardRow.AddChild(cardPanel);
		}

		var crewTitle = CreateLabel("CREW", 20, BoldFont);
		crewTitle.HorizontalAlignment = HorizontalAlignment.Left;
		stack.AddChild(crewTitle);

		var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		stack.AddChild(scroll);
		var crewList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		crewList.AddThemeConstantOverride("separation", 12);
		scroll.AddChild(crewList);

		foreach (RunState.CrewMember member in run.Crew)
		{
			HeroDef hero = run.LoadHero(member.HeroId);
			if (hero != null)
				crewList.AddChild(CreateCrewPanel(run, hero, member));
		}

		SelectCard(cards[0]);
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

	private PanelContainer CreateCrewPanel(RunState run, HeroDef hero, RunState.CrewMember member)
	{
		var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		panel.AddThemeStyleboxOverride("panel", PanelStyle(new Color(0.06f, 0.13f, 0.17f)));

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 14);
		margin.AddChild(row);

		var summary = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
		summary.AddThemeConstantOverride("separation", 6);
		row.AddChild(summary);

		summary.AddChild(CreateHeroPortrait(hero));
		summary.AddChild(CreateLabel(hero.HeroName.ToUpperInvariant(), 20, BoldFont));
		summary.AddChild(CreateLabel((hero.HeroRole ?? "").ToUpperInvariant(), 16, BodyFont));
		summary.AddChild(CreateLabel($"{Mathf.Max(0, member.HP)}/{Mathf.Max(1, member.MaxHP)} HP", 16, BodyFont));

		var give = CreateButton("GIVE SELECTED CARD");
		give.Pressed += () =>
		{
			run.ChooseCardReward(_selectedCardPath, member.HeroId);
			run.GoToMap();
		};
		summary.AddChild(give);
		_giveButtons.Add(give);

		var deckColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		deckColumn.AddThemeConstantOverride("separation", 8);
		row.AddChild(deckColumn);

		DeckList deck = run.CreateDeckListForMember(member);
		deckColumn.AddChild(CreateLabel($"CURRENT CARDS - {deck.Cards.Count}", 16, BodyFont));
		var cards = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cards.AddThemeConstantOverride("h_separation", 10);
		cards.AddThemeConstantOverride("v_separation", 10);
		deckColumn.AddChild(cards);

		foreach (CardData card in deck.Cards)
		{
			var cardView = _smallCardScene?.Instantiate<SmallCard>();
			if (cardView == null)
				continue;
			cardView.SetData(card);
			cards.AddChild(cardView);
		}

		return panel;
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

		if (_selectionLabel != null)
			_selectionLabel.Text = _selectedCard != null ? $"Selected: {_selectedCard.Title}" : "Pick a card, then give it to a crew member.";

		foreach (Button button in _giveButtons)
			button.Disabled = string.IsNullOrWhiteSpace(_selectedCardPath);
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
}
