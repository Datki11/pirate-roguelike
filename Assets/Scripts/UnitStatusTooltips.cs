using Godot;
using System.Collections.Generic;

public static class UnitStatusTooltips
{
	private static readonly Dictionary<string, string> IconPaths = new()
	{
		["hp"] = "res://Assets/Sprites/Icons/placeholder-icons/1-bit_Pixel_Icons/Sprites_Cropped/RPG_Stat_HP_Health_Heart.png",
		["block"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/shield_padded.png",
		["protector"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/shield_padded.png",
		["bleed"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/bleed_padded.png",
		["weak"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/weak_padded.png",
		["regen"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/regen_padded.png",
		["strategist"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/book_04a.png",
		["expose"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/skull_01a.png",
		["thorns"] = "res://Assets/Sprites/Icons/placeholder-icons/1-bit_Pixel_Icons/Sprites_Cropped/RPG_Skill_Bear_Trap_Foothold_Spikes_Gripping_Cripple_2.png",
		["sickening_aura"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/potion_03g.png",
		["arm_hammer"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/shield_03a.png",
		["wall_of_flesh"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/shield_03e.png",
		["goop"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/potion_03f.png"
	};

	private static readonly Dictionary<string, Texture2D> Icons = new();

	public static List<TooltipEntry> Build(IReadOnlyDictionary<string, int> statuses, int block)
	{
		var entries = new List<TooltipEntry>();

		if (block > 0)
			entries.Add(new("Block", $"Prevents incoming attack damage. Current block: {block}. Block is cleared at the start of the player's turn.", GetIcon("block")));

		AddStatus(entries, statuses, "protector", "Protector", "Single-target harmful intents against this team are redirected to this unit. Loses 1 at the start of this unit's turn.");
		AddStatus(entries, statuses, "bleed", "Bleed", "At the start of this unit's turn, it takes damage equal to bleed.");
		AddStatus(entries, statuses, "weak", "Weak", "This unit deals 50% less damage. At the start of its turn, it loses 1 weak.");
		AddStatus(entries, statuses, "regen", "Regen", "At the start of this unit's turn, it gains HP equal to regen, then loses 1 regen.");
		AddStatus(entries, statuses, "strategist", "Strategist", "This unit can see and play this many extra cards from the top of its draw pile.");
		AddStatus(entries, statuses, "expose", "Expose", "This unit receives 10% more attack damage for each stack. Expose is halved after the enemy acts.");
		AddStatus(entries, statuses, "thorns", "Thorns", "When this unit receives unblocked attack damage, it deals thorns damage back to the attacker.");
		AddStatus(entries, statuses, "sickening_aura", "Sickening Aura", "At the start of this unit's turn, apply this much expose to all enemies.");
		AddStatus(entries, statuses, "arm_hammer", "Arm & Hammer", "At the end of each turn, this unit gains this much block.");
		AddStatus(entries, statuses, "wall_of_flesh", "Wall of Flesh", "This turn, incoming attack damage against allies is redirected to this unit.");
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
