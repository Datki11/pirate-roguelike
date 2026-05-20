using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RunState : Node
{
	private const string SavePath = "user://run.cfg";
	private const string HeroResourceDir = "res://Assets/Resources/Heroes";
	private const string BattlefieldPath = "res://battlefield.tscn";
	private const string MapPath = "res://Assets/Scenes/RunMap.tscn";
	private const string MainMenuPath = "res://Assets/Scenes/MainMenu.tscn";
	private const string RecruitPath = "res://Assets/Scenes/RecruitReward.tscn";

	public sealed class CrewMember
	{
		public string HeroId = "";
		public string HeroPath = "";
		public string DeckPath = "";
		public int MaxHP;
		public int HP;
	}

	public sealed class EncounterDef
	{
		public string Id = "";
		public string Difficulty = "";
		public string[] EnemyPaths = Array.Empty<string>();
	}

	public sealed class CompletedLevel
	{
		public int Floor;
		public string EncounterId = "";
		public string Difficulty = "";
	}

	public bool HasActiveRun { get; private set; }
	public int CurrentFloor { get; private set; } = 1;
	public string CurrentEncounterId { get; private set; } = "";
	public string CurrentEncounterDifficulty { get; private set; } = "";
	public string[] CurrentEncounterEnemyPaths { get; private set; } = Array.Empty<string>();
	public bool RecruitRewardPending { get; private set; }
	public List<string> RecruitOfferHeroIds { get; private set; } = new();
	public List<CrewMember> Crew { get; private set; } = new();
	public List<CompletedLevel> CompletedLevels { get; private set; } = new();

	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		_rng.Randomize();
		LoadRun();
	}

	public void StartNewRun()
	{
		HasActiveRun = true;
		CurrentFloor = 1;
		CurrentEncounterId = "";
		CurrentEncounterDifficulty = "";
		CurrentEncounterEnemyPaths = Array.Empty<string>();
		RecruitRewardPending = false;
		RecruitOfferHeroIds.Clear();
		CompletedLevels.Clear();
		Crew.Clear();

		HeroDef captain = LoadHero("captain");
		if (captain != null)
			Crew.Add(CreateCrewMember(captain));

		SaveRun();
	}

	public void EndRun()
	{
		HasActiveRun = false;
		CurrentFloor = 1;
		CurrentEncounterId = "";
		CurrentEncounterDifficulty = "";
		CurrentEncounterEnemyPaths = Array.Empty<string>();
		RecruitRewardPending = false;
		RecruitOfferHeroIds.Clear();
		Crew.Clear();
		CompletedLevels.Clear();
		if (FileAccess.FileExists(SavePath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
	}

	public void StartNextCombat()
	{
		if (!HasActiveRun)
			StartNewRun();

		EncounterDef encounter = PickEncounter();
		CurrentEncounterId = encounter.Id;
		CurrentEncounterDifficulty = encounter.Difficulty;
		CurrentEncounterEnemyPaths = encounter.EnemyPaths;
		SaveRun();
		GetTree().ChangeSceneToFile(BattlefieldPath);
	}

	public void CompleteCombat(IEnumerable<PlayerUnit> survivingPlayers)
	{
		if (!HasActiveRun || string.IsNullOrWhiteSpace(CurrentEncounterId))
			return;

		foreach (PlayerUnit player in survivingPlayers ?? Enumerable.Empty<PlayerUnit>())
		{
			string heroId = GetHeroIdForName(player.GetHeroName());
			CrewMember member = Crew.FirstOrDefault(c => c.HeroId == heroId);
			if (member == null)
				continue;

			member.MaxHP = Mathf.Max(1, player.MaxHP);
			member.HP = Mathf.Clamp(player.HP, 0, member.MaxHP);
			member.DeckPath = player.GetHeroDeckResourcePath();
		}

		CompletedLevels.Add(new CompletedLevel
		{
			Floor = CurrentFloor,
			EncounterId = CurrentEncounterId,
			Difficulty = CurrentEncounterDifficulty
		});

		bool shouldOfferRecruit = CurrentFloor == 1 && Crew.Count == 1;
		CurrentFloor++;
		CurrentEncounterId = "";
		CurrentEncounterDifficulty = "";
		CurrentEncounterEnemyPaths = Array.Empty<string>();
		RecruitRewardPending = shouldOfferRecruit;
		RecruitOfferHeroIds = shouldOfferRecruit ? PickRecruitOffers() : new List<string>();
		SaveRun();
	}

	public void ChooseRecruit(string heroId)
	{
		if (!RecruitRewardPending)
			return;

		HeroDef hero = LoadHero(heroId);
		if (hero != null && Crew.All(c => c.HeroId != hero.Id))
			Crew.Add(CreateCrewMember(hero));

		RecruitRewardPending = false;
		RecruitOfferHeroIds.Clear();
		SaveRun();
	}

	public void GoToMap()
	{
		GetTree().ChangeSceneToFile(MapPath);
	}

	public void GoToRecruitReward()
	{
		GetTree().ChangeSceneToFile(RecruitPath);
	}

	public void GoToMainMenu()
	{
		GetTree().Paused = false;
		Engine.TimeScale = 1f;
		GetTree().ChangeSceneToFile(MainMenuPath);
	}

	public HeroDef LoadHero(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		foreach (HeroDef hero in LoadAllHeroes())
			if (string.Equals(hero.Id, id, StringComparison.OrdinalIgnoreCase))
				return hero;
		return null;
	}

	public List<HeroDef> LoadAllHeroes()
	{
		var heroes = new List<HeroDef>();
		using DirAccess dir = DirAccess.Open(HeroResourceDir);
		if (dir == null)
			return heroes;

		dir.ListDirBegin();
		for (string file = dir.GetNext(); !string.IsNullOrEmpty(file); file = dir.GetNext())
		{
			if (dir.CurrentIsDir() || !file.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
				continue;

			var hero = ResourceLoader.Load<HeroDef>($"{HeroResourceDir}/{file}");
			if (hero != null)
				heroes.Add(hero);
		}
		return heroes.OrderBy(h => h.HeroName ?? "", StringComparer.OrdinalIgnoreCase).ToList();
	}

	public CrewMember GetCrewMember(string heroId)
		=> Crew.FirstOrDefault(c => string.Equals(c.HeroId, heroId, StringComparison.OrdinalIgnoreCase));

	public List<HeroDef> GetRecruitOffers()
		=> RecruitOfferHeroIds.Select(LoadHero).Where(h => h != null).ToList();

	public void SaveRun()
	{
		var config = new ConfigFile();
		config.SetValue("run", "active", HasActiveRun);
		config.SetValue("run", "floor", CurrentFloor);
		config.SetValue("run", "current_encounter_id", CurrentEncounterId);
		config.SetValue("run", "current_encounter_difficulty", CurrentEncounterDifficulty);
		config.SetValue("run", "current_enemies", string.Join(",", CurrentEncounterEnemyPaths));
		config.SetValue("run", "recruit_pending", RecruitRewardPending);
		config.SetValue("run", "recruit_offers", string.Join(",", RecruitOfferHeroIds));

		config.SetValue("crew", "count", Crew.Count);
		for (int i = 0; i < Crew.Count; i++)
		{
			CrewMember member = Crew[i];
			string section = $"crew_{i}";
			config.SetValue(section, "hero_id", member.HeroId);
			config.SetValue(section, "hero_path", member.HeroPath);
			config.SetValue(section, "deck_path", member.DeckPath);
			config.SetValue(section, "max_hp", member.MaxHP);
			config.SetValue(section, "hp", member.HP);
		}

		config.SetValue("levels", "count", CompletedLevels.Count);
		for (int i = 0; i < CompletedLevels.Count; i++)
		{
			CompletedLevel level = CompletedLevels[i];
			string section = $"level_{i}";
			config.SetValue(section, "floor", level.Floor);
			config.SetValue(section, "encounter_id", level.EncounterId);
			config.SetValue(section, "difficulty", level.Difficulty);
		}

		config.Save(SavePath);
	}

	private void LoadRun()
	{
		var config = new ConfigFile();
		if (config.Load(SavePath) != Error.Ok)
			return;

		HasActiveRun = (bool)config.GetValue("run", "active", false);
		CurrentFloor = (int)config.GetValue("run", "floor", 1);
		CurrentEncounterId = (string)config.GetValue("run", "current_encounter_id", "");
		CurrentEncounterDifficulty = (string)config.GetValue("run", "current_encounter_difficulty", "");
		CurrentEncounterEnemyPaths = SplitList((string)config.GetValue("run", "current_enemies", "")).ToArray();
		RecruitRewardPending = (bool)config.GetValue("run", "recruit_pending", false);
		RecruitOfferHeroIds = SplitList((string)config.GetValue("run", "recruit_offers", "")).ToList();

		Crew.Clear();
		int crewCount = (int)config.GetValue("crew", "count", 0);
		for (int i = 0; i < crewCount; i++)
		{
			string section = $"crew_{i}";
			Crew.Add(new CrewMember
			{
				HeroId = (string)config.GetValue(section, "hero_id", ""),
				HeroPath = (string)config.GetValue(section, "hero_path", ""),
				DeckPath = (string)config.GetValue(section, "deck_path", ""),
				MaxHP = (int)config.GetValue(section, "max_hp", 1),
				HP = (int)config.GetValue(section, "hp", 1)
			});
		}

		CompletedLevels.Clear();
		int levelCount = (int)config.GetValue("levels", "count", 0);
		for (int i = 0; i < levelCount; i++)
		{
			string section = $"level_{i}";
			CompletedLevels.Add(new CompletedLevel
			{
				Floor = (int)config.GetValue(section, "floor", i + 1),
				EncounterId = (string)config.GetValue(section, "encounter_id", ""),
				Difficulty = (string)config.GetValue(section, "difficulty", "")
			});
		}
	}

	private CrewMember CreateCrewMember(HeroDef hero)
	{
		return new CrewMember
		{
			HeroId = hero.Id,
			HeroPath = hero.ResourcePath,
			DeckPath = hero.Deck?.ResourcePath ?? "",
			MaxHP = Mathf.Max(1, hero.StartingMaxHP),
			HP = Mathf.Max(1, hero.StartingMaxHP)
		};
	}

	private EncounterDef PickEncounter()
	{
		string difficulty = CurrentFloor <= 2 ? "Easy" : "Normal";
		List<EncounterDef> encounters = BuildEncounters().Where(e => e.Difficulty == difficulty).ToList();
		var used = CompletedLevels.Select(l => l.EncounterId).ToHashSet();
		List<EncounterDef> available = encounters.Where(e => !used.Contains(e.Id)).ToList();
		if (available.Count == 0)
			available = encounters;

		return available[(int)_rng.RandiRange(0, available.Count - 1)];
	}

	private List<EncounterDef> BuildEncounters()
	{
		const string frog = "res://Assets/Resources/Enemies/frog.tres";
		const string fireworm = "res://Assets/Resources/Enemies/fireworm.tres";
		const string golem = "res://Assets/Resources/Enemies/stone_golem.tres";
		return new List<EncounterDef>
		{
			new() { Id = "easy_frog", Difficulty = "Easy", EnemyPaths = new[] { frog } },
			new() { Id = "easy_fireworm", Difficulty = "Easy", EnemyPaths = new[] { fireworm } },
			new() { Id = "easy_stone_golem", Difficulty = "Easy", EnemyPaths = new[] { golem } },
			new() { Id = "normal_frog_fireworm", Difficulty = "Normal", EnemyPaths = new[] { frog, fireworm } },
			new() { Id = "normal_frog_stone_golem", Difficulty = "Normal", EnemyPaths = new[] { frog, golem } },
			new() { Id = "normal_fireworm_stone_golem", Difficulty = "Normal", EnemyPaths = new[] { fireworm, golem } },
			new() { Id = "normal_all_three", Difficulty = "Normal", EnemyPaths = new[] { frog, fireworm, golem } }
		};
	}

	private List<string> PickRecruitOffers()
	{
		List<string> candidates = LoadAllHeroes()
			.Select(h => h.Id)
			.Where(id => id != "captain" && Crew.All(c => c.HeroId != id))
			.ToList();

		var offers = new List<string>();
		while (candidates.Count > 0 && offers.Count < 2)
		{
			int index = (int)_rng.RandiRange(0, candidates.Count - 1);
			offers.Add(candidates[index]);
			candidates.RemoveAt(index);
		}
		return offers;
	}

	private string GetHeroIdForName(string heroName)
	{
		foreach (HeroDef hero in LoadAllHeroes())
			if (string.Equals(hero.HeroName, heroName, StringComparison.OrdinalIgnoreCase))
				return hero.Id;
		return "";
	}

	private static IEnumerable<string> SplitList(string value)
		=> (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
