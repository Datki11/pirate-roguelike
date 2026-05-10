using Godot;

[GlobalClass]
public partial class TargetDef : Resource
{
	[Export] public string Id { get; set; } = "single";
	[Export] public string DisplayName { get; set; } = "Single";
	[Export] public Texture2D BadgeIcon { get; set; }     // 16x16 or 24x24 for the corner badge
}
