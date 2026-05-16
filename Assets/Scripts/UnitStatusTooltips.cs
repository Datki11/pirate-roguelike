using Godot;
using System.Collections.Generic;

public static class UnitStatusTooltips
{
	private static readonly Dictionary<string, string> IconPaths = new()
	{
		["hp"] = "res://Assets/Sprites/Icons/placeholder-icons/1-bit_Pixel_Icons/Sprites_Cropped/RPG_Stat_HP_Health_Heart.png",
		["block"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/shield_padded.png",
		["bleed"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/bleed_padded.png",
		["weak"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/weak_padded.png",
		["regen"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/regen_padded.png"
	};

	private static readonly Dictionary<string, Texture2D> Icons = new();

	public static List<TooltipEntry> Build(IReadOnlyDictionary<string, int> statuses, int block)
	{
		var entries = new List<TooltipEntry>();

		if (block > 0)
			entries.Add(new("Block", $"Prevents incoming attack damage. Current block: {block}. Block is cleared at the start of the player's turn.", GetIcon("block")));

		AddStatus(entries, statuses, "bleed", "Bleed", "At the end of this unit's turn, it takes damage equal to bleed.");
		AddStatus(entries, statuses, "weak", "Weak", "This unit deals 50% less damage. At the end of its turn, it loses 1 weak.");
		AddStatus(entries, statuses, "regen", "Regen", "At the end of this unit's turn, it gains HP equal to regen, then loses 1 regen.");
		return entries;
	}

	private static void AddStatus(List<TooltipEntry> entries, IReadOnlyDictionary<string, int> statuses, string id, string title, string text)
	{
		if (statuses == null || !statuses.TryGetValue(id, out int amount) || amount <= 0)
			return;

		entries.Add(new TooltipEntry(title, text, GetIcon(id)));
	}

	private static Texture2D GetIcon(string id)
	{
		if (Icons.TryGetValue(id, out Texture2D icon))
			return icon;

		if (!IconPaths.TryGetValue(id, out string path))
			return null;

		icon = ResourceLoader.Load<Texture2D>(path);
		Icons[id] = icon;
		return icon;
	}
}
