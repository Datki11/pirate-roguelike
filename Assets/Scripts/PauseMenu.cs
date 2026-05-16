using Godot;

public partial class PauseMenu : CanvasLayer
{
	private readonly float[] _speedOptions = { 0.33f, 0.5f, 1f, 1.5f, 2f, 2.5f, 3f };

	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }
	[Export] public int BodyFontSize { get; set; } = 16;
	[Export] public int BoldFontSize { get; set; } = 20;

	private Control _root;
	private VBoxContainer _mainMenu;
	private VBoxContainer _settingsMenu;
	private OptionButton _speedSelect;
	private float _currentSpeed = 1f;

	public override void _Ready()
	{
		Layer = 1000;
		ProcessMode = ProcessModeEnum.Always;
		BodyFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/Minecraft.ttf");
		BoldFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf");
		ConfigurePixelFont(BodyFont);
		ConfigurePixelFont(BoldFont);
		Engine.TimeScale = _currentSpeed;
		BuildOverlay();
		HidePause();
	}

	public override void _Input(InputEvent e)
	{
		if (e is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		if (key.Keycode != Key.Escape && !e.IsActionPressed("ui_cancel"))
			return;

		if (_root != null && _root.Visible)
			ResumeGame();
		else
			ShowPause();

		GetViewport().SetInputAsHandled();
	}

	private void BuildOverlay()
	{
		_root = new Control
		{
			Name = "PauseOverlay",
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		var shade = new ColorRect
		{
			Color = new Color(0f, 0f, 0f, 0.68f),
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(shade);

		var center = new CenterContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(center);

		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(280, 250),
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		center.AddChild(panel);

		var content = new MarginContainer();
		content.AddThemeConstantOverride("margin_left", 18);
		content.AddThemeConstantOverride("margin_top", 18);
		content.AddThemeConstantOverride("margin_right", 18);
		content.AddThemeConstantOverride("margin_bottom", 18);
		panel.AddChild(content);

		_mainMenu = CreateMenuStack();
		content.AddChild(_mainMenu);
		_mainMenu.AddChild(CreateTitle("PAUSED"));
		_mainMenu.AddChild(CreateButton("RESUME", ResumeGame));
		_mainMenu.AddChild(CreateButton("SETTINGS", ShowSettings));
		_mainMenu.AddChild(CreateButton("QUIT", QuitGame));

		_settingsMenu = CreateMenuStack();
		_settingsMenu.Visible = false;
		content.AddChild(_settingsMenu);
		_settingsMenu.AddChild(CreateTitle("SETTINGS"));
		_settingsMenu.AddChild(CreateLabel("Game Speed"));
		_speedSelect = CreateSpeedSelect();
		_settingsMenu.AddChild(_speedSelect);
		_settingsMenu.AddChild(CreateButton("BACK", ShowMainMenu));
	}

	private VBoxContainer CreateMenuStack()
	{
		var stack = new VBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		stack.AddThemeConstantOverride("separation", 10);
		return stack;
	}

	private Label CreateTitle(string text)
	{
		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			CustomMinimumSize = new Vector2(220, 32),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		label.AddThemeFontOverride("font", BoldFont);
		label.AddThemeFontSizeOverride("font_size", BoldFontSize);
		label.AddThemeColorOverride("font_color", Colors.White);
		return label;
	}

	private Label CreateLabel(string text)
	{
		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			CustomMinimumSize = new Vector2(220, 24),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		label.AddThemeFontOverride("font", BodyFont);
		label.AddThemeFontSizeOverride("font_size", BodyFontSize);
		label.AddThemeColorOverride("font_color", Colors.White);
		return label;
	}

	private Button CreateButton(string text, System.Action pressed)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(220, 38),
			FocusMode = Control.FocusModeEnum.None,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		button.Pressed += pressed;
		ApplyButtonTheme(button);
		return button;
	}

	private OptionButton CreateSpeedSelect()
	{
		var option = new OptionButton
		{
			CustomMinimumSize = new Vector2(220, 38),
			FocusMode = Control.FocusModeEnum.None,
			MouseFilter = Control.MouseFilterEnum.Stop
		};

		for (int i = 0; i < _speedOptions.Length; i++)
			option.AddItem(FormatSpeed(_speedOptions[i]), i);

		option.Select(GetSpeedIndex(_currentSpeed));
		option.ItemSelected += OnSpeedSelected;
		ApplyButtonTheme(option);
		return option;
	}

	private void ApplyButtonTheme(Button button)
	{
		button.AddThemeFontOverride("font", BoldFont);
		button.AddThemeFontSizeOverride("font_size", BoldFontSize);
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		button.AddThemeColorOverride("font_pressed_color", Colors.White);
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.11f, 0.27f, 0.42f)));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.17f, 0.38f, 0.56f)));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(new Color(0.07f, 0.18f, 0.30f)));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	private StyleBoxFlat CreatePanelStyle()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.08f, 0.10f, 0.96f),
			BorderColor = Colors.Black,
			BorderWidthLeft = 3,
			BorderWidthTop = 3,
			BorderWidthRight = 3,
			BorderWidthBottom = 3,
			AntiAliasing = false
		};
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

	private void ShowPause()
	{
		ShowMainMenu();
		_root.Visible = true;
		GetTree().Paused = true;
	}

	private void HidePause()
	{
		if (_root != null)
			_root.Visible = false;
	}

	private void ResumeGame()
	{
		GetTree().Paused = false;
		HidePause();
		Engine.TimeScale = _currentSpeed;
	}

	private void ShowMainMenu()
	{
		if (_mainMenu != null)
			_mainMenu.Visible = true;
		if (_settingsMenu != null)
			_settingsMenu.Visible = false;
	}

	private void ShowSettings()
	{
		_mainMenu.Visible = false;
		_settingsMenu.Visible = true;
	}

	private void QuitGame()
	{
		GetTree().Paused = false;
		GetTree().Quit();
	}

	private void OnSpeedSelected(long index)
	{
		if (index < 0 || index >= _speedOptions.Length)
			return;

		_currentSpeed = _speedOptions[index];
		Engine.TimeScale = _currentSpeed;
	}

	private int GetSpeedIndex(float speed)
	{
		for (int i = 0; i < _speedOptions.Length; i++)
		{
			if (Mathf.IsEqualApprox(_speedOptions[i], speed))
				return i;
		}

		return 2;
	}

	private string FormatSpeed(float speed)
	{
		return speed < 1f ? $"{speed:0.##}X" : $"{speed:0.#}X";
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
}
