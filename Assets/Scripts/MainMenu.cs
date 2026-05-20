using Godot;

public partial class MainMenu : Control
{
	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }

	public override void _Ready()
	{
		BodyFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/Minecraft.ttf");
		BoldFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf");
		Build();
	}

	private void Build()
	{
		var shade = new ColorRect { Color = new Color(0.03f, 0.07f, 0.09f) };
		shade.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(shade);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(320, 180) };
		stack.AddThemeConstantOverride("separation", 16);
		center.AddChild(stack);

		var title = CreateLabel("PIRATE ROGUELIKE", 20, BoldFont);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		stack.AddChild(title);

		var newRun = CreateButton("NEW RUN");
		newRun.Pressed += () =>
		{
			GetRunState().StartNewRun();
			GetRunState().GoToMap();
		};
		stack.AddChild(newRun);
	}

	private Label CreateLabel(string text, int size, FontFile font)
	{
		var label = new Label { Text = text, CustomMinimumSize = new Vector2(280, 32) };
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", Colors.White);
		return label;
	}

	private Button CreateButton(string text)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(220, 44), FocusMode = FocusModeEnum.None };
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

	private RunState GetRunState() => GetNode<RunState>("/root/RunState");
}
