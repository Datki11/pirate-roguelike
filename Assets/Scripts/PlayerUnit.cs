using Godot;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class PlayerUnit : Node2D, IDamageable
{
	[Export] public int StartingMaxHP { get; set; } = 30;
	[Export] public Vector2 PopupAnchorOffset { get; set; } = new(0, -96);
	[Export] public NodePath SpritePath { get; set; }
	[Export] public NodePath HpPath { get; set; }
	[Export(PropertyHint.Dir)] public string SpriteRootPath { get; set; } = "";
	[Export] public string IdleFolderName { get; set; } = "1-Idle";
	[Export] public string ActionFolderName { get; set; } = "7-Attack";
	[Export] public float IdleFrameSeconds { get; set; } = 0.08f;
	[Export] public float ActionFrameSeconds { get; set; } = 0.055f;
	[Export] public bool FaceRight { get; set; } = true;

	private CombatManager _combat;
	private Sprite2D _sprite;
	private HPBar _hpBar;
	private Tween _hurtTween;
	private List<Texture2D> _idleFrames = new();
	private List<Texture2D> _actionFrames = new();
	private List<Texture2D> _currentFrames = new();
	private float _frameTime;
	private int _frameIndex;
	private bool _playingAction;

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
		if (_sprite != null)
		{
			_sprite.TextureFilter = TextureFilterEnum.Nearest;
			_sprite.FlipH = !FaceRight;
		}
		LoadAnimations();
		PlayIdle();
		_hpBar?.Set(HP, MaxHP);
		if (Engine.IsEditorHint())
			return;

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
		=> GlobalPosition + PopupAnchorOffset;

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
}
