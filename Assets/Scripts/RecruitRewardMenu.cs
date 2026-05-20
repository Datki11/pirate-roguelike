using Godot;
using System.Collections.Generic;

public partial class RecruitRewardMenu : Control
{
	private const string SmallCardScenePath = "res://Assets/Components/SmallCard.tscn";

	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }

	private PackedScene _smallCardScene;

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
		List<HeroDef> offers = run.GetRecruitOffers();
		if (offers.Count == 0)
		{
			run.ChooseRecruit("");
			run.CallDeferred(nameof(RunState.GoToMap));
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
		stack.AddChild(CreateLabel("ADD A CREW MEMBER", 20, BoldFont));

		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 16);
		stack.AddChild(row);

		foreach (HeroDef hero in offers)
			row.AddChild(CreateHeroOffer(hero, run));
	}

	private PanelContainer CreateHeroOffer(HeroDef hero, RunState run)
	{
		var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		panel.AddThemeStyleboxOverride("panel", ButtonStyle(new Color(0.06f, 0.13f, 0.17f)));

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
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

		stack.AddChild(CreateLabel("STARTING DECK", 16, BodyFont));

		var cards = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
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

		var choose = CreateButton($"CHOOSE {hero.HeroName.ToUpperInvariant()}");
		choose.Pressed += () =>
		{
			run.ChooseRecruit(hero.Id);
			run.GoToMap();
		};
		stack.AddChild(choose);

		return panel;
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
