using Godot;
using System.Collections.Generic;

public partial class SmallCard : BaseCardView
{
	private static readonly Color TriggerColor = new(0.85f, 0.53f, 0.0f);
	private const int PlaceholderArtSize = 64;
	private static Texture2D _placeholderArt;

	[Export] public NodePath TitlePath { get; set; }
	[Export] public NodePath ArtPath { get; set; }
	[Export] public NodePath BodyPath { get; set; }
	[Export] public NodePath BadgeIconPath { get; set; }
	[Export] public NodePath TooltipDisplayPath { get; set; }
	[Export] public int EffectIconWidth { get; set; } = RichTextInlineIcon.DefaultWidth;
	[Export] public int EffectIconHeight { get; set; } = RichTextInlineIcon.DefaultHeight;
	[Export] public string EffectIconAlign { get; set; } = RichTextInlineIcon.DefaultAlign;

	[Export] public CardData Data { get; set; }

	private TextureRect _art, _badgeIcon;
	private Label _title;
	private RichTextLabel _body;
	private TooltipDisplay _tooltipDisplay;
	private Panel _frame;
	private StyleBoxFlat _normalFrameStyle;
	private StyleBoxFlat _focusFrameStyle;
	private bool _targetingFocus;
	private bool _controllerFocus;
	private float _targetingFocusTime;
	
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
		_frame = GetNodeOrNull<Panel>("Frame");
		CaptureFrameStyles();

		MouseFilter = MouseFilterEnum.Pass;
		_art.TextureFilter = TextureFilterEnum.Nearest;
		_tooltipDisplay.SetHoverSource(this);
		ConfigurePixelFont(_title.GetThemeFont("font"));
		SetupBody(_body);
		if (Data != null) Apply();
	}

	public override void _Process(double delta)
	{
		if (!_targetingFocus || _focusFrameStyle == null)
			return;

		_targetingFocusTime += (float)delta;
		float pulse = (Mathf.Sin(_targetingFocusTime * 8.5f) + 1f) * 0.5f;
		float flash = Mathf.Pow(pulse, 2.25f);
		var gold = new Color(0.93f, 0.66f, 0.12f, 1f);
		var white = new Color(1f, 0.96f, 0.68f, 1f);
		_focusFrameStyle.BorderColor = Colors.Black.Lerp(gold, 0.62f + pulse * 0.38f).Lerp(white, flash * 0.32f);
	}

	public override void SetTargetingFocus(bool focused)
	{
		if (_targetingFocus == focused)
			return;

		_targetingFocus = focused;
		_targetingFocusTime = 0f;
		ApplyFocusStyle();
	}

	public override void SetControllerFocus(bool focused)
	{
		if (_controllerFocus == focused)
			return;

		_controllerFocus = focused;
		ApplyFocusStyle();
	}

	private void ApplyFocusStyle()
	{
		bool focused = _targetingFocus || _controllerFocus;

		if (_frame == null)
			return;

		if (_normalFrameStyle == null || _focusFrameStyle == null)
			return;

		_frame.AddThemeStyleboxOverride("panel", focused ? _focusFrameStyle : _normalFrameStyle);
		SetProcess(_targetingFocus);
	}

	public override void SetTooltipSuppressed(bool suppressed)
	{
		_tooltipDisplay?.SetSuppressed(suppressed);
	}

	private void CaptureFrameStyles()
	{
		if (_frame == null)
			return;

		_normalFrameStyle = (_frame.GetThemeStylebox("panel") as StyleBoxFlat)?.Duplicate() as StyleBoxFlat;
		_focusFrameStyle = _normalFrameStyle?.Duplicate() as StyleBoxFlat;
		if (_normalFrameStyle == null || _focusFrameStyle == null)
			return;

		_focusFrameStyle.BorderWidthLeft = 4;
		_focusFrameStyle.BorderWidthTop = 4;
		_focusFrameStyle.BorderWidthRight = 4;
		_focusFrameStyle.BorderWidthBottom = 4;
		_focusFrameStyle.BorderColor = new Color(0.93f, 0.66f, 0.12f, 1f);
		_frame.AddThemeStyleboxOverride("panel", _normalFrameStyle);
		SetProcess(false);
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
		r.VerticalAlignment = VerticalAlignment.Center;
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
				if (ShouldOmitEffectLine(effect)) continue;
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
			hasContent = true;
		}

		if (Data.Exhaust)
		{
			if (hasContent) _body.Newline();
			_body.AppendText("[color=#d98600]Exhaust[/color]");
		}
	}

	private void RenderEffectLine(EffectEntry e)
	{
		int iconWidth = Mathf.Max(1, EffectIconWidth);
		int iconHeight = Mathf.Max(1, EffectIconHeight);

		string effectText = e.Def.Id switch
		{
			"attack" => "Deal",
			"block" => "Gain",
			"heal" => "Gain",
			"regen" => "Gain",
			"bleed" => "Apply",
			"weak" => "Apply",
			"strategist" => "Gain",
			"play_top_cards" => "Play",
			"protect" => "Apply",
			"body_slam" => "Deal",
			_ => e.Def.DisplayName
		};

		_body.PushParagraph(HorizontalAlignment.Center);
		_body.AddText($"{effectText} ");
		if (e.Def.Icon != null)
		{
			_body.AppendText(RichTextInlineIcon.Build(e.Def.Icon, iconWidth, iconHeight, EffectIconAlign));
			_body.AddText(" ");
		}
		_body.AddText($"{e.Amount}{BuildTargetSuffix(e.Def)}");
		_body.Pop();
	}

	private bool ShouldOmitEffectLine(EffectEntry e)
	{
		return e?.Def?.Id is "play_top_cards" or "protect" or "body_slam" && !string.IsNullOrWhiteSpace(Data?.RulesText);
	}

	private string BuildTargetSuffix(EffectDef effect)
	{
		string targetId = (Data.TargetDef as TargetDef)?.Id?.ToLowerInvariant() ?? "";
		bool beneficial = effect.Id is "block" or "heal" or "regen" or "protect" or "play_top_cards" or "strategist";

		return targetId switch
		{
			"all" or "multiple" => beneficial ? " to all allies" : " to all enemies",
			"all_enemies" => " to all enemies",
			"all_allies" => " to all allies",
			"random" or "random_enemy" or "random_enemies" => " RANDOMLY",
			_ => ""
		};
	}

	private static Texture2D GetPlaceholderArt()
	{
		if (_placeholderArt != null)
			return _placeholderArt;

		var image = Image.CreateEmpty(PlaceholderArtSize, PlaceholderArtSize, false, Image.Format.Rgba8);
		var paper = new Color(0.92f, 0.92f, 0.86f);
		var shadow = new Color(0.62f, 0.62f, 0.56f);
		var ink = Colors.Black;
		var accent = new Color(0.2f, 0.35f, 0.72f);

		image.Fill(paper);
		FillRect(image, 0, 0, PlaceholderArtSize, 2, ink);
		FillRect(image, 0, PlaceholderArtSize - 2, PlaceholderArtSize, 2, ink);
		FillRect(image, 0, 0, 2, PlaceholderArtSize, ink);
		FillRect(image, PlaceholderArtSize - 2, 0, 2, PlaceholderArtSize, ink);
		FillRect(image, 7, 48, 50, 5, shadow);
		FillRect(image, 17, 16, 30, 27, accent);
		FillRect(image, 23, 9, 18, 7, ink);
		FillRect(image, 28, 23, 8, 15, paper);

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
		if (Data.Exhaust)
			entries.Add(new TooltipEntry("Exhaust", "Removed from this unit's deck for the rest of combat after it is played.", null, TriggerColor));

		return entries;
	}

	private string GetEffectTooltip(EffectDef effect)
		=> effect.Id switch
		{
			"attack" => "Damage dealt to a target.",
			"block" => "Prevents incoming damage.",
			"heal" => "Restores HP, up to maximum HP.",
			"bleed" => "At the start of your turn, take bleed damage.",
			"weak" => "Deal 50% less damage. At the start of your turn, lose 1 weak.",
			"regen" => "At the start of your turn, gain HP equal to regen, then lose 1 regen.",
			"strategist" => "See and play that many extra cards from the top of this unit's draw pile.",
			"play_top_cards" => "An ally plays cards from the top of their deck.",
			_ => string.IsNullOrWhiteSpace(effect.LongFormat) ? effect.DisplayName : effect.LongFormat
		};

	private string GetTargetTooltip(TargetDef target)
		=> target.Id switch
		{
			"single" => "Choose one enemy target.",
			"all" => "Affects all valid targets.",
			"multiple" or "all_enemies" => "Affects all enemies.",
			"all_allies" => "Affects all allies.",
			"ally" => "Choose one ally target.",
			"self" => "Targets this unit.",
			_ => target.DisplayName
		};

	private string GetTriggerTooltip(CardTrigger trigger)
		=> trigger switch
		{
			CardTrigger.Lethal => "Happens when this card kills a unit.",
			CardTrigger.Revenge => "Happens when this unit takes damage.",
			CardTrigger.Shuffle => "Happens when this unit shuffles its deck.",
			CardTrigger.Pierce => "Happens when this card deals unblocked attack damage.",
			_ => ""
		};

}
