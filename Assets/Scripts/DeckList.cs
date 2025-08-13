// DeckList.cs
using Godot;
using Godot.Collections;

[GlobalClass]
public partial class DeckList : Resource
{
	[Export] public Array<CardData> Cards { get; set; } = new();
}
