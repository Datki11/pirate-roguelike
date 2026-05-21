using Godot;
using System.Collections.Generic;

public partial class RecruitRewardMenu : Control
{
	private const string SmallCardScenePath = "res://Assets/Components/SmallCard.tscn";

	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }

	private PackedScene _smallCardScene;
	private RunState _run;
	private Button _confirmButton;
	private HeroDef _selectedHero;
	private readonly List<HeroOffer> _heroOffers = new();

	private sealed class HeroOffer
	{
		public HeroDef Hero;
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
		margin.AddThemeConstantOverride("margin_left", 32);
		margin.AddThemeConstantOverride("margin_top", 28);
		margin.AddThemeConstantOverride("margin_right", 32);
		margin.AddThemeConstantOverride("margin_bottom", 28);
		AddChild(margin);

		var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		stack.AddThemeConstantOverride("separation", 14);
		margin.AddChild(stack);

		var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		header.AddThemeConstantOverride("separation", 12);
		stack.AddChild(header);

		var title = CreateLabel("ADD A CREW MEMBER", 20, BoldFont);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		title.HorizontalAlignment = HorizontalAlignment.Center;
		header.AddChild(title);

		_confirmButton = CreateButton("ADD TO CREW");
		_confirmButton.CustomMinimumSize = new Vector2(180, 42);
		_confirmButton.Disabled = true;
		_confirmButton.Pressed += ConfirmRecruit;
		header.AddChild(_confirmButton);

		var offerCenter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		stack.AddChild(offerCenter);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);
		offerCenter.AddChild(row);

		foreach (HeroDef hero in offers)
		{
			PanelContainer offer = CreateHeroOffer(hero);
			_heroOffers.Add(new HeroOffer { Hero = hero, Panel = offer });
			row.AddChild(offer);
		}
	}

	private PanelContainer CreateHeroOffer(HeroDef hero)
	{
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(470, 0) };
		panel.AddThemeStyleboxOverride("panel", ButtonStyle(new Color(0.06f, 0.13f, 0.17f)));

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 8);
		margin.AddChild(stack);
		stack.AddChild(CreateLabel(hero.HeroName.ToUpperInvariant(), 20, BoldFont));

		var heroSummary = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		heroSummary.AddThemeConstantOverride("separation", 12);
		stack.AddChild(heroSummary);

		var portrait = CreateHeroPortrait(hero);
		heroSummary.AddChild(portrait);

		var statStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		statStack.AddThemeConstantOverride("separation", 4);
		heroSummary.AddChild(statStack);
		statStack.AddChild(CreateLabel($"{hero.StartingMaxHP} HP", 16, BodyFont));
		if (!string.IsNullOrWhiteSpace(hero.HeroRole))
			statStack.AddChild(CreateLabel(hero.HeroRole.ToUpperInvariant(), 16, BodyFont));

		var choose = CreateButton("SELECT");
		choose.Pressed += () => SelectHero(hero);
		stack.AddChild(choose);

		stack.AddChild(CreateLabel("STARTING DECK", 16, BodyFont));

		var cards = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cards.AddThemeConstantOverride("h_separation", 10);
		cards.AddThemeConstantOverride("v_separation", 10);
		stack.AddChild(cards);

		if (hero.Deck is DeckList deck && deck.Cards != null)
		{
			foreach (CardData card in deck.Cards)
			{
				var cardView = _smallCardScene?.Instantiate<SmallCard>();
				if (cardView == null)
					continue;
				cardView.SetData(card);
				cards.AddChild(cardView);
			}
		}

		return panel;
	}

	private void SelectHero(HeroDef hero)
	{
		_selectedHero = hero;
		foreach (HeroOffer offer in _heroOffers)
		{
			Color color = offer.Hero == _selectedHero && offer.Panel != null ? new Color(0.17f, 0.38f, 0.56f) : new Color(0.06f, 0.13f, 0.17f);
			offer.Panel?.AddThemeStyleboxOverride("panel", ButtonStyle(color));
		}

		if (_confirmButton != null)
			_confirmButton.Disabled = _selectedHero == null;
	}

	private void ConfirmRecruit()
	{
		if (_selectedHero == null)
			return;

		_run.ChooseRecruit(_selectedHero.Id);
		_run.GoToMap();
	}

	private TextureRect CreateHeroPortrait(HeroDef hero)
	{
		var portrait = new TextureRect
		{
			CustomMinimumSize = new Vector2(132, 108),
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
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(220, 42), FocusMode = FocusModeEnum.None };
		button.AddThemeFontOverride("font", BoldFont);
		button.AddThemeFontSizeOverride("font_size", 20);
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.11f, 0.27f, 0.42f)));
		button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.17f, 0.38f, 0.56f)));
		button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.07f, 0.18f, 0.30f)));
		return button;
	}

	private StyleBoxFlat ButtonStyle(Color color)
		=> new() { BgColor = color, BorderColor = Colors.Black, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2, AntiAliasing = false };

}
