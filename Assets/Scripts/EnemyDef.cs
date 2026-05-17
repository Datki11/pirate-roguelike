using Godot;

[GlobalClass]
public partial class EnemyDef : Resource
{
	[Export] public string Id = "bat";
	[Export] public string Name = "Bat";
	[Export] public Texture2D Art;
	[Export] public int MaxHP = 30;

	[Export] public Resource Deck;

	[ExportGroup("Legacy Sprite Sheet")]
	[Export] public Texture2D SpriteSheet;
	[Export] public Vector2I FrameSize = new(150, 150);
	[Export] public int IdleFrameCount = 3;

	[ExportGroup("Animation Sheets")]
	[Export] public Texture2D IdleSpriteSheet;
	[Export] public Vector2I IdleFrameSize = new(150, 150);
	[Export] public int IdleSpriteFrameCount;
	[Export] public Texture2D AttackSpriteSheet;
	[Export] public Vector2I AttackFrameSize = new(150, 150);
	[Export] public int AttackFrameCount;
	[Export] public Texture2D HitSpriteSheet;
	[Export] public Vector2I HitFrameSize = new(150, 150);
	[Export] public int HitFrameCount;

	[ExportGroup("Presentation")]
	[Export] public float IdleFrameSeconds = 0.12f;
	[Export] public float ActionFrameSeconds = 0.07f;
	[Export] public bool FaceLeft = true;

	[Export] public Resource Ai;
}
