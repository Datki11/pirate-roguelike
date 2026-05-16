using Godot;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class PlayerUnit : Node2D, IDamageable
{
	[Export] public int StartingMaxHP { get; set; } = 30;
	[Export] public Vector2 PopupAnchorOffset { get; set; } = new(0, -96);
	[Export] public NodePath SpritePath { get; set; } = "Sprite";
	[Export] public NodePath HpPath { get; set; } = "HP";
	[Export] public NodePath DeckPath { get; set; } = "Deck";
	[ExportGroup("Standard Layout")]
	[Export] public bool AutoLayoutAttachments { get; set; } = true;
	[Export] public Vector2 HealthBarSize { get; set; } = new(70, 18);
	[Export] public float HealthBarGap { get; set; } = 4f;
	[Export] public Vector2 DeckGap { get; set; } = new(8, 4);
	[Export] public Vector2 DefaultDeckSize { get; set; } = new(112, 105);
	[Export] public Vector2 PopupAnchorGap { get; set; } = new(0, 16);
	[ExportGroup("Animation")]
	[Export(PropertyHint.Dir)] public string SpriteRootPath { get; set; } = "";
	[Export] public string IdleFolderName { get; set; } = "1-Idle";
	[Export] public string ActionFolderName { get; set; } = "7-Attack";
	[Export] public float IdleFrameSeconds { get; set; } = 0.08f;
	[Export] public float ActionFrameSeconds { get; set; } = 0.055f;
	[Export] public bool FaceRight { get; set; } = true;

	private CombatManager _combat;
	private Sprite2D _sprite;
	private HPBar _hpBar;
	private Deck _deck;
	private Tween _hurtTween;
	private List<Texture2D> _idleFrames = new();
	private List<Texture2D> _actionFrames = new();
	private List<Texture2D> _currentFrames = new();
	private float _frameTime;
	private int _frameIndex;
	private bool _playingAction;
	private bool _layoutSpriteRectValid;
	private Rect2 _layoutSpriteRect;

	public int MaxHP { get; private set; }
	public int HP { get; private set; }
	public bool Alive => HP > 0;

	[Signal] public delegate void DamagedEventHandler(int amount);
	[Signal] public delegate void HealedEventHandler(int amount);
	[Signal] public delegate void DiedEventHandler();

	public override void _Ready()
	{
		MaxHP = Mathf.Max(1, StartingMaxHP);
		HP = MaxHP;
		_sprite = GetNodeOrNull<Sprite2D>(SpritePath);
		_hpBar = GetNodeOrNull<HPBar>(HpPath);
		_deck = GetNodeOrNull<Deck>(DeckPath);
		if (_sprite != null)
		{
			_sprite.TextureFilter = TextureFilterEnum.Nearest;
			_sprite.FlipH = !FaceRight;
		}
		LoadAnimations();
		PlayIdle();
		ApplyStandardLayout();
		_hpBar?.Set(HP, MaxHP);
		if (Engine.IsEditorHint())
			return;

		if (_deck != null)
		{
			_deck.EnableInput = true;
			_deck.Side = Deck.DeckSide.Player;
		}

		_combat = GetTree().Root.FindChild("CombatManager", true, false) as CombatManager;
		_combat?.RegisterPlayer(this);
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
			return;

		if (_currentFrames.Count == 0 || _sprite == null) return;

		float frameSeconds = _playingAction ? ActionFrameSeconds : IdleFrameSeconds;
		_frameTime += (float)delta;
		while (_frameTime >= frameSeconds)
		{
			_frameTime -= frameSeconds;
			_frameIndex++;
			if (_frameIndex >= _currentFrames.Count)
			{
				if (_playingAction) PlayIdle();
				else _frameIndex = 0;
			}
			ApplyFrame();
		}
	}

	public override void _ExitTree()
	{
		if (Engine.IsEditorHint())
			return;

		_combat?.UnregisterPlayer(this);
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0 || !Alive) return;
		HP = Mathf.Max(0, HP - amount);
		_hpBar?.Set(HP, MaxHP);
		PlayHurtAnimation();
		EmitSignal(SignalName.Damaged, amount);
		if (!Alive) EmitSignal(SignalName.Died);
	}

	public void Heal(int amount)
	{
		if (amount <= 0 || !Alive) return;
		HP = Mathf.Min(MaxHP, HP + amount);
		_hpBar?.Set(HP, MaxHP);
		EmitSignal(SignalName.Healed, amount);
	}

	public Vector2 GetPopupAnchorGlobal()
	{
		if (AutoLayoutAttachments && TryGetSpriteRect(out Rect2 spriteRect))
		{
			var local = new Vector2(spriteRect.Position.X + spriteRect.Size.X * 0.5f + PopupAnchorGap.X, spriteRect.Position.Y - PopupAnchorGap.Y);
			return ToGlobal(local);
		}

		return GlobalPosition + PopupAnchorOffset;
	}

	public void PlayCardAnimation()
	{
		if (_actionFrames.Count == 0) return;
		_currentFrames = _actionFrames;
		_frameIndex = 0;
		_frameTime = 0;
		_playingAction = true;
		ApplyFrame();
	}

	private void PlayHurtAnimation()
	{
		if (_sprite == null) return;
		_hurtTween?.Kill();
		_sprite.Modulate = new Color(1f, 0.35f, 0.35f);
		_hurtTween = CreateTween();
		_hurtTween.TweenProperty(_sprite, "modulate", Colors.White, 0.18f);
	}

	private void LoadAnimations()
	{
		_idleFrames = LoadFrames(IdleFolderName);
		_actionFrames = LoadFrames(ActionFolderName);
	}

	private List<Texture2D> LoadFrames(string folderName)
	{
		var frames = new List<Texture2D>();
		if (string.IsNullOrWhiteSpace(SpriteRootPath) || string.IsNullOrWhiteSpace(folderName))
			return frames;

		string folderPath = $"{SpriteRootPath.TrimEnd('/')}/{folderName}";
		using var dir = DirAccess.Open(folderPath);
		if (dir == null)
		{
			GD.PushWarning($"PlayerUnit: missing animation folder {folderPath}");
			return frames;
		}

		var files = dir.GetFiles()
			.Where(f => f.EndsWith(".png"))
			.OrderBy(ParseFrameNumber);

		foreach (string file in files)
		{
			var texture = ResourceLoader.Load<Texture2D>($"{folderPath}/{file}");
			if (texture != null) frames.Add(texture);
		}

		return frames;
	}

	private int ParseFrameNumber(string file)
	{
		string stem = file.GetBaseName();
		return int.TryParse(stem, out int number) ? number : int.MaxValue;
	}

	private void PlayIdle()
	{
		_currentFrames = _idleFrames;
		_frameIndex = 0;
		_frameTime = 0;
		_playingAction = false;
		ApplyFrame();
	}

	private void ApplyFrame()
	{
		if (_sprite == null || _currentFrames.Count == 0) return;
		_frameIndex = Mathf.Clamp(_frameIndex, 0, _currentFrames.Count - 1);
		_sprite.Texture = _currentFrames[_frameIndex];
	}

	private void ApplyStandardLayout()
	{
		if (!AutoLayoutAttachments || !TryGetSpriteRect(out Rect2 spriteRect))
			return;

		if (_hpBar != null)
		{
			_hpBar.CustomMinimumSize = HealthBarSize;
			_hpBar.Size = HealthBarSize;
			_hpBar.Position = new Vector2(
				Mathf.Round(spriteRect.Position.X + (spriteRect.Size.X - HealthBarSize.X) * 0.5f),
				Mathf.Round(spriteRect.End.Y + HealthBarGap)
			);
		}

		if (_deck != null)
		{
			Vector2 deckSize = GetDeckSize();
			_deck.CustomMinimumSize = deckSize;
			_deck.Size = deckSize;
			Rect2 deckRect = _deck.GetLocalContentRect();
			float spriteCenterX = spriteRect.Position.X + spriteRect.Size.X * 0.5f;
			float x = spriteCenterX;
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

		return SpriteBounds.TryGetSprite2DRect(_sprite, out rect);
	}

	private bool TryBuildStableSpriteRect(out Rect2 rect)
	{
		rect = default;
		bool hasRect = false;
		List<Texture2D> layoutFrames = _idleFrames.Count > 0 ? _idleFrames : _currentFrames;
		foreach (Texture2D frame in layoutFrames)
		{
			if (SpriteBounds.TryGetSprite2DRect(frame, _sprite.Position, _sprite.Offset, _sprite.Scale, _sprite.Centered, _sprite.FlipH, out Rect2 frameRect))
				MergeSpriteRect(frameRect, ref rect, ref hasRect);
		}

		if (hasRect)
			return true;

		return _sprite.Texture != null && SpriteBounds.TryGetSprite2DRect(_sprite, out rect);
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
}
