using Godot;

public partial class Enemy : Control, IDamageable
{
	[Export] public EnemyDef Def { get; set; }

	[Export] public NodePath SpritePath { get; set; }
	[Export] public NodePath HpPath     { get; set; }
	[Export] public NodePath RingPath   { get; set; }
	[Export] public NodePath PopupAnchorPath { get; set; }

	// Deck + combat
	[Export] public NodePath DeckPath { get; set; }
	[Export] public NodePath CombatPath { get; set; }
	[Export] public float ThinkDelaySec { get; set; } = 0.6f;

	private TextureRect _sprite;
	private HPBar _hp;
	private Control _ring;
	private TargetRing _ringTR;     // <— cached cast
	private Control _popupAnchor;

	private Deck _deck;
	private CombatManager _combat;

	// --- Signals (ADD THIS BACK) ---
	[Signal] public delegate void ClickedEventHandler(Enemy who);

	// IDamageable
	public int MaxHP { get; private set; } = 10;
	public int HP    { get; private set; } = 10;
	public bool Alive => HP > 0;

	[Signal] public delegate void DamagedEventHandler(int amount);
	[Signal] public delegate void HealedEventHandler(int amount);
	[Signal] public delegate void DiedEventHandler();

	public override void _Ready()
	{
		_sprite      = GetNodeOrNull<TextureRect>(SpritePath);
		_hp          = GetNodeOrNull<HPBar>(HpPath);
		_ring        = GetNodeOrNull<Control>(RingPath);
		_ringTR      = _ring as TargetRing;           // <— keep a typed ref
		_popupAnchor = GetNodeOrNull<Control>(PopupAnchorPath);

		// We want clicks on the whole enemy rect
		MouseFilter = MouseFilterEnum.Stop;
		if (_sprite != null) _sprite.MouseFilter = MouseFilterEnum.Pass;
		if (_hp     != null) _hp.MouseFilter     = MouseFilterEnum.Pass;
		if (_ring   != null) _ring.MouseFilter   = MouseFilterEnum.Pass;
		
		// hook hover
		MouseEntered += OnMouseEntered;
		MouseExited  += OnMouseExited;

		// ------ NAME ALIGNMENT: EnemyDef.MaxHP ------
		if (Def != null)
		{
			MaxHP = Mathf.Max(1, Def.MaxHP);           // << use MaxHP (capital HP)
			HP    = MaxHP;
			if (Def.Art != null && _sprite != null) _sprite.Texture = Def.Art;
		}
		_hp?.Set(HP, MaxHP);

		_deck   = GetNodeOrNull<Deck>(DeckPath);
		_combat = GetNodeOrNull<CombatManager>(CombatPath)
				  ?? GetTree().Root.FindChild("CombatManager", true, false) as CombatManager;

		if (_deck != null)
		{
			_deck.EnableInput = false;
			_deck.DiscardOnTopClick = false;
			_deck.Side = Deck.DeckSide.Enemy;
			_deck.PlayRequested += OnDeckPlayRequested;
		}

		_combat?.RegisterEnemy(this);
		SetTargetable(false);
	}

	public override void _ExitTree()
	{
		if (_deck != null) _deck.PlayRequested -= OnDeckPlayRequested;
		_combat?.UnregisterEnemy(this);
	}

	// Emit Clicked so CombatTargeting keeps working
	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
			EmitSignal(SignalName.Clicked, this);
	}

	private void OnDeckPlayRequested(Deck deck, CardData card)
	{
		if (!Alive || _combat == null || card == null) return;
		_combat.PlayCardAuto(deck, card, Deck.DeckSide.Enemy);
	}

	public async void PlayTurn()
	{
		if (!Alive || _deck == null) return;
		if (_deck.Peek() == null) _deck.EnsureTop();
		await ToSignal(GetTree().CreateTimer(ThinkDelaySec), "timeout");
		_deck.RequestPlay();
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0 || !Alive) return;
		HP = Mathf.Max(0, HP - amount);
		_hp?.Set(HP, MaxHP);
		EmitSignal(SignalName.Damaged, amount);
		if (!Alive)
		{
			SetTargetable(false);
			EmitSignal(SignalName.Died);
		}
	}

	public void Heal(int amount)
	{
		if (amount <= 0 || !Alive) return;
		HP = Mathf.Min(MaxHP, HP + amount);
		_hp?.Set(HP, MaxHP);
		EmitSignal(SignalName.Healed, amount);
	}

	public Vector2 GetPopupAnchorGlobal()
	{
		if (_popupAnchor != null) return _popupAnchor.GlobalPosition;
		var r = GetGlobalRect();
		return new Vector2(r.Position.X + r.Size.X * 0.5f, r.Position.Y);
	}

	public void SetTargetable(bool on)
	{
		if (_ring != null) _ring.Visible = on;
		if (!on && _ring is TargetRing tr) tr.SetHover(false);
	}
	
	private void OnMouseEntered()
	{
		// Only show hover while targetable
		if (_ring != null && _ring.Visible) _ringTR?.SetHover(true);
	}

	private void OnMouseExited()
	{
		_ringTR?.SetHover(false);
	}
}
