using Godot;

[GlobalClass]
public partial class TargetDef : Resource
{
	[Export] public string Id { get; set; } = "single";
	[Export] public string DisplayName { get; set; } = "Single";
	[Export] public Texture2D BadgeIcon { get; set; }     // 8x8 or 16x16 for the corner badge
}
