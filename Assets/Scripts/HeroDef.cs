using Godot;

[GlobalClass]
public partial class HeroDef : Resource
{
	[Export] public string Id = "";
	[Export] public string HeroName = "";
	[Export] public string HeroRole = "";
	[Export(PropertyHint.MultilineText)] public string HeroDescription = "";
	[Export] public int StartingMaxHP = 30;
	[Export] public Resource Deck;
	[Export(PropertyHint.Dir)] public string SpriteRootPath = "";
	[Export] public string IdleFolderName = "1-Idle";
	[Export] public string ActionFolderName = "7-Attack";
	[Export] public string DeadGroundFolderName = "9-Dead Ground";
	[Export] public bool FaceRight = true;
}
