using Godot;

public partial class Enemy : Control, IDamageable
{
	// Data
	[Export] public EnemyDef Def { get; set; }

	// Scene refs
	[Export] public NodePath SpritePath { get; set; }
	[Export] public NodePath HpPath { get; set; }
	[Export] public NodePath RingPath { get; set; }
	[Export] public NodePath PopupAnchorPath { get; set; } // small Control above head

	private TextureRect _sprite;
	private HPBar _hp;
	private Control _ring;
	private Control _popupAnchor;

	// Signals (keep old API)
	[Signal] public delegate void ClickedEventHandler(Enemy who);
	[Signal] public delegate void DamagedEventHandler(int amount);
	[Signal] public delegate void HealedEventHandler(int amount);
	[Signal] public delegate void DiedEventHandler();

	// IDamageable
	public int MaxHP { get; private set; } = 10;
	public int HP    { get; private set; } = 10;
	public bool Alive => HP > 0;

	public override void _Ready()
	{
		_sprite       = GetNodeOrNull<TextureRect>(SpritePath);
		_hp           = GetNodeOrNull<HPBar>(HpPath);
		_ring         = GetNodeOrNull<Control>(RingPath);
		_popupAnchor  = GetNodeOrNull<Control>(PopupAnchorPath);

		// Make the whole Enemy clickable; children should pass events up.
		MouseFilter = MouseFilterEnum.Stop;
		if (_sprite != null) _sprite.MouseFilter = MouseFilterEnum.Pass;
		if (_hp     != null) _hp.MouseFilter     = MouseFilterEnum.Pass;
		if (_ring   != null) _ring.MouseFilter   = MouseFilterEnum.Pass;

		// Hover highlight for ring (only when visible)
		MouseEntered += () => { if (_ring is TargetRing tr && tr.Visible) tr.SetHover(true); };
		MouseExited  += () => { if (_ring is TargetRing tr) tr.SetHover(false); };

		// Load from definition
		if (Def != null)
		{
			// NOTE: EnemyDef uses MaxHP (capital P)
			MaxHP = Mathf.Max(1, Def.MaxHP);
			HP    = MaxHP;
			if (_sprite != null && Def.Art != null)
				_sprite.Texture = Def.Art;
		}

		_hp?.Set(HP, MaxHP);
	}

	// Restore old click signal
	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			EmitSignal(SignalName.Clicked, this);
	}

	// Target ring visibility toggle used by CombatTargeting
	public void SetTargetable(bool on)
	{
		if (_ring != null) _ring.Visible = on;
		if (!on && _ring is TargetRing tr) tr.SetHover(false);
	}

	// IDamageable
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
		return new Vector2(r.Position.X + r.Size.X * 0.5f, r.Position.Y); // top-center fallback
	}
}
