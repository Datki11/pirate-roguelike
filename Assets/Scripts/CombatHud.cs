using Godot;

public partial class CombatHud : Control
{
	[Export] public NodePath EnergyPath { get; set; }
	[Export] public FontFile Font { get; set; }

	private readonly Color _panel = new(0.98f, 0.98f, 0.92f);
	private readonly Color _ink = Colors.Black;
	private readonly Color _energy = new(1.0f, 0.74f, 0.12f);
	private readonly Color _button = new(0.82f, 0.12f, 0.10f);
	private readonly Color _buttonHover = new(0.95f, 0.18f, 0.14f);

	private EnergyManager _energyManager;
	private Rect2 _endTurnRect;
	private bool _hoverEndTurn;
	private bool _lastCanEndTurn;
	private int _current;
	private int _max;

	public override void _Ready()
	{
		Visible = true;
		MouseFilter = MouseFilterEnum.Stop;
		_energyManager = GetNodeOrNull<EnergyManager>(EnergyPath);
		if (Font == null) Font = ThemeDB.FallbackFont as FontFile;
		ConfigurePixelFont(Font);

		if (_energyManager != null)
		{
			_current = _energyManager.CurrentEnergy;
			_max = _energyManager.MaxEnergy;
			_energyManager.EnergyChanged += OnEnergyChanged;
			_energyManager.PlayerTurnStarted += OnTurnStateChanged;
			_energyManager.PlayerTurnEnded += OnTurnStateChanged;
		}
		QueueRedraw();
	}

	public override bool _HasPoint(Vector2 point)
	{
		if (!CanEndTurn())
			return false;

		return GetEndTurnRect().HasPoint(point);
	}

	public override void _ExitTree()
	{
		if (_energyManager != null)
		{
			_energyManager.EnergyChanged -= OnEnergyChanged;
			_energyManager.PlayerTurnStarted -= OnTurnStateChanged;
			_energyManager.PlayerTurnEnded -= OnTurnStateChanged;
		}
	}

	public override void _Process(double delta)
	{
		bool canEndTurn = CanEndTurn();
		if (canEndTurn != _lastCanEndTurn)
		{
			_lastCanEndTurn = canEndTurn;
			if (!canEndTurn)
				_hoverEndTurn = false;
			QueueRedraw();
		}

		if (Deck.IsDrawPileModalOpen)
		{
			if (_hoverEndTurn)
			{
				_hoverEndTurn = false;
				QueueRedraw();
			}
			return;
		}

		var mouse = GetLocalMousePosition();
		bool hover = canEndTurn && GetEndTurnRect().HasPoint(mouse);
		if (hover != _hoverEndTurn)
		{
			_hoverEndTurn = hover;
			QueueRedraw();
		}
	}

	public override void _GuiInput(InputEvent e)
	{
		if (Deck.IsDrawPileModalOpen)
		{
			AcceptEvent();
			return;
		}

		if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && CanEndTurn() && _endTurnRect.HasPoint(mb.Position))
		{
			_energyManager?.EndPlayerTurn();
			AcceptEvent();
		}
	}

	public override void _Draw()
	{
		Vector2 size = Size;
		_endTurnRect = GetEndTurnRect();

		DrawEnergyPanel(size);
		if (CanEndTurn())
			DrawEndTurnButton();
	}

	private void DrawEnergyPanel(Vector2 size)
	{
		var rect = GetEnergyRect(size);
		DrawRect(rect, _panel, true);
		DrawRect(rect, _ink, false, 2);

		var pipSize = new Vector2(24, 24);
		for (int i = 0; i < _max; i++)
		{
			var pip = new Rect2(rect.Position + new Vector2(10 + i * 34, 8), pipSize);
			DrawRect(pip, i < _current ? _energy : Colors.White, true);
			DrawRect(pip, _ink, false, 2);
		}

		DrawPixelText($"{_current}/{_max} ENERGY", rect.Position + new Vector2(10, 48), 16, _ink);
	}

	private void DrawEndTurnButton()
	{
		DrawRect(_endTurnRect, _hoverEndTurn ? _buttonHover : _button, true);
		DrawRect(_endTurnRect, _ink, false, 2);
		DrawPixelText("END TURN", _endTurnRect.Position + new Vector2(25, 29), 18, Colors.White);
	}

	private Rect2 GetEndTurnRect()
	{
		Vector2 size = Size;
		const float energyWidth = 132f;
		const float gap = 24f;
		const float buttonWidth = 160f;
		var groupX = Mathf.Round((size.X - energyWidth - gap - buttonWidth) * 0.5f);
		return new Rect2(new Vector2(groupX + energyWidth + gap, size.Y - 79).Floor(), new Vector2(buttonWidth, 42));
	}

	private Rect2 GetEnergyRect(Vector2 size)
	{
		const float energyWidth = 132f;
		const float gap = 24f;
		const float buttonWidth = 160f;
		var groupX = Mathf.Round((size.X - energyWidth - gap - buttonWidth) * 0.5f);
		return new Rect2(new Vector2(groupX, size.Y - 86).Floor(), new Vector2(energyWidth, 56));
	}

	private bool CanEndTurn()
	{
		return _energyManager != null && _energyManager.IsPlayerTurn && !Deck.IsDrawPileModalOpen;
	}

	private void DrawPixelText(string text, Vector2 baseline, int size, Color color)
	{
		if (Font == null) return;
		DrawString(Font, baseline.Floor(), text, HorizontalAlignment.Left, -1, size, color);
	}

	private void OnEnergyChanged(int current, int max)
	{
		_current = current;
		_max = max;
		QueueRedraw();
	}

	private void OnTurnStateChanged()
	{
		if (!CanEndTurn())
			_hoverEndTurn = false;
		QueueRedraw();
	}

	private void ConfigurePixelFont(Font font)
	{
		if (font is FontFile fontFile)
		{
			fontFile.Antialiasing = TextServer.FontAntialiasing.None;
			fontFile.GenerateMipmaps = false;
			fontFile.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
		}
	}
}
