using Godot;
using Godot.Collections;

public enum Rarity { Common, Uncommon, Rare, Legendary }

[GlobalClass] // so it shows up in "New Resource"
public partial class CardData : Resource
{
	[Export] public string Title { get; set; } = "";
	[Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
	[Export] public Texture2D Art { get; set; }
	[Export] public int ManaCost { get; set; } = 0;
	[Export] public Rarity Rarity { get; set; } = Rarity.Common;
	// Targeting without an enum? See TargetDef below. For now keep it simple:
	[Export] public Resource TargetDef { get; set; }      // Option B below (icon-based)
	// Or keep your old enum for logic and map to an icon elsewhere.

	[Export] public Array<EffectEntry> Effects { get; set; } = new(); // up to 3
}
