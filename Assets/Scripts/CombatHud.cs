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
		}
		QueueRedraw();
	}

	public override bool _HasPoint(Vector2 point)
	{
		return GetEndTurnRect().HasPoint(point);
	}

	public override void _ExitTree()
	{
		if (_energyManager != null) _energyManager.EnergyChanged -= OnEnergyChanged;
	}

	public override void _Process(double delta)
	{
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
		bool hover = GetEndTurnRect().HasPoint(mouse);
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

		if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && _endTurnRect.HasPoint(mb.Position))
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
		DrawEndTurnButton();
	}

	private void DrawEnergyPanel(Vector2 size)
	{
		var rect = new Rect2(new Vector2(30, size.Y - 86).Floor(), new Vector2(132, 56));
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
		return new Rect2(new Vector2(size.X - 190, size.Y - 70).Floor(), new Vector2(160, 42));
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
