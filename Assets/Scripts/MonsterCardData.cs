using Godot;

[GlobalClass]
public partial class MonsterCardData : CardData
{
	public MonsterCardData()
	{
		Class = CardClass.Enemy;
	}

	public override bool CanAppearInCardRewards() => false;
}
