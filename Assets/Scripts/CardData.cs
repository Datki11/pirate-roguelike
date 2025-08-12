using Godot;

public enum Rarity { Common, Uncommon, Rare, Legendary }

[GlobalClass] // so it shows up in "New Resource"
public partial class CardData : Resource
{
	[Export] public string Title { get; set; } = "";
	[Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
	[Export] public Texture2D Art { get; set; }
	[Export] public int ManaCost { get; set; } = 0;
	[Export] public Rarity Rarity { get; set; } = Rarity.Common;
}
