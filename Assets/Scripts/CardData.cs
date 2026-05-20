using Godot;
using Godot.Collections;

public enum CardClass { Basic, Common, Uncommon, Rare, Enemy, Status }
public enum CardTrigger { None, Lethal, Revenge, Shuffle, Pierce }

[GlobalClass] // so it shows up in "New Resource"
public partial class CardData : Resource
{
	[Export] public string Title { get; set; } = "";
	[Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
	[Export(PropertyHint.MultilineText)] public string RulesText { get; set; } = "";
	[Export] public Texture2D Art { get; set; }
	[Export] public int ManaCost { get; set; } = 0;
	[Export] public CardClass Class { get; set; } = CardClass.Common;
	// Targeting without an enum? See TargetDef below. For now keep it simple:
	[Export] public Resource TargetDef { get; set; }      // Option B below (icon-based)
	// Or keep your old enum for logic and map to an icon elsewhere.

	[Export] public Array<EffectEntry> Effects { get; set; } = new(); // up to 3

	[ExportGroup("Unit Card Triggers")]
	[Export] public CardTrigger Trigger { get; set; } = CardTrigger.None;
	[Export(PropertyHint.MultilineText)] public string TriggerText { get; set; } = "";
	[Export] public int LethalHealAmount { get; set; } = 0;
	[Export] public int PierceHealAmount { get; set; } = 0;
	[Export] public bool ReturnToDrawOnLethal { get; set; } = false;
	[Export] public bool PlayTopCardOnShuffle { get; set; } = false;
	[Export] public bool Exhaust { get; set; } = false;

	public virtual bool CanAppearInCardRewards()
		=> Class is CardClass.Common or CardClass.Uncommon or CardClass.Rare;
}
