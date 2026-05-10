using Godot;
using System.Collections.Generic;

public partial class SmallCard : BaseCardView
{
	private static readonly Color TriggerColor = new(0.85f, 0.53f, 0.0f);

	[Export] public NodePath TitlePath { get; set; }
	[Export] public NodePath ArtPath { get; set; }
	[Export] public NodePath BodyPath { get; set; }
	[Export] public NodePath BadgeIconPath { get; set; }
	[Export] public NodePath TooltipDisplayPath { get; set; }
	[Export] public int EffectIconSize { get; set; } = 12;

	[Export] public CardData Data { get; set; }

	private TextureRect _art, _badgeIcon;
	private Label _title;
	private RichTextLabel _body;
	private TooltipDisplay _tooltipDisplay;
	
	public override void SetData(CardData data)
	{
		Data = data;
		if (IsInsideTree()) Apply();
	}

	public override void _Ready()
	{
		_title = GetNode<Label>(TitlePath);
		_art = GetNode<TextureRect>(ArtPath);
		_badgeIcon = GetNode<TextureRect>(BadgeIconPath);
		_body = GetNode<RichTextLabel>(BodyPath);
		_tooltipDisplay = GetNode<TooltipDisplay>(TooltipDisplayPath);

		MouseFilter = MouseFilterEnum.Pass;
		_tooltipDisplay.SetHoverSource(this);
		ConfigurePixelFont(_title.GetThemeFont("font"));
		SetupBody(_body);
		if (Data != null) Apply();
	}

	private void ConfigurePixelFont(Font font)
	{
		if (font is FontFile fontFile)
		{
			fontFile.Antialiasing = TextServer.FontAntialiasing.None;
			fontFile.GenerateMipmaps = false;
			fontFile.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
		}
	}

	private void SetupBody(RichTextLabel r)
	{
		r.BbcodeEnabled = true;
		r.FitContent = false;
		r.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		r.ScrollActive = false;
		r.HorizontalAlignment = HorizontalAlignment.Center;
		r.VerticalAlignment = VerticalAlignment.Top;
		r.AddThemeConstantOverride("line_separation", 0);
		ConfigurePixelFont(r.GetThemeFont("normal_font"));
	}

	private void Apply()
	{
		_title.Text = Data.Title ?? "";
		_art.Texture = Data.Art;

		// Target badge (if you used TargetDef)
		if (Data.TargetDef is TargetDef t && t.BadgeIcon != null)
		{
			_badgeIcon.Texture = t.BadgeIcon;
			_badgeIcon.GetParent<CanvasItem>().Visible = true;
		}
		else
		{
			_badgeIcon.GetParent<CanvasItem>().Visible = false;
		}

		_body.Text = BuildBodyBBCode();
		_tooltipDisplay.SetEntries(BuildTooltipEntries());
	}

	private string BuildEffectBBCode(EffectEntry e)
	{
		int iconPx = Mathf.Max(1, EffectIconSize);

		string iconPath = e.Def.Icon?.ResourcePath ?? "";
		// Use EffectDef.ShortFormat later if you want text, colors, etc.
		return string.IsNullOrEmpty(iconPath)
			? e.Amount.ToString()
			: $"[center][img={iconPx}x{iconPx}]{iconPath}[/img] {e.Amount}[/center]";
	}

	private string BuildBodyBBCode()
	{
		var parts = new List<string>();
		if (Data.Effects != null)
		{
			foreach (var effect in Data.Effects)
				if (effect?.Def != null) parts.Add(BuildEffectBBCode(effect));
		}
		if (!string.IsNullOrWhiteSpace(Data.RulesText))
			parts.Add(Data.RulesText);
		if (Data.Trigger != CardTrigger.None && !string.IsNullOrWhiteSpace(Data.TriggerText))
			parts.Add($"[color=#d98600]{Data.Trigger}:[/color] {Data.TriggerText}");
		return string.Join("\n", parts);
	}

	private List<TooltipEntry> BuildTooltipEntries()
	{
		var entries = new List<TooltipEntry>();
		if (Data.TargetDef is TargetDef target)
			entries.Add(new TooltipEntry(target.DisplayName, GetTargetTooltip(target), target.BadgeIcon));

		if (Data.Effects != null)
		{
			foreach (var effect in Data.Effects)
				if (effect?.Def != null) entries.Add(new TooltipEntry(effect.Def.DisplayName, GetEffectTooltip(effect.Def), effect.Def.Icon));
		}

		if (Data.Trigger != CardTrigger.None)
			entries.Add(new TooltipEntry(Data.Trigger.ToString(), GetTriggerTooltip(Data.Trigger), null, TriggerColor));

		return entries;
	}

	private string GetEffectTooltip(EffectDef effect)
		=> effect.Id switch
		{
			"attack" => "Damage dealt to a target.",
			"block" => "Prevents incoming damage.",
			_ => string.IsNullOrWhiteSpace(effect.LongFormat) ? effect.DisplayName : effect.LongFormat
		};

	private string GetTargetTooltip(TargetDef target)
		=> target.Id switch
		{
			"single" => "Choose one enemy target.",
			"multiple" or "all" or "all_enemies" => "Affects all enemies.",
			"self" => "Targets this unit.",
			_ => target.DisplayName
		};

	private string GetTriggerTooltip(CardTrigger trigger)
		=> trigger switch
		{
			CardTrigger.Lethal => "Happens when this card kills a unit.",
			CardTrigger.Revenge => "Happens when this unit takes damage.",
			CardTrigger.Shuffle => "Happens when this unit shuffles its deck.",
			_ => ""
		};

}
