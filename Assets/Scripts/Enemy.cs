using Godot;

public partial class Enemy : Control
{
	[Export] public EnemyDef Def;   // assign a .tres in Inspector
	[Export] public NodePath SpritePath { get; set; } = "Sprite";
	[Export] public NodePath HPPath     { get; set; } = "HP";
	[Export] public NodePath RingPath   { get; set; } = "Ring";

	[Signal] public delegate void ClickedEventHandler(Enemy who);
	public bool Alive => HP > 0;    // not exported
	
	public int HP = 0;

	private TextureRect _sprite;
	private HPBar _hp;
	private Control _ring;

	public override void _Ready()
	{
		_sprite = GetNode<TextureRect>(SpritePath);
		_hp     = GetNode<HPBar>(HPPath);
		_ring   = GetNode<Control>(RingPath);

		MouseFilter = MouseFilterEnum.Stop;

		// Load data from resource if provided
		if (Def != null)
		{
			HP    = Def.MaxHP;
			if (Def.Art != null) _sprite.Texture = Def.Art;
		}

		_hp.Set(HP, Def.MaxHP);
		_ring.Visible = false;

		Position = Position.Floor();  // integer pixels only
		
		MouseEntered += () => (_ring as TargetRing)?.SetHover(true);
		MouseExited  += () => (_ring as TargetRing)?.SetHover(false);
	}

	public void SetTargetable(bool on) => _ring.Visible = on;

	public void TakeDamage(int n)
	{
		HP = Mathf.Max(0, HP - Mathf.Max(0, n));
		_hp.Set(HP, Def.MaxHP);
		if (HP == 0) Die();
	}

	private void Die()
	{
		SetTargetable(false);
		Modulate = new Color(0.6f, 0.6f, 0.6f, 1f);
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			EmitSignal(SignalName.Clicked, this);
	}
}
