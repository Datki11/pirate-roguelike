using Godot;

public partial class DeveloperConsole : CanvasLayer
{
	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }
	[Export] public NodePath CombatPath { get; set; }

	private Control _root;
	private RichTextLabel _history;
	private LineEdit _input;
	private CombatManager _combat;

	public override void _Ready()
	{
		Layer = 5000;
		BodyFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/Minecraft.ttf");
		BoldFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf");
		_combat = GetNodeOrNull<CombatManager>(CombatPath)
			?? GetTree().Root.FindChild("CombatManager", true, false) as CombatManager;

		BuildUi();
		HideConsole();
	}

	public override void _Input(InputEvent e)
	{
		if (IsConsoleToggle(e))
		{
			ToggleConsole();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_root.Visible && e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
		{
			HideConsole();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (_root.Visible)
			GetViewport().SetInputAsHandled();
	}

	private bool IsConsoleToggle(InputEvent e)
	{
		return e is InputEventKey key
			&& key.Pressed
			&& !key.Echo
			&& key.ShiftPressed
			&& key.Unicode == '~';
	}

	private void ToggleConsole()
	{
		if (_root.Visible)
			HideConsole();
		else
			ShowConsole();
	}

	private void ShowConsole()
	{
		_root.Visible = true;
		AppendLine("> developer console");
		CallDeferred(MethodName.FocusInput);
	}

	private void HideConsole()
	{
		if (_root != null)
			_root.Visible = false;
	}

	private void FocusInput()
	{
		_input?.GrabFocus();
		_input?.SelectAll();
	}

	private void OnCommandSubmitted(string command)
	{
		string trimmed = command.Trim();
		if (string.IsNullOrEmpty(trimmed))
			return;

		AppendLine($"> {trimmed}");
		_input.Text = "";

		_combat ??= GetNodeOrNull<CombatManager>(CombatPath)
			?? GetTree().Root.FindChild("CombatManager", true, false) as CombatManager;

		if (_combat == null)
		{
			AppendLine("No CombatManager found.");
			return;
		}

		_combat.TryExecuteDeveloperCommand(trimmed, out string response);
		AppendLine(response);
	}

	private void BuildUi()
	{
		_root = new Control
		{
			Name = "DeveloperConsoleRoot",
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		AddChild(_root);

		var panel = new PanelContainer
		{
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			OffsetLeft = -360f,
			OffsetTop = 24f,
			OffsetRight = 360f,
			OffsetBottom = 214f,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		_root.AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 8);
		margin.AddChild(stack);

		var title = new Label
		{
			Text = "Developer Console",
			HorizontalAlignment = HorizontalAlignment.Left
		};
		title.AddThemeFontOverride("font", BoldFont);
		title.AddThemeFontSizeOverride("font_size", 20);
		title.AddThemeColorOverride("font_color", Colors.White);
		stack.AddChild(title);

		_history = new RichTextLabel
		{
			FitContent = false,
			ScrollFollowing = true,
			CustomMinimumSize = new Vector2(0, 86),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_history.AddThemeFontOverride("normal_font", BodyFont);
		_history.AddThemeFontSizeOverride("normal_font_size", 16);
		_history.AddThemeColorOverride("default_color", new Color(0.82f, 0.94f, 1f));
		stack.AddChild(_history);

		_input = new LineEdit
		{
			PlaceholderText = "command",
			CaretBlink = true
		};
		ApplyLineEditTheme(_input);
		_input.TextSubmitted += OnCommandSubmitted;
		stack.AddChild(_input);
	}

	private void AppendLine(string text)
	{
		if (_history == null)
			return;

		if (!string.IsNullOrEmpty(_history.Text))
			_history.AppendText("\n");
		_history.AppendText(text);
	}

	private void ApplyLineEditTheme(LineEdit input)
	{
		input.AddThemeFontOverride("font", BodyFont);
		input.AddThemeFontSizeOverride("font_size", 16);
		input.AddThemeColorOverride("font_color", Colors.White);
		input.AddThemeColorOverride("caret_color", Colors.White);
		input.AddThemeColorOverride("font_placeholder_color", new Color(0.68f, 0.76f, 0.82f));
		input.AddThemeStyleboxOverride("normal", CreateInputStyle(new Color(0.04f, 0.10f, 0.14f)));
		input.AddThemeStyleboxOverride("focus", CreateInputStyle(new Color(0.07f, 0.16f, 0.22f)));
		input.AddThemeStyleboxOverride("read_only", CreateInputStyle(new Color(0.04f, 0.10f, 0.14f)));
	}

	private static StyleBoxFlat CreatePanelStyle()
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

	private static StyleBoxFlat CreateInputStyle(Color color)
	{
		return new StyleBoxFlat
		{
			BgColor = color,
			BorderColor = Colors.Black,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
			AntiAliasing = false
		};
	}
}
