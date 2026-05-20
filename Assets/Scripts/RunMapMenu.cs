using Godot;

public partial class RunMapMenu : Control
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
		RunState run = GetNode<RunState>("/root/RunState");
		if (!run.HasActiveRun)
			run.StartNewRun();

		var bg = new ColorRect { Color = new Color(0.03f, 0.07f, 0.09f) };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 36);
		margin.AddThemeConstantOverride("margin_top", 32);
		margin.AddThemeConstantOverride("margin_right", 36);
		margin.AddThemeConstantOverride("margin_bottom", 32);
		AddChild(margin);

		var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		stack.AddThemeConstantOverride("separation", 14);
		margin.AddChild(stack);

		stack.AddChild(CreateLabel("RUN", 20, BoldFont));
		stack.AddChild(CreateLabel("VISITED LEVELS", 16, BodyFont));

		if (run.CompletedLevels.Count == 0)
		{
			stack.AddChild(CreateLabel("None yet", 16, BodyFont));
		}
		else
		{
			foreach (RunState.CompletedLevel level in run.CompletedLevels)
				stack.AddChild(CreateLabel($"Floor {level.Floor}: {level.Difficulty} - {level.EncounterId}", 16, BodyFont));
		}

		var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
		stack.AddChild(spacer);

		var start = CreateButton($"START FLOOR {run.CurrentFloor}");
		start.Pressed += run.StartNextCombat;
		stack.AddChild(start);
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
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(260, 44), FocusMode = FocusModeEnum.None };
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
