using Godot;

[GlobalClass]
public partial class EnemyDef : Resource
{
	[Export] public string Id = "bat";
	[Export] public string Name = "Bat";
	[Export] public Texture2D Art;
	[Export] public int MaxHP = 30;

	// future-proof:
	[Export] public DeckList Deck;  // enemy’s card deck (optional today)
	[Export] public Resource Ai;    // stub for later
}
