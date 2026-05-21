using Godot;
using Godot.Collections;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class RunBattlefieldController : Node
{
	private const string PlayerUnitScenePath = "res://Assets/Components/player_unit.tscn";
	private bool _transitionHandled;

	private static readonly Vector2[] PlayerPositions =
	{
		new(-611, -59),
		new(-763, -128),
		new(-462, 50)
	};

	private static readonly Vector2[] EnemyPositions =
	{
		new(145, -70),
		new(-31, -100),
		new(299, -103)
	};

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ApplyRun();
	}

	private async Task ApplyRun()
	{
		RunState run = GetNode<RunState>("/root/RunState");
		if (!run.HasActiveRun)
			run.StartNewRun();

		ConfigurePlayers(run);
		ConfigureEnemies(run);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var combat = GetNodeOrNull<CombatManager>("CombatManager");
		if (combat != null)
		{
			combat.CombatWon += OnCombatWon;
			combat.CombatLost += OnCombatLost;
			combat.RefreshPlayers();
			combat.RefreshEnemies();
		}

		RebindTargeting();
	}

	private void ConfigurePlayers(RunState run)
	{
		var allPlayers = GetChildren().OfType<PlayerUnit>().ToList();
		foreach (PlayerUnit player in allPlayers)
		{
			string heroId = GetHeroIdForPlayer(player);
			if (run.GetCrewMember(heroId) == null)
			{
				player.Visible = false;
				player.ProcessMode = ProcessModeEnum.Disabled;
			}
		}

		PackedScene playerScene = ResourceLoader.Load<PackedScene>(PlayerUnitScenePath);
		int activeIndex = 0;
		foreach (RunState.CrewMember member in run.Crew)
		{
			HeroDef hero = run.LoadHero(member.HeroId);
			if (hero == null)
				continue;

			PlayerUnit player = allPlayers.FirstOrDefault(p => GetHeroIdForPlayer(p) == member.HeroId);
			if (player == null && playerScene != null)
			{
				player = playerScene.Instantiate<PlayerUnit>();
				player.Name = $"Crew_{member.HeroId}";
				player.ConfigureFromHeroDef(hero);
				AddChild(player);
				allPlayers.Add(player);
			}

			if (player == null)
				continue;

			player.Visible = true;
			player.ProcessMode = ProcessModeEnum.Inherit;
			player.ConfigureFromHeroDef(hero);
			player.SetDeckListOverride(run.CreateDeckListForMember(member));
			player.ApplyRunHealth(member.MaxHP, member.HP);
			player.Position = PlayerPositions[Mathf.Min(activeIndex, PlayerPositions.Length - 1)];
			activeIndex++;
		}
	}

	private void ConfigureEnemies(RunState run)
	{
		var enemiesRoot = GetNodeOrNull<Control>("Enemies");
		if (enemiesRoot == null)
			return;

		var currentEnemies = enemiesRoot.GetChildren().OfType<Enemy>().ToList();
		for (int i = 0; i < currentEnemies.Count; i++)
		{
			Enemy enemy = currentEnemies[i];
			if (i >= run.CurrentEncounterEnemyPaths.Length)
			{
				enemy.Visible = false;
				enemy.ProcessMode = ProcessModeEnum.Disabled;
				continue;
			}

			EnemyDef def = ResourceLoader.Load<EnemyDef>(run.CurrentEncounterEnemyPaths[i]);
			if (def == null)
			{
				enemy.Visible = false;
				enemy.ProcessMode = ProcessModeEnum.Disabled;
				continue;
			}

			enemy.Visible = true;
			enemy.ProcessMode = ProcessModeEnum.Inherit;
			enemy.ConfigureFromEnemyDef(def);
			enemy.Position = EnemyPositions[Mathf.Min(i, EnemyPositions.Length - 1)];
		}
	}

	private void RebindTargeting()
	{
		var targeting = GetNodeOrNull<CombatTargeting>("CombatTargeting");
		if (targeting == null)
			return;

		var deckPaths = new Array<NodePath>();
		foreach (PlayerUnit player in GetChildren().OfType<PlayerUnit>().Where(p => p.Visible))
			deckPaths.Add(targeting.GetPathTo(player.GetNode("Deck")));

		targeting.DeckPaths = deckPaths;
		targeting.RefreshBindings();
	}

	private void OnCombatWon()
	{
		if (_transitionHandled)
			return;
		_transitionHandled = true;

		RunState run = GetNode<RunState>("/root/RunState");
		run.CompleteCombat(GetChildren().OfType<PlayerUnit>().Where(p => p.Alive));
		if (run.CardRewardPending)
			run.GoToCardReward();
		else if (run.RecruitRewardPending)
			run.GoToRecruitReward();
		else
			run.GoToMap();
	}

	private void OnCombatLost()
	{
		if (_transitionHandled)
			return;
		_transitionHandled = true;

		RunState run = GetNode<RunState>("/root/RunState");
		run.EndRun();
		run.GoToMainMenu();
	}

	private string GetHeroIdForPlayer(PlayerUnit player)
	{
		string name = player.GetHeroName();
		if (string.Equals(name, "Commander", StringComparison.OrdinalIgnoreCase))
			return "captain";
		if (string.Equals(name, "Duelist", StringComparison.OrdinalIgnoreCase))
			return "duelist";
		if (string.Equals(name, "Brute", StringComparison.OrdinalIgnoreCase))
			return "brute";
		return name.ToLowerInvariant();
	}
}
