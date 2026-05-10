using Godot;

[GlobalClass]                 // shows up in “New Resource”
public partial class EffectDef : Resource
{
	[Export] public string Id { get; set; } = "attack";   // unique, e.g. "attack", "block", "bleed"
	[Export] public string DisplayName { get; set; } = "Attack";
	[Export] public Texture2D Icon { get; set; }          // 16x16 or 24x24 pixel icon (Filter Off)
	// Optional formatting knobs for UI/full card later:
	[Export(PropertyHint.MultilineText)] public string ShortFormat { get; set; } = "{ICON} {AMOUNT}";
	[Export(PropertyHint.MultilineText)] public string LongFormat  { get; set; } = "Deal {AMOUNT} damage.";
}
