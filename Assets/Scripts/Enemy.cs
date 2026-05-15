using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

[Tool]
public partial class Enemy : Control, IDamageable
{
	[Export] public Resource Def { get; set; }

	[Export] public NodePath SpritePath { get; set; } = "Sprite";
	[Export] public NodePath HpPath     { get; set; } = "HP";
	[Export] public NodePath RingPath   { get; set; } = "Ring";
	[Export] public NodePath PopupAnchorPath { get; set; } = "Damage Popup Anchor";
	[ExportGroup("Standard Layout")]
	[Export] public bool AutoLayoutAttachments { get; set; } = true;
	[Export] public Vector2 HealthBarSize { get; set; } = new(60, 18);
	[Export] public float HealthBarGap { get; set; } = -4f;
	[Export] public Vector2 DeckGap { get; set; } = new(8, 4);
	[Export] public Vector2 DefaultDeckSize { get; set; } = new(112, 105);
	[Export] public float DeckCenterCorrection { get; set; } = 34f;
	[Export] public Vector2 PopupAnchorGap { get; set; } = new(0, 16);
	[Export] public float TargetRingWidthMultiplier { get; set; } = 1.8f;
	[Export] public float TargetRingMinimumWidth { get; set; } = 42f;
	[Export] public float TargetRingVerticalOffset { get; set; } = -2f;

	// Deck + combat
	[Export] public NodePath DeckPath { get; set; } = "Deck";
	[Export] public NodePath CombatPath { get; set; }
	[Export] public float ThinkDelaySec { get; set; } = 0.6f;
	[Export] public Texture2D SpriteSheet { get; set; }
	[Export] public Vector2I FrameSize { get; set; } = new(150, 150);
	[Export] public int IdleFrameCount { get; set; } = 3;
	[Export] public float IdleFrameSeconds { get; set; } = 0.12f;
	[Export] public float ActionFrameSeconds { get; set; } = 0.07f;
	[Export] public float TurnThinkDelaySec { get; set; } = 0.25f;
	[Export] public float TurnImpactDelaySec { get; set; } = 0.35f;
	[Export] public float TurnRecoveryDelaySec { get; set; } = 0.25f;
	[Export] public bool FaceLeft { get; set; } = true;

	private TextureRect _sprite;
	private HPBar _hp;
	private Control _ring;
	private TargetRing _ringTR;     // <— cached cast
	private Control _popupAnchor;

	private Deck _deck;
	private CombatManager _combat;
	private readonly List<AtlasTexture> _sheetFrames = new();
	private float _frameTime;
	private int _frameIndex;
	private bool _playingAction;

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
		_deck        = GetNodeOrNull<Deck>(DeckPath);
		BuildSheetFrames();

		// We want clicks on the whole enemy rect
		MouseFilter = MouseFilterEnum.Stop;
		if (_sprite != null) _sprite.MouseFilter = MouseFilterEnum.Pass;
		if (_hp     != null) _hp.MouseFilter     = MouseFilterEnum.Pass;
		if (_ring   != null) _ring.MouseFilter   = MouseFilterEnum.Pass;
		
		// hook hover
		MouseEntered += OnMouseEntered;
		MouseExited  += OnMouseExited;

		// ------ NAME ALIGNMENT: EnemyDef.MaxHP ------
		if (Def is EnemyDef enemyDef)
		{
			MaxHP = Mathf.Max(1, enemyDef.MaxHP);           // << use MaxHP (capital HP)
			HP    = MaxHP;
			if (_sheetFrames.Count == 0 && enemyDef.Art != null && _sprite != null) _sprite.Texture = enemyDef.Art;
		}
		ApplyFrame();
		ApplyStandardLayout();
		_hp?.Set(HP, MaxHP);
		if (Engine.IsEditorHint())
			return;

		_combat = GetNodeOrNull<CombatManager>(CombatPath)
				  ?? GetTree().Root.FindChild("CombatManager", true, false) as CombatManager;

		if (_deck != null)
		{
			_deck.EnableInput = false;
			_deck.DiscardOnTopClick = false;
			_deck.Side = Deck.DeckSide.Enemy;
			_deck.PlayRequested += OnDeckPlayRequested;
			_deck.Shuffled += OnDeckShuffled;
		}

		_combat?.RegisterEnemy(this);
		SetTargetable(false);
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
			return;

		if (_sheetFrames.Count == 0 || _sprite == null) return;

		float frameSeconds = _playingAction ? ActionFrameSeconds : IdleFrameSeconds;
		_frameTime += (float)delta;
		while (_frameTime >= frameSeconds)
		{
			_frameTime -= frameSeconds;
			_frameIndex++;
			if (_playingAction)
			{
				if (_frameIndex >= _sheetFrames.Count)
				{
					_playingAction = false;
					_frameIndex = 0;
				}
			}
			else if (_frameIndex >= Mathf.Min(IdleFrameCount, _sheetFrames.Count))
			{
				_frameIndex = 0;
			}
			ApplyFrame();
		}
	}

	public override void _ExitTree()
	{
		if (Engine.IsEditorHint())
			return;

		if (_deck != null)
		{
			_deck.PlayRequested -= OnDeckPlayRequested;
			_deck.Shuffled -= OnDeckShuffled;
		}
		_combat?.UnregisterEnemy(this);
	}

	// Emit Clicked so CombatTargeting keeps working
	public override void _GuiInput(InputEvent e)
	{
		if (Deck.IsDrawPileModalOpen)
		{
			AcceptEvent();
			return;
		}

		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
			EmitSignal(SignalName.Clicked, this);
	}

	private async void OnDeckPlayRequested(Deck deck, CardData card)
	{
		if (!Alive || _combat == null || card == null) return;
		PlayCardAnimation();
		await _combat.PlayCardAuto(deck, card, Deck.DeckSide.Enemy, this);
	}

	private async void OnDeckShuffled(Deck deck, CardData newTop)
	{
		if (!Alive || _combat == null || deck == null || newTop == null || !newTop.PlayTopCardOnShuffle)
			return;

		PlayCardAnimation();
		await _combat.PlayCardAuto(deck, newTop, Deck.DeckSide.Enemy, this);
	}

	public async void PlayTurn()
		=> await PlayTurnAsync();

	public async Task PlayTurnAsync()
	{
		if (!Alive || _deck == null) return;
		if (_deck.Peek() == null) _deck.EnsureTop();
		var card = _deck.Peek();
		if (card == null || _combat == null) return;

		await ToSignal(GetTree().CreateTimer(TurnThinkDelaySec), "timeout");
		PlayCardAnimation();
		await ToSignal(GetTree().CreateTimer(TurnImpactDelaySec), "timeout");
		await _combat.PlayCardAuto(_deck, card, Deck.DeckSide.Enemy, this);
		await ToSignal(GetTree().CreateTimer(TurnRecoveryDelaySec), "timeout");
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
		if (AutoLayoutAttachments && TryGetSpriteRect(out Rect2 spriteRect))
		{
			var local = new Vector2(spriteRect.Position.X + spriteRect.Size.X * 0.5f + PopupAnchorGap.X, spriteRect.Position.Y - PopupAnchorGap.Y);
			return GetGlobalTransformWithCanvas() * local;
		}

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

	public void PlayCardAnimation()
	{
		if (_sheetFrames.Count == 0) return;
		_playingAction = true;
		_frameIndex = 0;
		_frameTime = 0;
		ApplyFrame();
	}

	private void BuildSheetFrames()
	{
		_sheetFrames.Clear();
		if (SpriteSheet == null || FrameSize.X <= 0 || FrameSize.Y <= 0) return;

		int frameCount = Mathf.Max(1, SpriteSheet.GetWidth() / FrameSize.X);
		for (int i = 0; i < frameCount; i++)
		{
			var atlas = new AtlasTexture
			{
				Atlas = SpriteSheet,
				Region = new Rect2(i * FrameSize.X, 0, FrameSize.X, FrameSize.Y)
			};
			_sheetFrames.Add(atlas);
		}
	}

	private void ApplyFrame()
	{
		if (_sprite == null || _sheetFrames.Count == 0) return;
		_frameIndex = Mathf.Clamp(_frameIndex, 0, _sheetFrames.Count - 1);
		_sprite.Texture = _sheetFrames[_frameIndex];
		_sprite.FlipH = !FaceLeft;
		Vector2 frameSize = _sprite.Texture?.GetSize() ?? FrameSize;
		_sprite.CustomMinimumSize = frameSize;
		_sprite.Size = frameSize;
	}

	private void ApplyStandardLayout()
	{
		if (!AutoLayoutAttachments || !TryGetSpriteRect(out Rect2 spriteRect))
			return;

		if (_hp != null)
		{
			_hp.CustomMinimumSize = HealthBarSize;
			_hp.Size = HealthBarSize;
			_hp.Position = new Vector2(
				Mathf.Round(spriteRect.Position.X + (spriteRect.Size.X - HealthBarSize.X) * 0.5f),
				Mathf.Round(spriteRect.End.Y + HealthBarGap)
			);
		}

		if (_ring != null)
		{
			float ringWidth = Mathf.Max(TargetRingMinimumWidth, spriteRect.Size.X * TargetRingWidthMultiplier);
			float ringHeight = Mathf.Max(18f, ringWidth * 0.62f);
			_ring.CustomMinimumSize = new Vector2(ringWidth, ringHeight);
			_ring.Size = new Vector2(ringWidth, ringHeight);
			_ring.Position = new Vector2(
				Mathf.Round(spriteRect.Position.X + (spriteRect.Size.X - ringWidth) * 0.5f),
				Mathf.Round(spriteRect.End.Y - ringHeight * 0.5f + TargetRingVerticalOffset)
			);
		}

		if (_deck != null)
		{
			Vector2 deckSize = GetDeckSize();
			_deck.CustomMinimumSize = deckSize;
			_deck.Size = deckSize;
			float x = spriteRect.Position.X + spriteRect.Size.X * 0.5f - DeckCenterCorrection;
			float y = spriteRect.Position.Y - deckSize.Y - DeckGap.Y;
			_deck.Position = new Vector2(Mathf.Round(x), Mathf.Round(y));
		}
	}

	private bool TryGetSpriteRect(out Rect2 rect)
	{
		rect = default;
		if (_sprite == null)
			return false;

		Vector2 size = _sprite.Size;
		if ((size.X <= 0 || size.Y <= 0) && _sprite.Texture != null)
			size = _sprite.Texture.GetSize();
		if (size.X <= 0 || size.Y <= 0)
			return false;

		rect = new Rect2(_sprite.Position, size);
		return true;
	}

	private Vector2 GetDeckSize()
	{
		if (_deck == null)
			return DefaultDeckSize;

		Vector2 size = _deck.Size;
		if (size.X <= 0 || size.Y <= 0)
			size = _deck.CustomMinimumSize;
		if (size.X <= 0 || size.Y <= 0)
			size = DefaultDeckSize;
		return size;
	}

}
