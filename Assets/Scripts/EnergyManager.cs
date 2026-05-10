using Godot;

public partial class EnergyManager : Node
{
	[Export] public int MaxEnergy { get; set; } = 3;
	[Export] public int CardEnergyCost { get; set; } = 1;

	public int CurrentEnergy { get; private set; }

	[Signal] public delegate void EnergyChangedEventHandler(int current, int max);
	[Signal] public delegate void PlayerTurnStartedEventHandler();
	[Signal] public delegate void PlayerTurnEndedEventHandler();

	public override void _Ready()
	{
		StartPlayerTurn();
	}

	public bool CanSpend(int amount)
		=> amount <= 0 || CurrentEnergy >= amount;

	public bool TrySpend(int amount)
	{
		if (!CanSpend(amount)) return false;
		CurrentEnergy = Mathf.Max(0, CurrentEnergy - amount);
		EmitSignal(SignalName.EnergyChanged, CurrentEnergy, MaxEnergy);
		return true;
	}

	public void StartPlayerTurn()
	{
		CurrentEnergy = Mathf.Max(0, MaxEnergy);
		EmitSignal(SignalName.PlayerTurnStarted);
		EmitSignal(SignalName.EnergyChanged, CurrentEnergy, MaxEnergy);
	}

	public void EndPlayerTurn()
	{
		EmitSignal(SignalName.PlayerTurnEnded);
		StartPlayerTurn();
	}
}
