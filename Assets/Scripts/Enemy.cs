using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[Tool]
public partial class Enemy : Control, IDamageable
{
	[Export] public Resource Def { get; set; }

	[Export] public NodePath SpritePath { get; set; } = "Sprite";
	[Export] public NodePath HpPath     { get; set; } = "HP";
	[Export] public NodePath StatusPath { get; set; } = "StatusBar";
	[Export] public NodePath RingPath   { get; set; } = "Ring";
	[Export] public NodePath PopupAnchorPath { get; set; } = "Damage Popup Anchor";
	[ExportGroup("Standard Layout")]
	[Export] public bool AutoLayoutAttachments { get; set; } = true;
	[Export] public Vector2 HealthBarSize { get; set; } = new(88, 20);
	[Export] public float HealthBarGap { get; set; } = 4f;
	[Export] public Vector2 StatusBarSize { get; set; } = new(96, 18);
	[Export] public float StatusBarGap { get; set; } = 1f;
	[Export] public Vector2 DeckGap { get; set; } = new(8, 4);
	[Export] public Vector2 DefaultDeckSize { get; set; } = new(112, 105);
	[Export] public Vector2 PopupAnchorGap { get; set; } = new(0, 16);
	[Export] public float TargetRingWidthMultiplier { get; set; } = 1.8f;
	[Export] public float TargetRingMinimumWidth { get; set; } = 42f;
	[Export] public float TargetRingVerticalOffset { get; set; } = -2f;

	// Deck + combat
	[Export] public NodePath DeckPath { get; set; } = "Deck";
	[Export] public Resource DeckListOverride { get; set; }
	[Export] public NodePath CombatPath { get; set; }
	[Export] public float ThinkDelaySec { get; set; } = 0.6f;

	[ExportGroup("Legacy Sprite Sheet")]
	[Export] public Texture2D SpriteSheet { get; set; }
	[Export] public Vector2I FrameSize { get; set; } = new(150, 150);
	[Export] public int IdleFrameCount { get; set; } = 3;

	[ExportGroup("Animation Sheets")]
	[Export] public Texture2D IdleSpriteSheet { get; set; }
	[Export] public Vector2I IdleFrameSize { get; set; } = new(150, 150);
	[Export] public int IdleSpriteFrameCount { get; set; }
	[Export] public Texture2D AttackSpriteSheet { get; set; }
	[Export] public Vector2I AttackFrameSize { get; set; } = new(150, 150);
	[Export] public int AttackFrameCount { get; set; }
	[Export] public Texture2D HitSpriteSheet { get; set; }
	[Export] public Vector2I HitFrameSize { get; set; } = new(150, 150);
	[Export] public int HitFrameCount { get; set; }

	[Export] public float IdleFrameSeconds { get; set; } = 0.12f;
	[Export] public float ActionFrameSeconds { get; set; } = 0.07f;
	[Export] public float TurnThinkDelaySec { get; set; } = 0.25f;
	[Export] public float TurnImpactDelaySec { get; set; } = 0.35f;
	[Export] public float TurnRecoveryDelaySec { get; set; } = 0.25f;
	[Export] public float DeathFadeDelaySec { get; set; } = 0.35f;
	[Export] public float DeathFadeDurationSec { get; set; } = 0.28f;
	[Export] public bool FaceLeft { get; set; } = true;

	private TextureRect _sprite;
	private HPBar _hp;
	private StatusBar _statusBar;
	private Control _hudTooltipArea;
	private TooltipDisplay _hudTooltip;
	private Control _ring;
	private TargetRing _ringTR;     // <— cached cast
	private Control _popupAnchor;

	private Deck _deck;
	private CombatManager _combat;
	private enum EnemyAnimation { Idle, Attack, Hit }
	private readonly List<AtlasTexture> _idleFrames = new();
	private readonly List<AtlasTexture> _attackFrames = new();
	private readonly List<AtlasTexture> _hitFrames = new();
	private readonly List<AtlasTexture> _legacyFrames = new();
	private List<AtlasTexture> _currentFrames;
	private float _frameTime;
	private int _frameIndex;
	private EnemyAnimation _activeAnimation = EnemyAnimation.Idle;
	private bool _layoutSpriteRectValid;
	private Rect2 _layoutSpriteRect;
	private bool _deathPresentationRunning;
	private readonly Dictionary<string, int> _statuses = new();

	// --- Signals (ADD THIS BACK) ---
	[Signal] public delegate void ClickedEventHandler(Enemy who);

	// IDamageable
	public int MaxHP { get; private set; } = 40;
	public int HP    { get; private set; } = 40;
	public int Block { get; private set; }
	public bool Alive => HP > 0;

	[Signal] public delegate void DamagedEventHandler(int amount);
	[Signal] public delegate void HealedEventHandler(int amount);
	[Signal] public delegate void DiedEventHandler();

	public override void _Ready()
	{
		_sprite      = GetNodeOrNull<TextureRect>(SpritePath);
		_hp          = GetNodeOrNull<HPBar>(HpPath);
		_statusBar   = GetNodeOrNull<StatusBar>(StatusPath);
		_ring        = GetNodeOrNull<Control>(RingPath);
		_ringTR      = _ring as TargetRing;           // <— keep a typed ref
		_popupAnchor = GetNodeOrNull<Control>(PopupAnchorPath);
		_deck        = GetNodeOrNull<Deck>(DeckPath);
		BuildAnimationFrames();

		// We want clicks on the whole enemy rect
		MouseFilter = MouseFilterEnum.Stop;
		if (_sprite != null) _sprite.MouseFilter = MouseFilterEnum.Pass;
		if (_hp     != null) _hp.MouseFilter     = MouseFilterEnum.Pass;
		if (_statusBar != null) _statusBar.MouseFilter = MouseFilterEnum.Ignore;
		if (_ring   != null) _ring.MouseFilter   = MouseFilterEnum.Pass;
		
		// hook hover
		MouseEntered += OnMouseEntered;
		MouseExited  += OnMouseExited;

		// ------ NAME ALIGNMENT: EnemyDef.MaxHP ------
		if (Def is EnemyDef enemyDef)
		{
			MaxHP = Mathf.Max(1, enemyDef.MaxHP);           // << use MaxHP (capital HP)
			HP    = MaxHP;
			if (!HasAnimationFrames() && enemyDef.Art != null && _sprite != null) _sprite.Texture = enemyDef.Art;
		}
		EnsureStatusBar();
		EnsureHudTooltip();
		ApplyFrame();
		ApplyStandardLayout();
		_hp?.Set(HP, MaxHP);
		RefreshStatusBar();
		if (Engine.IsEditorHint())
			return;

		_combat = GetNodeOrNull<CombatManager>(CombatPath)
				  ?? GetTree().Root.FindChild("CombatManager", true, false) as CombatManager;

		if (_deck != null)
		{
			if (DeckListOverride != null)
				_deck.DeckList = DeckListOverride;
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

		if (_currentFrames == null || _currentFrames.Count == 0 || _sprite == null) return;

		float frameSeconds = _activeAnimation == EnemyAnimation.Idle ? IdleFrameSeconds : ActionFrameSeconds;
		_frameTime += (float)delta;
		while (_frameTime >= frameSeconds)
		{
			_frameTime -= frameSeconds;
			_frameIndex++;
			if (_activeAnimation != EnemyAnimation.Idle)
			{
				if (_frameIndex >= _currentFrames.Count)
					PlayAnimation(EnemyAnimation.Idle);
			}
			else if (_frameIndex >= _currentFrames.Count)
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

	public async Task PlayTopCardsAsync(int count)
	{
		if (!Alive || _deck == null || _combat == null || count <= 0) return;

		for (int i = 0; i < count; i++)
		{
			if (!Alive || _deck.Peek() == null)
				_deck.EnsureTop();

			var card = _deck.Peek();
			if (card == null)
				return;
			if (card.Effects != null && card.Effects.Any(e => e?.Def?.Id == "play_top_cards"))
			{
				await _deck.AdvanceTopToDiscardWithPresentation(card);
				continue;
			}

			PlayCardAnimation();
			await ToSignal(GetTree().CreateTimer(TurnImpactDelaySec), "timeout");
			await _combat.PlayCardAuto(_deck, card, Deck.DeckSide.Enemy, this);
			await ToSignal(GetTree().CreateTimer(TurnRecoveryDelaySec), "timeout");
		}
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0 || !Alive) return;
		HP = Mathf.Max(0, HP - amount);
		_hp?.Set(HP, MaxHP);
		RefreshStatusBar();
		EmitSignal(SignalName.Damaged, amount);
		PlayHitAnimation();
		if (!Alive)
		{
			SetTargetable(false);
			BeginDeathPresentation();
			EmitSignal(SignalName.Died);
		}
	}

	public int TakeAttackDamage(int amount)
	{
		if (amount <= 0 || !Alive) return 0;
		int blocked = Mathf.Min(Block, amount);
		Block -= blocked;
		int hpDamage = amount - blocked;
		if (hpDamage > 0)
			TakeDamage(hpDamage);
		RefreshStatusBar();
		return hpDamage;
	}

	public void Heal(int amount)
	{
		if (amount <= 0 || !Alive) return;
		HP = Mathf.Min(MaxHP, HP + amount);
		_hp?.Set(HP, MaxHP);
		RefreshStatusBar();
		EmitSignal(SignalName.Healed, amount);
	}

	public void GainBlock(int amount)
	{
		if (amount <= 0 || !Alive) return;
		Block += amount;
		RefreshStatusBar();
	}

	public void ClearBlock()
	{
		if (Block == 0) return;
		Block = 0;
		RefreshStatusBar();
	}

	public void ApplyStatus(string id, int amount)
	{
		if (string.IsNullOrWhiteSpace(id) || amount <= 0 || !Alive) return;
		_statuses[id] = GetStatusAmount(id) + amount;
		RefreshStatusBar();
	}

	public void ReduceStatus(string id, int amount)
	{
		if (string.IsNullOrWhiteSpace(id) || amount <= 0) return;
		int next = GetStatusAmount(id) - amount;
		if (next > 0) _statuses[id] = next;
		else _statuses.Remove(id);
		RefreshStatusBar();
	}

	public int GetStatusAmount(string id)
		=> !string.IsNullOrWhiteSpace(id) && _statuses.TryGetValue(id, out int amount) ? amount : 0;

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

	public void SetDeckTargetingDimmed(bool dimmed)
	{
		_deck?.SetTargetingDimmed(dimmed);
	}

	public void SetDeckTurnDimmed(bool dimmed)
	{
		_deck?.SetTurnDimmed(dimmed);
	}

	public void SetDeckBaseDrawPriority(int priority)
	{
		_deck?.SetBaseDrawPriority(priority);
	}

	public void SetDeckTooltipSuppressed(bool suppressed)
	{
		_deck?.SetTopCardTooltipSuppressed(suppressed);
	}

	public Vector2 GetDeckStackAnchorGlobal()
	{
		if (_deck != null)
		{
			Rect2 deckRect = _deck.GetGlobalRect();
			return deckRect.Position + deckRect.Size * 0.5f;
		}

		Rect2 rect = GetGlobalRect();
		return rect.Position + rect.Size * 0.5f;
	}

	public Rect2 GetTargetingCanvasRect()
	{
		Rect2 rect = GetControlCanvasRect(this);
		rect = MergeVisibleControlCanvasRect(rect, _sprite);
		rect = MergeVisibleControlCanvasRect(rect, _hp);
		rect = MergeVisibleControlCanvasRect(rect, _statusBar);
		if (_ring != null && _ring.Visible)
			rect = rect.Merge(GetControlCanvasRect(_ring));

		return rect.Grow(6f);
	}

	public Vector2 GetTargetingAnchorCanvas()
	{
		var rect = GetTargetingCanvasRect();
		return rect.Position + rect.Size * 0.5f;
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
		PlayAnimation(EnemyAnimation.Attack);
	}

	public void PlayHitAnimation()
	{
		PlayAnimation(EnemyAnimation.Hit);
	}

	private async void BeginDeathPresentation()
	{
		if (_deathPresentationRunning || !IsInsideTree())
			return;

		_deathPresentationRunning = true;
		MouseFilter = MouseFilterEnum.Ignore;
		if (_deck != null) _deck.Visible = false;
		if (_hp != null) _hp.Visible = false;
		if (_statusBar != null) _statusBar.Visible = false;
		if (_ring != null) _ring.Visible = false;

		if (DeathFadeDelaySec > 0f)
			await ToSignal(GetTree().CreateTimer(DeathFadeDelaySec), "timeout");

		if (!GodotObject.IsInstanceValid(this))
			return;

		var tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0f, Mathf.Max(0.01f, DeathFadeDurationSec));
		await ToSignal(tween, "finished");

		if (GodotObject.IsInstanceValid(this))
			Visible = false;
	}

	private void BuildAnimationFrames()
	{
		_idleFrames.Clear();
		_attackFrames.Clear();
		_hitFrames.Clear();
		_legacyFrames.Clear();

		_idleFrames.AddRange(BuildHorizontalFrames(IdleSpriteSheet, IdleFrameSize, IdleSpriteFrameCount));
		_attackFrames.AddRange(BuildHorizontalFrames(AttackSpriteSheet, AttackFrameSize, AttackFrameCount));
		_hitFrames.AddRange(BuildHorizontalFrames(HitSpriteSheet, HitFrameSize, HitFrameCount));

		_legacyFrames.AddRange(BuildHorizontalFrames(SpriteSheet, FrameSize, 0));
		if (_idleFrames.Count == 0 && _legacyFrames.Count > 0)
		{
			int idleCount = Mathf.Clamp(IdleFrameCount, 1, _legacyFrames.Count);
			for (int i = 0; i < idleCount; i++)
				_idleFrames.Add(_legacyFrames[i]);
		}
		if (_attackFrames.Count == 0 && _legacyFrames.Count > 0)
			_attackFrames.AddRange(_legacyFrames);

		PlayAnimation(EnemyAnimation.Idle);
	}

	private List<AtlasTexture> BuildHorizontalFrames(Texture2D sheet, Vector2I frameSize, int explicitFrameCount)
	{
		var frames = new List<AtlasTexture>();
		if (sheet == null || frameSize.X <= 0 || frameSize.Y <= 0) return frames;

		int availableFrames = Mathf.Max(1, sheet.GetWidth() / frameSize.X);
		int frameCount = explicitFrameCount > 0 ? Mathf.Min(explicitFrameCount, availableFrames) : availableFrames;
		for (int i = 0; i < frameCount; i++)
		{
			var atlas = new AtlasTexture
			{
				Atlas = sheet,
				Region = new Rect2(i * frameSize.X, 0, frameSize.X, frameSize.Y)
			};
			frames.Add(atlas);
		}

		return frames;
	}

	private bool HasAnimationFrames()
	{
		return _idleFrames.Count > 0 || _attackFrames.Count > 0 || _hitFrames.Count > 0 || _legacyFrames.Count > 0;
	}

	private void PlayAnimation(EnemyAnimation animation)
	{
		var frames = GetFrames(animation);
		if (frames.Count == 0 && animation != EnemyAnimation.Idle)
		{
			animation = EnemyAnimation.Idle;
			frames = GetFrames(animation);
		}
		if (frames.Count == 0)
			return;

		_activeAnimation = animation;
		_currentFrames = frames;
		_frameIndex = 0;
		_frameTime = 0;
		ApplyFrame();
	}

	private List<AtlasTexture> GetFrames(EnemyAnimation animation)
	{
		return animation switch
		{
			EnemyAnimation.Attack => _attackFrames,
			EnemyAnimation.Hit => _hitFrames,
			_ => _idleFrames
		};
	}

	private void ApplyFrame()
	{
		if (_sprite == null || _currentFrames == null || _currentFrames.Count == 0) return;
		_frameIndex = Mathf.Clamp(_frameIndex, 0, _currentFrames.Count - 1);
		_sprite.Texture = _currentFrames[_frameIndex];
		_sprite.FlipH = FaceLeft;
		Vector2 frameSize = _sprite.Texture?.GetSize() ?? IdleFrameSize;
		_sprite.CustomMinimumSize = frameSize;
		_sprite.Size = frameSize;
		ApplyStandardLayout();
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

		if (_statusBar != null)
		{
			_statusBar.CustomMinimumSize = StatusBarSize;
			_statusBar.Size = StatusBarSize;
			float hpX = _hp?.Position.X ?? Mathf.Round(spriteRect.Position.X + (spriteRect.Size.X - StatusBarSize.X) * 0.5f);
			float hpY = _hp?.Position.Y ?? Mathf.Round(spriteRect.End.Y + HealthBarGap);
			float inlineWidth = GetHpInlineWidth();
			float groupWidth = Mathf.Max(inlineWidth, StatusBarSize.X);
			float groupX = hpX + (inlineWidth - groupWidth) * 0.5f;
			_statusBar.Position = new Vector2(
				Mathf.Round(groupX + (groupWidth - StatusBarSize.X) * 0.5f),
				Mathf.Round(hpY + HealthBarSize.Y + StatusBarGap)
			);
		}

		if (_hudTooltipArea != null)
		{
			float hpX = _hp?.Position.X ?? Mathf.Round(spriteRect.Position.X + (spriteRect.Size.X - HealthBarSize.X) * 0.5f);
			float hpY = _hp?.Position.Y ?? Mathf.Round(spriteRect.End.Y + HealthBarGap);
			float inlineWidth = GetHpInlineWidth();
			float width = Mathf.Max(inlineWidth, StatusBarSize.X);
			float groupX = hpX + (inlineWidth - width) * 0.5f;
			float height = HealthBarSize.Y + StatusBarGap + StatusBarSize.Y;
			_hudTooltipArea.Position = new Vector2(Mathf.Round(groupX), Mathf.Round(hpY));
			_hudTooltipArea.CustomMinimumSize = new Vector2(width, height);
			_hudTooltipArea.Size = _hudTooltipArea.CustomMinimumSize;
			if (_hudTooltip != null)
				_hudTooltip.Position = new Vector2(Mathf.Round(width + 6f), 0);
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
			Rect2 deckRect = _deck.GetLocalContentRect();
			float spriteCenterX = spriteRect.Position.X + spriteRect.Size.X * 0.5f;
			float deckCenterX = deckRect.Position.X + deckRect.Size.X * 0.5f;
			float x = spriteCenterX - deckCenterX;
			float y = spriteRect.Position.Y - DeckGap.Y - deckRect.End.Y;
			_deck.Position = new Vector2(Mathf.Round(x), Mathf.Round(y));
		}
	}

	private bool TryGetSpriteRect(out Rect2 rect)
	{
		rect = default;
		if (_sprite == null)
			return false;

		if (_layoutSpriteRectValid)
		{
			rect = _layoutSpriteRect;
			return true;
		}

		if (TryBuildStableSpriteRect(out rect))
		{
			_layoutSpriteRect = rect;
			_layoutSpriteRectValid = true;
			return true;
		}

		return SpriteBounds.TryGetTextureRect(_sprite, out rect);
	}

	private bool TryBuildStableSpriteRect(out Rect2 rect)
	{
		rect = default;
		bool hasRect = false;

		IEnumerable<AtlasTexture> layoutFrames = _idleFrames.Count > 0 ? _idleFrames : _legacyFrames;
		foreach (AtlasTexture frame in layoutFrames)
		{
			if (frame == null)
				continue;

			Vector2 frameSize = frame.GetSize();
			if (SpriteBounds.TryGetTextureRect(frame, _sprite.Position, frameSize, _sprite.FlipH, out Rect2 frameRect))
				MergeSpriteRect(frameRect, ref rect, ref hasRect);
		}

		if (hasRect)
			return true;

		return _sprite.Texture != null && SpriteBounds.TryGetTextureRect(_sprite, out rect);
	}

	private void MergeSpriteRect(Rect2 next, ref Rect2 rect, ref bool hasRect)
	{
		if (!hasRect)
		{
			rect = next;
			hasRect = true;
			return;
		}

		rect = rect.Merge(next);
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

	private float GetHpInlineWidth()
	{
		if (_hp != null)
			return Mathf.Max(HealthBarSize.X, _hp.GetInlineContentWidth());

		return HealthBarSize.X;
	}

	private Rect2 MergeVisibleControlCanvasRect(Rect2 rect, Control control)
	{
		if (control == null || !control.Visible)
			return rect;

		return rect.Merge(GetControlCanvasRect(control));
	}

	private Rect2 GetControlCanvasRect(Control control)
	{
		Vector2 size = control.Size;
		if (size.X <= 0f || size.Y <= 0f)
			size = control.CustomMinimumSize;

		return new Rect2(control.GetGlobalTransformWithCanvas().Origin, size);
	}

	private void EnsureStatusBar()
	{
		if (_statusBar != null || Engine.IsEditorHint())
			return;

		_statusBar = new StatusBar { Name = "StatusBar" };
		AddChild(_statusBar);
	}

	private void RefreshStatusBar()
	{
		_hp?.SetBlock(Block);
		_statusBar?.SetStatuses(_statuses, Block);
		_hudTooltip?.SetEntries(UnitStatusTooltips.Build(_statuses, Block));
		ApplyStandardLayout();
	}

	private void EnsureHudTooltip()
	{
		if (_hudTooltipArea != null || Engine.IsEditorHint())
			return;

		_hudTooltipArea = new Control
		{
			Name = "HudTooltipArea",
			MouseFilter = MouseFilterEnum.Ignore
		};
		AddChild(_hudTooltipArea);

		_hudTooltip = new TooltipDisplay
		{
			Name = "HudTooltip",
			MouseFilter = MouseFilterEnum.Ignore,
			PixelFont = ResourceLoader.Load<FontFile>("res://Assets/Fonts/Minecraft.ttf"),
			FontSize = 16,
			BoldPixelFont = ResourceLoader.Load<FontFile>("res://Assets/Fonts/upheaval/upheavtt.ttf"),
			BoldFontSize = 20,
			PanelSize = new Vector2(230, 54)
		};
		_hudTooltipArea.AddChild(_hudTooltip);
		_hudTooltip.SetHoverSource(_hudTooltipArea);
	}

}
