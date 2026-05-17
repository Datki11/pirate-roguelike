using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PauseMenu : CanvasLayer
{
	private readonly float[] _speedOptions = { 0.33f, 0.5f, 0.75f, 1f, 1.5f, 2f, 2.5f, 3f };
	private const string CardResourceDir = "res://Assets/Resources/SmallCards";
	private const string EnemyResourceDir = "res://Assets/Resources/Enemies";
	private const string SmallCardScenePath = "res://Assets/Components/SmallCard.tscn";

	[Export] public FontFile BodyFont { get; set; }
	[Export] public FontFile BoldFont { get; set; }
	[Export] public int BodyFontSize { get; set; } = 16;
	[Export] public int BoldFontSize { get; set; } = 20;

	private Control _root;
	private VBoxContainer _mainMenu;
	private VBoxContainer _settingsMenu;
	private Control _compendiumView;
	private Control _bestiaryView;
	private HFlowContainer _cardFlow;
	private HFlowContainer _enemyFlow;
	private LineEdit _cardSearch;
	private OptionButton _cardSort;
	private OptionButton _enemySort;
	private Label _compendiumCount;
	private Label _bestiaryCount;
	private VBoxContainer _effectFilterList;
	private Control _bestiaryModal;
	private Label _bestiaryModalTitle;
	private Label _bestiaryModalHp;
	private HBoxContainer _bestiaryAnimationRow;
	private HFlowContainer _bestiaryDeckFlow;
	private OptionButton _speedSelect;
	private float _currentSpeed = 1f;
	private PackedScene _smallCardScene;
	private readonly List<CardData> _allCards = new();
	private readonly List<EnemyDef> _allEnemies = new();
	private readonly Dictionary<string, string> _effectNames = new();
	private readonly Dictionary<string, CheckButton> _effectFilters = new();
	private readonly List<AnimationPreview> _activeAnimationPreviews = new();

	private sealed class AnimationPreview
	{
		public TextureRect View;
		public List<AtlasTexture> Frames = new();
		public float FrameSeconds = 0.12f;
		public float Time;
		public int Index;
	}

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

	public override void _Process(double delta)
	{
		if (_bestiaryModal == null || !_bestiaryModal.Visible)
			return;

		foreach (AnimationPreview preview in _activeAnimationPreviews)
		{
			if (preview?.View == null || preview.Frames == null || preview.Frames.Count == 0)
				continue;

			preview.Time += (float)delta;
			float frameSeconds = Mathf.Max(0.01f, preview.FrameSeconds);
			while (preview.Time >= frameSeconds)
			{
				preview.Time -= frameSeconds;
				preview.Index = (preview.Index + 1) % preview.Frames.Count;
				preview.View.Texture = preview.Frames[preview.Index];
			}
		}
	}

	public override void _Input(InputEvent e)
	{
		if (e is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		if (key.Keycode != Key.Escape && !e.IsActionPressed("ui_cancel"))
			return;

		if (_root != null && _root.Visible)
		{
			if (_bestiaryModal != null && _bestiaryModal.Visible)
				CloseBestiaryModal();
			else if ((_compendiumView != null && _compendiumView.Visible) || (_bestiaryView != null && _bestiaryView.Visible) || (_settingsMenu != null && _settingsMenu.Visible))
				ShowMainMenu();
			else
				ResumeGame();
		}
		else
		{
			ShowPause();
		}

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
		_mainMenu.AddChild(CreateButton("COMPENDIUM", ShowCompendium));
		_mainMenu.AddChild(CreateButton("BESTIARY", ShowBestiary));
		_mainMenu.AddChild(CreateButton("QUIT", QuitGame));

		_settingsMenu = CreateMenuStack();
		_settingsMenu.Visible = false;
		content.AddChild(_settingsMenu);
		_settingsMenu.AddChild(CreateTitle("SETTINGS"));
		_settingsMenu.AddChild(CreateLabel("Game Speed"));
		_speedSelect = CreateSpeedSelect();
		_settingsMenu.AddChild(_speedSelect);
		_settingsMenu.AddChild(CreateButton("BACK", ShowMainMenu));

		LoadCompendiumCards();
		LoadBestiaryEnemies();
		BuildCompendiumView();
		BuildBestiaryView();
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

	private void LoadCompendiumCards()
	{
		_allCards.Clear();
		_effectNames.Clear();
		_smallCardScene ??= ResourceLoader.Load<PackedScene>(SmallCardScenePath);

		var dir = DirAccess.Open(CardResourceDir);
		if (dir == null)
			return;

		dir.ListDirBegin();
		while (true)
		{
			string fileName = dir.GetNext();
			if (string.IsNullOrEmpty(fileName))
				break;
			if (dir.CurrentIsDir() || !fileName.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
				continue;

			var card = ResourceLoader.Load<CardData>($"{CardResourceDir}/{fileName}");
			if (card == null)
				continue;

			_allCards.Add(card);
			foreach (string effectId in GetCardEffectIds(card))
			{
				if (!_effectNames.ContainsKey(effectId))
					_effectNames[effectId] = GetEffectDisplayName(card, effectId);
			}
		}
		dir.ListDirEnd();

		_allCards.Sort((a, b) => string.Compare(a?.Title ?? "", b?.Title ?? "", StringComparison.OrdinalIgnoreCase));
	}

	private void LoadBestiaryEnemies()
	{
		_allEnemies.Clear();

		var dir = DirAccess.Open(EnemyResourceDir);
		if (dir == null)
			return;

		dir.ListDirBegin();
		while (true)
		{
			string fileName = dir.GetNext();
			if (string.IsNullOrEmpty(fileName))
				break;
			if (dir.CurrentIsDir() || !fileName.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
				continue;

			var enemy = ResourceLoader.Load<EnemyDef>($"{EnemyResourceDir}/{fileName}");
			if (enemy != null)
				_allEnemies.Add(enemy);
		}
		dir.ListDirEnd();

		_allEnemies.Sort((a, b) => string.Compare(GetEnemyName(a), GetEnemyName(b), StringComparison.OrdinalIgnoreCase));
	}

	private void BuildCompendiumView()
	{
		_compendiumView = new MarginContainer
		{
			Name = "CompendiumView",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_compendiumView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_compendiumView.AddThemeConstantOverride("margin_left", 24);
		_compendiumView.AddThemeConstantOverride("margin_top", 24);
		_compendiumView.AddThemeConstantOverride("margin_right", 24);
		_compendiumView.AddThemeConstantOverride("margin_bottom", 24);
		_root.AddChild(_compendiumView);

		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Stop,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		_compendiumView.AddChild(panel);

		var content = new MarginContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		content.AddThemeConstantOverride("margin_left", 18);
		content.AddThemeConstantOverride("margin_top", 18);
		content.AddThemeConstantOverride("margin_right", 18);
		content.AddThemeConstantOverride("margin_bottom", 18);
		panel.AddChild(content);

		var stack = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		stack.AddThemeConstantOverride("separation", 12);
		content.AddChild(stack);

		var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		header.AddThemeConstantOverride("separation", 12);
		stack.AddChild(header);

		var title = CreateTitle("COMPENDIUM");
		title.CustomMinimumSize = new Vector2(240, 32);
		title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		header.AddChild(title);

		_compendiumCount = CreateLabel("");
		_compendiumCount.CustomMinimumSize = new Vector2(160, 28);
		_compendiumCount.HorizontalAlignment = HorizontalAlignment.Right;
		header.AddChild(_compendiumCount);

		var back = CreateButton("BACK", ShowMainMenu);
		back.CustomMinimumSize = new Vector2(120, 38);
		header.AddChild(back);

		var controls = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		controls.AddThemeConstantOverride("separation", 10);
		stack.AddChild(controls);

		_cardSearch = new LineEdit
		{
			PlaceholderText = "Search card names",
			CustomMinimumSize = new Vector2(300, 38),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			FocusMode = Control.FocusModeEnum.Click
		};
		ApplyLineEditTheme(_cardSearch);
		_cardSearch.TextChanged += _ => RefreshCompendiumCards();
		controls.AddChild(_cardSearch);

		_cardSort = new OptionButton
		{
			CustomMinimumSize = new Vector2(150, 38),
			FocusMode = Control.FocusModeEnum.None
		};
		_cardSort.AddItem("A TO Z", 0);
		_cardSort.AddItem("Z TO A", 1);
		_cardSort.ItemSelected += _ => RefreshCompendiumCards();
		ApplyButtonTheme(_cardSort);
		controls.AddChild(_cardSort);

		var clear = CreateButton("CLEAR", ClearCompendiumFilters);
		clear.CustomMinimumSize = new Vector2(120, 38);
		controls.AddChild(clear);

		var body = new HBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		body.AddThemeConstantOverride("separation", 14);
		stack.AddChild(body);

		var filters = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(180, 0),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		filters.AddThemeConstantOverride("separation", 8);
		body.AddChild(filters);
		filters.AddChild(CreateLabel("Effects"));

		_effectFilterList = new VBoxContainer();
		_effectFilterList.AddThemeConstantOverride("separation", 4);
		filters.AddChild(_effectFilterList);
		BuildEffectFilters();

		var scroll = new ScrollContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		body.AddChild(scroll);

		_cardFlow = new HFlowContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_cardFlow.AddThemeConstantOverride("h_separation", 12);
		_cardFlow.AddThemeConstantOverride("v_separation", 12);
		scroll.AddChild(_cardFlow);

		RefreshCompendiumCards();
	}

	private void BuildBestiaryView()
	{
		_bestiaryView = new MarginContainer
		{
			Name = "BestiaryView",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_bestiaryView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_bestiaryView.AddThemeConstantOverride("margin_left", 24);
		_bestiaryView.AddThemeConstantOverride("margin_top", 24);
		_bestiaryView.AddThemeConstantOverride("margin_right", 24);
		_bestiaryView.AddThemeConstantOverride("margin_bottom", 24);
		_root.AddChild(_bestiaryView);

		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Stop,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		_bestiaryView.AddChild(panel);

		var content = new MarginContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		content.AddThemeConstantOverride("margin_left", 18);
		content.AddThemeConstantOverride("margin_top", 18);
		content.AddThemeConstantOverride("margin_right", 18);
		content.AddThemeConstantOverride("margin_bottom", 18);
		panel.AddChild(content);

		var stack = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		stack.AddThemeConstantOverride("separation", 12);
		content.AddChild(stack);

		var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		header.AddThemeConstantOverride("separation", 12);
		stack.AddChild(header);

		var title = CreateTitle("BESTIARY");
		title.CustomMinimumSize = new Vector2(240, 32);
		title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		header.AddChild(title);

		_bestiaryCount = CreateLabel("");
		_bestiaryCount.CustomMinimumSize = new Vector2(180, 28);
		_bestiaryCount.HorizontalAlignment = HorizontalAlignment.Right;
		header.AddChild(_bestiaryCount);

		var back = CreateButton("BACK", ShowMainMenu);
		back.CustomMinimumSize = new Vector2(120, 38);
		header.AddChild(back);

		var controls = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		controls.AddThemeConstantOverride("separation", 10);
		stack.AddChild(controls);
		controls.AddChild(CreateLabel("Sort by name"));

		_enemySort = new OptionButton
		{
			CustomMinimumSize = new Vector2(150, 38),
			FocusMode = Control.FocusModeEnum.None
		};
		_enemySort.AddItem("A TO Z", 0);
		_enemySort.AddItem("Z TO A", 1);
		_enemySort.ItemSelected += _ => RefreshBestiaryEnemies();
		ApplyButtonTheme(_enemySort);
		controls.AddChild(_enemySort);

		var scroll = new ScrollContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		stack.AddChild(scroll);

		_enemyFlow = new HFlowContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_enemyFlow.AddThemeConstantOverride("h_separation", 12);
		_enemyFlow.AddThemeConstantOverride("v_separation", 12);
		scroll.AddChild(_enemyFlow);

		BuildBestiaryModal();
		RefreshBestiaryEnemies();
	}

	private void BuildBestiaryModal()
	{
		_bestiaryModal = new Control
		{
			Name = "BestiaryModal",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_bestiaryModal.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_bestiaryView.AddChild(_bestiaryModal);

		var shade = new ColorRect
		{
			Color = new Color(0f, 0f, 0f, 0.72f),
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_bestiaryModal.AddChild(shade);

		var center = new CenterContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_bestiaryModal.AddChild(center);

		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(900, 620),
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		center.AddChild(panel);

		var content = new MarginContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		content.AddThemeConstantOverride("margin_left", 18);
		content.AddThemeConstantOverride("margin_top", 18);
		content.AddThemeConstantOverride("margin_right", 18);
		content.AddThemeConstantOverride("margin_bottom", 18);
		panel.AddChild(content);

		var stack = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		stack.AddThemeConstantOverride("separation", 12);
		content.AddChild(stack);

		var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		header.AddThemeConstantOverride("separation", 12);
		stack.AddChild(header);

		_bestiaryModalTitle = CreateTitle("");
		_bestiaryModalTitle.CustomMinimumSize = new Vector2(300, 32);
		_bestiaryModalTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		header.AddChild(_bestiaryModalTitle);

		_bestiaryModalHp = CreateLabel("");
		_bestiaryModalHp.CustomMinimumSize = new Vector2(140, 28);
		_bestiaryModalHp.HorizontalAlignment = HorizontalAlignment.Right;
		header.AddChild(_bestiaryModalHp);

		var close = CreateButton("CLOSE", CloseBestiaryModal);
		close.CustomMinimumSize = new Vector2(120, 38);
		header.AddChild(close);

		_bestiaryAnimationRow = new HBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		_bestiaryAnimationRow.AddThemeConstantOverride("separation", 12);
		stack.AddChild(_bestiaryAnimationRow);

		stack.AddChild(CreateLabel("Deck"));

		var scroll = new ScrollContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		stack.AddChild(scroll);

		_bestiaryDeckFlow = new HFlowContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_bestiaryDeckFlow.AddThemeConstantOverride("h_separation", 12);
		_bestiaryDeckFlow.AddThemeConstantOverride("v_separation", 12);
		scroll.AddChild(_bestiaryDeckFlow);
	}

	private void BuildEffectFilters()
	{
		_effectFilters.Clear();
		foreach (var entry in _effectNames.OrderBy(e => e.Value, StringComparer.OrdinalIgnoreCase))
		{
			var toggle = new CheckButton
			{
				Text = entry.Value.ToUpperInvariant(),
				FocusMode = Control.FocusModeEnum.None,
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			ApplyCheckButtonTheme(toggle);
			toggle.Toggled += _ => RefreshCompendiumCards();
			_effectFilterList.AddChild(toggle);
			_effectFilters[entry.Key] = toggle;
		}
	}

	private void RefreshCompendiumCards()
	{
		if (_cardFlow == null)
			return;

		foreach (Node child in _cardFlow.GetChildren())
		{
			_cardFlow.RemoveChild(child);
			child.QueueFree();
		}

		IEnumerable<CardData> cards = _allCards.Where(CardMatchesFilters);
		cards = _cardSort != null && _cardSort.Selected == 1
			? cards.OrderByDescending(c => c.Title ?? "", StringComparer.OrdinalIgnoreCase)
			: cards.OrderBy(c => c.Title ?? "", StringComparer.OrdinalIgnoreCase);

		int count = 0;
		foreach (var card in cards)
		{
			var cardView = _smallCardScene?.Instantiate<SmallCard>();
			if (cardView == null)
				continue;

			cardView.SetData(card);
			_cardFlow.AddChild(cardView);
			count++;
		}

		if (_compendiumCount != null)
			_compendiumCount.Text = $"{count}/{_allCards.Count} CARDS";
	}

	private void RefreshBestiaryEnemies()
	{
		if (_enemyFlow == null)
			return;

		ClearChildren(_enemyFlow);

		IEnumerable<EnemyDef> enemies = _enemySort != null && _enemySort.Selected == 1
			? _allEnemies.OrderByDescending(GetEnemyName, StringComparer.OrdinalIgnoreCase)
			: _allEnemies.OrderBy(GetEnemyName, StringComparer.OrdinalIgnoreCase);

		int count = 0;
		foreach (EnemyDef enemy in enemies)
		{
			_enemyFlow.AddChild(CreateBestiaryEnemyButton(enemy));
			count++;
		}

		if (_bestiaryCount != null)
			_bestiaryCount.Text = $"{count}/{_allEnemies.Count} ENEMIES";
	}

	private Button CreateBestiaryEnemyButton(EnemyDef enemy)
	{
		var button = new Button
		{
			Text = "",
			CustomMinimumSize = new Vector2(178, 214),
			FocusMode = Control.FocusModeEnum.None,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		ApplyButtonTheme(button);
		button.Pressed += () => OpenBestiaryModal(enemy);

		var margin = new MarginContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		button.AddChild(margin);

		var stack = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		stack.AddThemeConstantOverride("separation", 6);
		margin.AddChild(stack);

		var preview = new TextureRect
		{
			Texture = GetEnemyPreviewTexture(enemy),
			CustomMinimumSize = new Vector2(150, 112),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		stack.AddChild(preview);

		var name = CreateLabel(GetEnemyName(enemy).ToUpperInvariant());
		name.CustomMinimumSize = new Vector2(150, 24);
		stack.AddChild(name);

		var hp = CreateLabel($"{Mathf.Max(1, enemy?.MaxHP ?? 1)} HP");
		hp.CustomMinimumSize = new Vector2(150, 22);
		stack.AddChild(hp);

		var deck = CreateLabel($"{GetEnemyDeckCount(enemy)} CARDS");
		deck.CustomMinimumSize = new Vector2(150, 22);
		stack.AddChild(deck);

		return button;
	}

	private void OpenBestiaryModal(EnemyDef enemy)
	{
		if (enemy == null || _bestiaryModal == null)
			return;

		_activeAnimationPreviews.Clear();
		ClearChildren(_bestiaryAnimationRow);
		ClearChildren(_bestiaryDeckFlow);

		_bestiaryModalTitle.Text = GetEnemyName(enemy).ToUpperInvariant();
		_bestiaryModalHp.Text = $"{Mathf.Max(1, enemy.MaxHP)} HP";

		AddAnimationPreview(enemy, "IDLE", enemy.IdleSpriteSheet, enemy.IdleFrameSize, enemy.IdleSpriteFrameCount, enemy.IdleFrameSeconds);
		AddAnimationPreview(enemy, "ATTACK", enemy.AttackSpriteSheet, enemy.AttackFrameSize, enemy.AttackFrameCount, enemy.ActionFrameSeconds);
		AddAnimationPreview(enemy, "HIT", enemy.HitSpriteSheet, enemy.HitFrameSize, enemy.HitFrameCount, enemy.ActionFrameSeconds);

		if (_activeAnimationPreviews.Count == 0)
			AddStaticPreview(enemy);

		if (enemy.Deck is DeckList deckList && deckList.Cards != null && deckList.Cards.Count > 0)
		{
			foreach (CardData card in deckList.Cards)
			{
				var cardView = _smallCardScene?.Instantiate<SmallCard>();
				if (cardView == null)
					continue;

				cardView.SetData(card);
				_bestiaryDeckFlow.AddChild(cardView);
			}
		}
		else
		{
			_bestiaryDeckFlow.AddChild(CreateLabel("No deck assigned"));
		}

		_bestiaryModal.Visible = true;
	}

	private void CloseBestiaryModal()
	{
		if (_bestiaryModal != null)
			_bestiaryModal.Visible = false;

		_activeAnimationPreviews.Clear();
	}

	private void AddAnimationPreview(EnemyDef enemy, string label, Texture2D sheet, Vector2I frameSize, int explicitFrameCount, float frameSeconds)
	{
		List<AtlasTexture> frames = BuildHorizontalFrames(sheet, frameSize, explicitFrameCount);
		if (frames.Count == 0)
			return;

		var column = CreateAnimationPreviewColumn(label, frames[0]);
		var preview = column.GetNode<TextureRect>("Preview");
		_bestiaryAnimationRow.AddChild(column);

		_activeAnimationPreviews.Add(new AnimationPreview
		{
			View = preview,
			Frames = frames,
			FrameSeconds = frameSeconds
		});
	}

	private void AddStaticPreview(EnemyDef enemy)
	{
		Texture2D texture = GetEnemyPreviewTexture(enemy);
		if (texture == null)
			return;

		_bestiaryAnimationRow.AddChild(CreateAnimationPreviewColumn("PREVIEW", texture));
	}

	private VBoxContainer CreateAnimationPreviewColumn(string label, Texture2D texture)
	{
		var column = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(190, 160),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		column.AddThemeConstantOverride("separation", 6);

		var preview = new TextureRect
		{
			Name = "Preview",
			Texture = texture,
			CustomMinimumSize = new Vector2(180, 120),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		column.AddChild(preview);

		var caption = CreateLabel(label);
		caption.CustomMinimumSize = new Vector2(180, 22);
		column.AddChild(caption);

		return column;
	}

	private Texture2D GetEnemyPreviewTexture(EnemyDef enemy)
	{
		if (enemy == null)
			return null;

		var idleFrames = BuildHorizontalFrames(enemy.IdleSpriteSheet, enemy.IdleFrameSize, enemy.IdleSpriteFrameCount);
		if (idleFrames.Count > 0)
			return idleFrames[0];

		var legacyFrames = BuildHorizontalFrames(enemy.SpriteSheet, enemy.FrameSize, enemy.IdleFrameCount);
		if (legacyFrames.Count > 0)
			return legacyFrames[0];

		return enemy.Art;
	}

	private List<AtlasTexture> BuildHorizontalFrames(Texture2D sheet, Vector2I frameSize, int explicitFrameCount)
	{
		var frames = new List<AtlasTexture>();
		if (sheet == null || frameSize.X <= 0 || frameSize.Y <= 0)
			return frames;

		int availableFrames = Mathf.Max(1, sheet.GetWidth() / frameSize.X);
		int frameCount = explicitFrameCount > 0 ? Mathf.Min(explicitFrameCount, availableFrames) : availableFrames;
		for (int i = 0; i < frameCount; i++)
		{
			frames.Add(new AtlasTexture
			{
				Atlas = sheet,
				Region = new Rect2(i * frameSize.X, 0, frameSize.X, frameSize.Y)
			});
		}

		return frames;
	}

	private string GetEnemyName(EnemyDef enemy)
	{
		if (enemy == null)
			return "";

		return string.IsNullOrWhiteSpace(enemy.Name) ? enemy.Id ?? "" : enemy.Name;
	}

	private int GetEnemyDeckCount(EnemyDef enemy)
	{
		return enemy?.Deck is DeckList deckList && deckList.Cards != null ? deckList.Cards.Count : 0;
	}

	private void ClearChildren(Node node)
	{
		if (node == null)
			return;

		foreach (Node child in node.GetChildren())
		{
			node.RemoveChild(child);
			child.QueueFree();
		}
	}

	private bool CardMatchesFilters(CardData card)
	{
		if (card == null)
			return false;

		string search = _cardSearch?.Text?.Trim() ?? "";
		if (!string.IsNullOrWhiteSpace(search) && (card.Title ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
			return false;

		var selectedEffects = _effectFilters
			.Where(entry => entry.Value.ButtonPressed)
			.Select(entry => entry.Key)
			.ToArray();
		if (selectedEffects.Length == 0)
			return true;

		var cardEffects = GetCardEffectIds(card);
		return selectedEffects.Any(cardEffects.Contains);
	}

	private HashSet<string> GetCardEffectIds(CardData card)
	{
		var ids = new HashSet<string>();
		if (card?.Effects == null)
			return ids;

		foreach (var effect in card.Effects)
		{
			string id = effect?.Def?.Id;
			if (!string.IsNullOrWhiteSpace(id))
				ids.Add(id);
		}

		return ids;
	}

	private string GetEffectDisplayName(CardData card, string effectId)
	{
		if (card?.Effects != null)
		{
			foreach (var effect in card.Effects)
			{
				if (effect?.Def?.Id == effectId)
					return string.IsNullOrWhiteSpace(effect.Def.DisplayName) ? effectId : effect.Def.DisplayName;
			}
		}

		return effectId;
	}

	private void ClearCompendiumFilters()
	{
		if (_cardSearch != null)
			_cardSearch.Text = "";
		if (_cardSort != null)
			_cardSort.Select(0);
		foreach (var toggle in _effectFilters.Values)
			toggle.ButtonPressed = false;
		RefreshCompendiumCards();
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

	private void ApplyCheckButtonTheme(CheckButton button)
	{
		button.AddThemeFontOverride("font", BodyFont);
		button.AddThemeFontSizeOverride("font_size", BodyFontSize);
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		button.AddThemeColorOverride("font_pressed_color", Colors.White);
		button.AddThemeColorOverride("font_focus_color", Colors.White);
		button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
	}

	private void ApplyLineEditTheme(LineEdit input)
	{
		input.AddThemeFontOverride("font", BodyFont);
		input.AddThemeFontSizeOverride("font_size", BodyFontSize);
		input.AddThemeColorOverride("font_color", Colors.White);
		input.AddThemeColorOverride("caret_color", Colors.White);
		input.AddThemeColorOverride("font_placeholder_color", new Color(0.68f, 0.76f, 0.82f));
		input.AddThemeStyleboxOverride("normal", CreateInputStyle(new Color(0.04f, 0.10f, 0.14f)));
		input.AddThemeStyleboxOverride("focus", CreateInputStyle(new Color(0.07f, 0.16f, 0.22f)));
		input.AddThemeStyleboxOverride("read_only", CreateInputStyle(new Color(0.04f, 0.10f, 0.14f)));
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

	private StyleBoxFlat CreateInputStyle(Color color)
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
		if (_compendiumView != null)
			_compendiumView.Visible = false;
		if (_bestiaryView != null)
			_bestiaryView.Visible = false;
		CloseBestiaryModal();
	}

	private void ShowSettings()
	{
		_mainMenu.Visible = false;
		_settingsMenu.Visible = true;
		if (_compendiumView != null)
			_compendiumView.Visible = false;
		if (_bestiaryView != null)
			_bestiaryView.Visible = false;
		CloseBestiaryModal();
	}

	private void ShowCompendium()
	{
		_mainMenu.Visible = false;
		if (_settingsMenu != null)
			_settingsMenu.Visible = false;
		if (_compendiumView != null)
			_compendiumView.Visible = true;
		if (_bestiaryView != null)
			_bestiaryView.Visible = false;
		CloseBestiaryModal();
		RefreshCompendiumCards();
	}

	private void ShowBestiary()
	{
		_mainMenu.Visible = false;
		if (_settingsMenu != null)
			_settingsMenu.Visible = false;
		if (_compendiumView != null)
			_compendiumView.Visible = false;
		if (_bestiaryView != null)
			_bestiaryView.Visible = true;
		RefreshBestiaryEnemies();
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

		return 3;
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
