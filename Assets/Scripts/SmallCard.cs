using Godot;

public partial class SmallCard : BaseCardView
{
	[Export] public NodePath ArtPath { get; set; }
	[Export] public NodePath Row1Path { get; set; }
	[Export] public NodePath Row2Path { get; set; }
	[Export] public NodePath Row3Path { get; set; }
	[Export] public NodePath BadgeIconPath { get; set; }

	[Export] public CardData Data { get; set; }

	private TextureRect _art, _badgeIcon;
	private RichTextLabel _row1, _row2, _row3;
	
	public override void SetData(CardData data)
	{
		Data = data;
		if (IsInsideTree()) Apply();
	}

	public override void _Ready()
	{
		_art = GetNode<TextureRect>(ArtPath);
		_badgeIcon = GetNode<TextureRect>(BadgeIconPath);
		_row1 = GetNode<RichTextLabel>(Row1Path);
		_row2 = GetNode<RichTextLabel>(Row2Path);
		_row3 = GetNode<RichTextLabel>(Row3Path);

		SetupRow(_row1); SetupRow(_row2); SetupRow(_row3);
		if (Data != null) Apply();
	}

	private void SetupRow(RichTextLabel r)
	{
		r.BbcodeEnabled = true; r.FitContent = false;
		r.AutowrapMode = TextServer.AutowrapMode.Off;
		r.ScrollActive = false;
		r.HorizontalAlignment = HorizontalAlignment.Left;
		r.VerticalAlignment = VerticalAlignment.Top;
		r.AddThemeConstantOverride("line_separation", 0);

		var font = r.GetThemeFont("normal_font");
		var fs = r.GetThemeFontSize("normal_font_size");
		int h = Mathf.CeilToInt(font.GetHeight(fs)) + 1;   // avoid bottom shave
		r.CustomMinimumSize = new Vector2(0, h);
	}

	private void Apply()
	{
		_art.Texture = Data.Art;

		// Target badge (if you used TargetDef)
		if (Data.TargetDef is TargetDef t && t.BadgeIcon != null)
			_badgeIcon.Texture = t.BadgeIcon;

		var rows = new[] { _row1, _row2, _row3 };
		for (int i = 0; i < rows.Length; i++)
		{
			if (i < Data.Effects.Count && Data.Effects[i]?.Def != null)
			{
				rows[i].Visible = true;
				rows[i].Text = BuildRowBBCode(Data.Effects[i], rows[i]);
			}
			else { rows[i].Visible = false; rows[i].Text = ""; }
		}
	}

	private string BuildRowBBCode(EffectEntry e, RichTextLabel r)
	{
		var font = r.GetThemeFont("normal_font");
		var fs = r.GetThemeFontSize("normal_font_size");
		int lineH = Mathf.CeilToInt(font.GetHeight(fs));
		int iconPx = Mathf.Max(1, lineH - 1);

		string iconPath = e.Def.Icon?.ResourcePath ?? "";
		// Use EffectDef.ShortFormat later if you want text, colors, etc.
		return string.IsNullOrEmpty(iconPath)
			? e.Amount.ToString()
			: $"[center][img={8}x{8}]{iconPath}[/img] {e.Amount}[/center]";
	}
}
