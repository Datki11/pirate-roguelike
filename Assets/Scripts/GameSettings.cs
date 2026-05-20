using Godot;

public static class GameSettings
{
	private const string SettingsPath = "user://settings.cfg";
	private const string GameplaySection = "gameplay";
	private const string GameSpeedKey = "game_speed";
	public const float DefaultGameSpeed = 1f;

	public static float LoadGameSpeed()
	{
		var config = new ConfigFile();
		if (config.Load(SettingsPath) != Error.Ok)
			return DefaultGameSpeed;

		return (float)config.GetValue(GameplaySection, GameSpeedKey, DefaultGameSpeed);
	}

	public static void SaveGameSpeed(float speed)
	{
		var config = new ConfigFile();
		config.Load(SettingsPath);
		config.SetValue(GameplaySection, GameSpeedKey, speed);
		config.Save(SettingsPath);
	}
}
