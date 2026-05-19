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
	private Control _heroesView;
	private HFlowContainer _cardFlow;
	private VBoxContainer _enemyList;
	private VBoxContainer _heroList;
	private LineEdit _cardSearch;
	private OptionButton _cardSort;
	private OptionButton _enemySort;
	private OptionButton _heroSort;
	private Label _compendiumCount;
	private Label _bestiaryCount;
	private Label _heroesCount;
	private VBoxContainer _rarityFilterList;
	private VBoxContainer _effectFilterList;
	private OptionButton _speedSelect;
	private float _currentSpeed = 1f;
	private PackedScene _smallCardScene;
	private readonly List<CardData> _allCards = new();
	private readonly List<EnemyDef> _allEnemies = new();
	private readonly Dictionary<string, string> _effectNames = new();
	private readonly Dictionary<CardClass, CheckButton> _rarityFilters = new();
	private readonly Dictionary<string, CheckButton> _effectFilters = new();
	private readonly List<AnimationPreview> _activeAnimationPreviews = new();

	private sealed class AnimationPreview
	{
		public TextureRect View;
		public Label Caption;
		public List<AnimationClip> Clips = new();
		public List<AtlasTexture> Frames = new();
		public float FrameSeconds = 0.12f;
		public float Time;
		public int Index;
		public int ClipIndex;
	}

	private sealed class AnimationClip
	{
		public string Label = "";
		public List<AtlasTexture> Frames = new();
		public float FrameSeconds = 0.12f;
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
		if (_activeAnimationPreviews.Count == 0)
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
		bool pauseToggle = IsPauseTogglePressed(e);
		bool menuCancel = IsMenuCancelPressed(e);
		if (!pauseToggle && !menuCancel)
			return;

		if (_root != null && _root.Visible)
		{
			if ((_compendiumView != null && _compendiumView.Visible) || (_bestiaryView != null && _bestiaryView.Visible) || (_heroesView != null && _heroesView.Visible) || (_settingsMenu != null && _settingsMenu.Visible))
				ShowMainMenu();
			else
				ResumeGame();
		}
		else
		{
			if (!pauseToggle)
				return;

			ShowPause();
		}

		GetViewport().SetInputAsHandled();
	}

	private bool IsPauseTogglePressed(InputEvent e)
	{
		if (e is InputEventKey key)
			return key.Pressed && !key.Echo && key.Keycode == Key.Escape;

		return e is InputEventJoypadButton button && button.Pressed && button.ButtonIndex == JoyButton.Start;
	}

	private bool IsMenuCancelPressed(InputEvent e)
	{
		if (e is InputEventKey key && (!key.Pressed || key.Echo))
			return false;

		if (e.IsActionPressed("ui_cancel"))
			return true;

		return e is InputEventJoypadButton button && button.Pressed && button.ButtonIndex == JoyButton.B;
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
		_mainMenu.AddChild(CreateButton("HEROES", ShowHeroes));
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
		BuildHeroesView();
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

	private Label CreateWrappedLabel(string text)
	{
		var label = CreateLabel(text);
		label.HorizontalAlignment = HorizontalAlignment.Left;
		label.VerticalAlignment = VerticalAlignment.Top;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		return label;
	}

	private Button CreateButton(string text, System.Action pressed)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(220, 38),
			FocusMode = Control.FocusModeEnum.All,
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
			FocusMode = Control.FocusModeEnum.All,
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
			if (card is MonsterCardData || card.Class == CardClass.Enemy)
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

		filters.AddChild(CreateLabel("Rarities"));
		_rarityFilterList = new VBoxContainer();
		_rarityFilterList.AddThemeConstantOverride("separation", 4);
		filters.AddChild(_rarityFilterList);
		BuildRarityFilters();

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

		_enemyList = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_enemyList.AddThemeConstantOverride("separation", 14);
		scroll.AddChild(_enemyList);

		RefreshBestiaryEnemies();
	}

	private void BuildHeroesView()
	{
		_heroesView = new MarginContainer
		{
			Name = "HeroesView",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_heroesView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_heroesView.AddThemeConstantOverride("margin_left", 24);
		_heroesView.AddThemeConstantOverride("margin_top", 24);
		_heroesView.AddThemeConstantOverride("margin_right", 24);
		_heroesView.AddThemeConstantOverride("margin_bottom", 24);
		_root.AddChild(_heroesView);

		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Stop,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		_heroesView.AddChild(panel);

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

		var title = CreateTitle("HEROES");
		title.CustomMinimumSize = new Vector2(240, 32);
		title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		header.AddChild(title);

		_heroesCount = CreateLabel("");
		_heroesCount.CustomMinimumSize = new Vector2(160, 28);
		_heroesCount.HorizontalAlignment = HorizontalAlignment.Right;
		header.AddChild(_heroesCount);

		var back = CreateButton("BACK", ShowMainMenu);
		back.CustomMinimumSize = new Vector2(120, 38);
		header.AddChild(back);

		var controls = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		controls.AddThemeConstantOverride("separation", 10);
		stack.AddChild(controls);
		controls.AddChild(CreateLabel("Sort by name"));

		_heroSort = new OptionButton
		{
			CustomMinimumSize = new Vector2(150, 38),
			FocusMode = Control.FocusModeEnum.None
		};
		_heroSort.AddItem("A TO Z", 0);
		_heroSort.AddItem("Z TO A", 1);
		_heroSort.ItemSelected += _ => RefreshHeroes();
		ApplyButtonTheme(_heroSort);
		controls.AddChild(_heroSort);

		var scroll = new ScrollContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		stack.AddChild(scroll);

		_heroList = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_heroList.AddThemeConstantOverride("separation", 14);
		scroll.AddChild(_heroList);

		RefreshHeroes();
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

	private void BuildRarityFilters()
	{
		_rarityFilters.Clear();
		foreach (CardClass rarity in new[] { CardClass.Basic, CardClass.Common, CardClass.Uncommon, CardClass.Rare })
		{
			var toggle = new CheckButton
			{
				Text = rarity.ToString().ToUpperInvariant(),
				FocusMode = Control.FocusModeEnum.None,
				MouseFilter = Control.MouseFilterEnum.Stop
			};
			ApplyCheckButtonTheme(toggle);
			toggle.Toggled += _ => RefreshCompendiumCards();
			_rarityFilterList.AddChild(toggle);
			_rarityFilters[rarity] = toggle;
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
		if (_enemyList == null)
			return;

		ClearChildren(_enemyList);
		_activeAnimationPreviews.Clear();

		IEnumerable<EnemyDef> enemies = _enemySort != null && _enemySort.Selected == 1
			? _allEnemies.OrderByDescending(GetEnemyName, StringComparer.OrdinalIgnoreCase)
			: _allEnemies.OrderBy(GetEnemyName, StringComparer.OrdinalIgnoreCase);

		int count = 0;
		foreach (EnemyDef enemy in enemies)
		{
			_enemyList.AddChild(CreateBestiaryEnemyPanel(enemy));
			count++;
		}

		if (_bestiaryCount != null)
			_bestiaryCount.Text = $"{count}/{_allEnemies.Count} ENEMIES";
	}

	private void RefreshHeroes()
	{
		if (_heroList == null)
			return;

		ClearChildren(_heroList);

		List<PlayerUnit> heroes = GetCurrentHeroes();
		IEnumerable<PlayerUnit> sortedHeroes = _heroSort != null && _heroSort.Selected == 1
			? heroes.OrderByDescending(h => h.GetHeroName(), StringComparer.OrdinalIgnoreCase)
			: heroes.OrderBy(h => h.GetHeroName(), StringComparer.OrdinalIgnoreCase);

		int count = 0;
		foreach (PlayerUnit hero in sortedHeroes)
		{
			_heroList.AddChild(CreateHeroInfoPanel(hero));
			count++;
		}

		if (count == 0)
			_heroList.AddChild(CreateLabel("No heroes found"));

		if (_heroesCount != null)
			_heroesCount.Text = $"{count} HEROES";
	}

	private PanelContainer CreateHeroInfoPanel(PlayerUnit hero)
	{
		DeckList deck = hero.GetHeroDeckList();
		return CreateUnitInfoPanel(
			hero.GetHeroName(),
			hero.GetHeroRole(),
			$"{Mathf.Max(0, hero.HP)}/{Mathf.Max(1, hero.MaxHP)} HP",
			hero.HeroDescription,
			deck,
			summary => AddStaticPreview(summary, hero.GetHeroPreviewTexture(), new Vector2(190, 130))
		);
	}

	private PanelContainer CreateBestiaryEnemyPanel(EnemyDef enemy)
	{
		return CreateUnitInfoPanel(
			GetEnemyName(enemy),
			"Enemy",
			$"{Mathf.Max(1, enemy?.MaxHP ?? 1)} HP",
			"",
			enemy?.Deck as DeckList,
			summary => AddEnemyPreviews(summary, enemy)
		);
	}

	private PanelContainer CreateUnitInfoPanel(string unitName, string subtitle, string statsText, string description, DeckList deck, Action<VBoxContainer> addPreview)
	{
		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Stop,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		panel.AddThemeStyleboxOverride("panel", CreateButtonStyle(new Color(0.06f, 0.13f, 0.17f)));

		var margin = new MarginContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		var row = new HBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		row.AddThemeConstantOverride("separation", 14);
		margin.AddChild(row);

		var summary = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(220, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		summary.AddThemeConstantOverride("separation", 6);
		row.AddChild(summary);

		addPreview?.Invoke(summary);

		var name = CreateTitle((unitName ?? "").ToUpperInvariant());
		name.CustomMinimumSize = new Vector2(220, 30);
		name.HorizontalAlignment = HorizontalAlignment.Left;
		summary.AddChild(name);

		var role = CreateLabel((subtitle ?? "").ToUpperInvariant());
		role.CustomMinimumSize = new Vector2(220, 22);
		role.HorizontalAlignment = HorizontalAlignment.Left;
		summary.AddChild(role);

		var stats = CreateLabel(statsText ?? "");
		stats.CustomMinimumSize = new Vector2(220, 22);
		stats.HorizontalAlignment = HorizontalAlignment.Left;
		summary.AddChild(stats);

		if (!string.IsNullOrWhiteSpace(description))
		{
			var descriptionLabel = CreateWrappedLabel(description);
			descriptionLabel.CustomMinimumSize = new Vector2(220, 48);
			summary.AddChild(descriptionLabel);
		}

		var deckColumn = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		deckColumn.AddThemeConstantOverride("separation", 8);
		row.AddChild(deckColumn);

		int deckCount = deck?.Cards?.Count ?? 0;
		var deckTitle = CreateLabel($"DECK - {deckCount} CARDS");
		deckTitle.HorizontalAlignment = HorizontalAlignment.Left;
		deckTitle.CustomMinimumSize = new Vector2(320, 24);
		deckColumn.AddChild(deckTitle);

		var cardFlow = new HFlowContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		cardFlow.AddThemeConstantOverride("h_separation", 10);
		cardFlow.AddThemeConstantOverride("v_separation", 10);
		deckColumn.AddChild(cardFlow);

		if (deck?.Cards != null && deck.Cards.Count > 0)
		{
			foreach (CardData card in deck.Cards)
			{
				var cardView = _smallCardScene?.Instantiate<SmallCard>();
				if (cardView == null)
					continue;

				cardView.SetData(card);
				cardFlow.AddChild(cardView);
			}
		}
		else
		{
			cardFlow.AddChild(CreateLabel("No deck assigned"));
		}

		return panel;
	}

	private void AddEnemyPreviews(VBoxContainer summary, EnemyDef enemy)
	{
		var clips = new List<AnimationClip>();
		AddAnimationClip(clips, "IDLE", enemy?.IdleSpriteSheet, enemy?.IdleFrameSize ?? Vector2I.Zero, enemy?.IdleSpriteFrameCount ?? 0, enemy?.IdleFrameSeconds ?? 0.12f);
		AddAnimationClip(clips, "ATTACK", enemy?.AttackSpriteSheet, enemy?.AttackFrameSize ?? Vector2I.Zero, enemy?.AttackFrameCount ?? 0, enemy?.ActionFrameSeconds ?? 0.07f);
		AddAnimationClip(clips, "HIT", enemy?.HitSpriteSheet, enemy?.HitFrameSize ?? Vector2I.Zero, enemy?.HitFrameCount ?? 0, enemy?.ActionFrameSeconds ?? 0.07f);

		if (clips.Count == 0)
		{
			AddStaticPreview(summary, GetEnemyPreviewTexture(enemy), new Vector2(190, 130));
			return;
		}

		AddAnimationSelector(summary, clips);
	}

	private void AddAnimationClip(List<AnimationClip> clips, string label, Texture2D sheet, Vector2I frameSize, int explicitFrameCount, float frameSeconds)
	{
		List<AtlasTexture> frames = BuildHorizontalFrames(sheet, frameSize, explicitFrameCount);
		if (frames.Count == 0)
			return;

		clips.Add(new AnimationClip
		{
			Label = label,
			Frames = frames,
			FrameSeconds = frameSeconds
		});
	}

	private void AddAnimationSelector(VBoxContainer parent, List<AnimationClip> clips)
	{
		var row = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(220, 160),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		row.AddThemeConstantOverride("separation", 6);
		parent.AddChild(row);

		var previous = CreateButton("<", () => { });
		previous.CustomMinimumSize = new Vector2(34, 34);
		previous.Disabled = clips.Count <= 1;
		row.AddChild(previous);

		var column = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(140, 154),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		column.AddThemeConstantOverride("separation", 6);
		row.AddChild(column);

		var preview = new TextureRect
		{
			Texture = clips[0].Frames[0],
			CustomMinimumSize = new Vector2(140, 120),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		column.AddChild(preview);

		var caption = CreateLabel(clips[0].Label);
		caption.CustomMinimumSize = new Vector2(140, 22);
		column.AddChild(caption);

		var next = CreateButton(">", () => { });
		next.CustomMinimumSize = new Vector2(34, 34);
		next.Disabled = clips.Count <= 1;
		row.AddChild(next);

		var animationPreview = new AnimationPreview
		{
			View = preview,
			Caption = caption,
			Clips = clips
		};
		ApplyAnimationClip(animationPreview, 0);
		_activeAnimationPreviews.Add(animationPreview);

		previous.Pressed += () => CycleAnimationClip(animationPreview, -1);
		next.Pressed += () => CycleAnimationClip(animationPreview, 1);
	}

	private void CycleAnimationClip(AnimationPreview preview, int direction)
	{
		if (preview?.Clips == null || preview.Clips.Count == 0)
			return;

		int index = (preview.ClipIndex + direction) % preview.Clips.Count;
		if (index < 0)
			index += preview.Clips.Count;

		ApplyAnimationClip(preview, index);
	}

	private void ApplyAnimationClip(AnimationPreview preview, int index)
	{
		if (preview?.Clips == null || preview.Clips.Count == 0)
			return;

		index = Mathf.Clamp(index, 0, preview.Clips.Count - 1);
		AnimationClip clip = preview.Clips[index];
		preview.ClipIndex = index;
		preview.Frames = clip.Frames;
		preview.FrameSeconds = clip.FrameSeconds;
		preview.Time = 0f;
		preview.Index = 0;
		if (preview.View != null && preview.Frames.Count > 0)
			preview.View.Texture = preview.Frames[0];
		if (preview.Caption != null)
			preview.Caption.Text = clip.Label;
	}

	private void AddStaticPreview(VBoxContainer parent, Texture2D texture, Vector2 size)
	{
		if (texture == null)
			return;

		var preview = new TextureRect
		{
			Texture = texture,
			CustomMinimumSize = size,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		parent.AddChild(preview);
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

	private List<PlayerUnit> GetCurrentHeroes()
	{
		var heroes = new List<PlayerUnit>();
		CollectHeroes(GetTree().CurrentScene, heroes);
		return heroes;
	}

	private void CollectHeroes(Node node, List<PlayerUnit> heroes)
	{
		if (node == null)
			return;

		if (node is PlayerUnit hero)
			heroes.Add(hero);

		foreach (Node child in node.GetChildren())
			CollectHeroes(child, heroes);
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

		var selectedRarities = _rarityFilters
			.Where(entry => entry.Value.ButtonPressed)
			.Select(entry => entry.Key)
			.ToArray();
		if (selectedRarities.Length > 0 && !selectedRarities.Contains(card.Class))
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
		foreach (var toggle in _rarityFilters.Values)
			toggle.ButtonPressed = false;
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
		GrabFirstMenuFocus(_mainMenu);
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
		if (_heroesView != null)
			_heroesView.Visible = false;
		_activeAnimationPreviews.Clear();
		GrabFirstMenuFocus(_mainMenu);
	}

	private void ShowSettings()
	{
		_mainMenu.Visible = false;
		_settingsMenu.Visible = true;
		if (_compendiumView != null)
			_compendiumView.Visible = false;
		if (_bestiaryView != null)
			_bestiaryView.Visible = false;
		if (_heroesView != null)
			_heroesView.Visible = false;
		_activeAnimationPreviews.Clear();
		GrabFirstMenuFocus(_settingsMenu);
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
		if (_heroesView != null)
			_heroesView.Visible = false;
		_activeAnimationPreviews.Clear();
		RefreshCompendiumCards();
		GrabFirstMenuFocus(_compendiumView);
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
		if (_heroesView != null)
			_heroesView.Visible = false;
		RefreshBestiaryEnemies();
		GrabFirstMenuFocus(_bestiaryView);
	}

	private void ShowHeroes()
	{
		_mainMenu.Visible = false;
		if (_settingsMenu != null)
			_settingsMenu.Visible = false;
		if (_compendiumView != null)
			_compendiumView.Visible = false;
		if (_bestiaryView != null)
			_bestiaryView.Visible = false;
		if (_heroesView != null)
			_heroesView.Visible = true;
		_activeAnimationPreviews.Clear();
		RefreshHeroes();
		GrabFirstMenuFocus(_heroesView);
	}

	private void GrabFirstMenuFocus(Control root)
	{
		if (root == null || !root.Visible)
			return;

		foreach (Node child in root.GetChildren())
		{
			if (child is Control control)
			{
				if (control.Visible && control.FocusMode != Control.FocusModeEnum.None)
				{
					control.GrabFocus();
					return;
				}

				GrabFirstMenuFocus(control);
				if (GetViewport().GuiGetFocusOwner() != null)
					return;
			}
		}
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
