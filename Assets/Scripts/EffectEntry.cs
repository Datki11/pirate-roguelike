using Godot;

[GlobalClass]
public partial class EffectEntry : Resource
{
	[Export] public EffectDef Def { get; set; }           // pick the EffectDef asset here
	[Export] public int Amount { get; set; } = 0;         // number displayed / applied
	// add more params later (e.g., Duration, Stacks, Times)
}
