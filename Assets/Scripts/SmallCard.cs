using Godot;
using System.Collections.Generic;

public partial class SmallCard : BaseCardView
{
	private static readonly Color TriggerColor = new(0.85f, 0.53f, 0.0f);
	private const InlineAlignment EffectIconAlignment = InlineAlignment.BottomTo | InlineAlignment.ToBaseline;
	private static Texture2D _placeholderArt;

	[Export] public NodePath TitlePath { get; set; }
	[Export] public NodePath ArtPath { get; set; }
	[Export] public NodePath BodyPath { get; set; }
	[Export] public NodePath BadgeIconPath { get; set; }
	[Export] public NodePath TooltipDisplayPath { get; set; }
	[Export] public int EffectIconSize { get; set; } = 16;

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
		_art.TextureFilter = TextureFilterEnum.Nearest;
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
			fontFile.AllowSystemFallback = false;
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
		_art.Texture = Data.Art ?? GetPlaceholderArt();

		_badgeIcon.GetParent<CanvasItem>().Visible = false;

		RenderBody();
		_tooltipDisplay.SetEntries(BuildTooltipEntries());
	}

	private void RenderBody()
	{
		_body.Clear();

		bool hasContent = false;
		if (Data.Effects != null)
		{
			foreach (var effect in Data.Effects)
			{
				if (effect?.Def == null) continue;
				if (hasContent) _body.Newline();
				RenderEffectLine(effect);
				hasContent = true;
			}
		}

		if (!string.IsNullOrWhiteSpace(Data.RulesText))
		{
			if (hasContent) _body.Newline();
			_body.AppendText(Data.RulesText);
			hasContent = true;
		}

		if (Data.Trigger != CardTrigger.None && !string.IsNullOrWhiteSpace(Data.TriggerText))
		{
			if (hasContent) _body.Newline();
			_body.AppendText($"[color=#d98600]{Data.Trigger}:[/color] {Data.TriggerText}");
		}
	}

	private void RenderEffectLine(EffectEntry e)
	{
		int iconPx = Mathf.Max(1, EffectIconSize);

		string effectText = e.Def.Id switch
		{
			"attack" => "Deal",
			"block" => "Gain",
			_ => e.Def.DisplayName
		};

		_body.PushParagraph(HorizontalAlignment.Center);
		_body.AddText($"{effectText} ");
		if (e.Def.Icon != null)
		{
			_body.AddImage(e.Def.Icon, iconPx, iconPx, null, EffectIconAlignment, null);
			_body.AddText(" ");
		}
		_body.AddText($"{e.Amount}{BuildTargetSuffix(e.Def)}");
		_body.Pop();
	}

	private string BuildTargetSuffix(EffectDef effect)
	{
		string targetId = (Data.TargetDef as TargetDef)?.Id?.ToLowerInvariant() ?? "";
		bool beneficial = effect.Id is "block";

		return targetId switch
		{
			"all" or "multiple" or "all_enemies" => " to ALL",
			"all_allies" => beneficial ? " to ALL" : "",
			"random" or "random_enemy" or "random_enemies" => " RANDOMLY",
			_ => ""
		};
	}

	private static Texture2D GetPlaceholderArt()
	{
		if (_placeholderArt != null)
			return _placeholderArt;

		var image = Image.CreateEmpty(48, 48, false, Image.Format.Rgba8);
		var paper = new Color(0.92f, 0.92f, 0.86f);
		var shadow = new Color(0.62f, 0.62f, 0.56f);
		var ink = Colors.Black;
		var accent = new Color(0.2f, 0.35f, 0.72f);

		image.Fill(paper);
		FillRect(image, 0, 0, 48, 2, ink);
		FillRect(image, 0, 46, 48, 2, ink);
		FillRect(image, 0, 0, 2, 48, ink);
		FillRect(image, 46, 0, 2, 48, ink);
		FillRect(image, 5, 36, 38, 4, shadow);
		FillRect(image, 13, 12, 22, 20, accent);
		FillRect(image, 17, 7, 14, 5, ink);
		FillRect(image, 21, 17, 6, 11, paper);

		_placeholderArt = ImageTexture.CreateFromImage(image);
		return _placeholderArt;
	}

	private static void FillRect(Image image, int x, int y, int width, int height, Color color)
	{
		for (int yy = y; yy < y + height; yy++)
		{
			for (int xx = x; xx < x + width; xx++)
			{
				image.SetPixel(xx, yy, color);
			}
		}
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
