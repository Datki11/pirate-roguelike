using Godot;

public partial class HPBar : Control
{
	[Export] public int Max = 10;
	[Export] public int Value = 10;
	[Export] public int Block = 0;
	[Export] public Color Back = new Color(0,0,0,1);
	[Export] public Color Fill = new Color(0.1f,0.8f,0.1f,1);
	[Export] public Color BlockFill = new Color(0.1f, 0.42f, 0.95f, 1);

	// Text config
	[Export] public bool ShowText = true;
	[Export] public Color TextColor = Colors.White;

	// Exported font takes priority; leave null to use the theme default.
	[Export] public FontFile PixelFont;
	[Export] public int PixelFontSize = 16;
	[Export] public Texture2D BlockIcon;

	private const int IconSize = 16;
	private const int IconHeight = 18;
	private const int IconGap = 2;
	private const int BlockRightPadding = 2;
	private const int BlockGap = 4;

	public override void _Ready()
	{
		PixelFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/Minecraft.ttf");
		BlockIcon ??= ResourceLoader.Load<Texture2D>("res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/shield_padded.png");
		ConfigurePixelFont(PixelFont);
	}

	public void Set(int value, int max)
	{
		Max = Mathf.Max(1, max);
		Value = Mathf.Clamp(value, 0, Max);
		QueueRedraw();
	}

	public void SetBlock(int block)
	{
		Block = Mathf.Max(0, block);
		QueueRedraw();
	}

	public override void _Draw()
	{
		var size = Size;
		var rect = new Rect2(Vector2.Zero, size);

		// back + fill + outline (local space)
		DrawRect(rect, Back, true);
		float t = (float)Value / Mathf.Max(1, Max);
		int w = (int)Mathf.Round(size.X * t);
		DrawRect(new Rect2(Vector2.Zero, new Vector2(w, size.Y)), Fill, true);
		DrawRect(rect, Colors.Black, false, 1);

		if (!ShowText) return;

		// pick font (exported takes priority, then theme)
		Font font = PixelFont ?? GetThemeFont("font") ?? GetThemeFont("normal_font");
		ConfigurePixelFont(font);
		int fs = PixelFont != null ? PixelFontSize
				 : (GetThemeFontSize("font_size") > 0 ? GetThemeFontSize("font_size") : 16);

		string healthText = $"{Value}/{Max}";
		float baseline = Mathf.Round((size.Y - font.GetHeight(fs)) * 0.5f + font.GetAscent(fs));
		float healthWidth = font.GetStringSize(healthText, HorizontalAlignment.Left, -1, fs).X;
		float healthX = Mathf.Round((size.X - healthWidth) * 0.5f);
		var healthPos = new Vector2(healthX, baseline);

		DrawStringOutline(font, healthPos, healthText, HorizontalAlignment.Left, healthWidth, fs, 1, Colors.Black);
		DrawString(font, healthPos, healthText, HorizontalAlignment.Left, healthWidth, fs, TextColor);

		if (Block <= 0)
			return;

		string blockText = Block.ToString();
		float blockTextWidth = font.GetStringSize(blockText, HorizontalAlignment.Left, -1, fs).X;
		float blockX = Mathf.Round(size.X + BlockGap + BlockRightPadding);
		if (BlockIcon != null)
		{
			DrawTextureRect(BlockIcon, new Rect2(new Vector2(blockX, 0), new Vector2(IconSize, IconHeight)), false);
			blockX += IconSize + IconGap;
		}

		var blockPos = new Vector2(blockX, baseline);
		DrawStringOutline(font, blockPos, blockText, HorizontalAlignment.Left, blockTextWidth, fs, 1, Colors.Black);
		DrawString(font, blockPos, blockText, HorizontalAlignment.Left, blockTextWidth, fs, TextColor);
	}

	public float GetInlineContentWidth()
	{
		Font font = PixelFont ?? GetThemeFont("font") ?? GetThemeFont("normal_font");
		int fs = PixelFont != null ? PixelFontSize
				 : (GetThemeFontSize("font_size") > 0 ? GetThemeFontSize("font_size") : 16);
		float width = Size.X > 0f ? Size.X : CustomMinimumSize.X;
		return width + GetBlockWidth(font, fs);
	}

	private float GetBlockWidth(Font font, int fs)
	{
		if (Block <= 0 || font == null)
			return 0f;

		float textWidth = font.GetStringSize(Block.ToString(), HorizontalAlignment.Left, -1, fs).X;
		return BlockGap + BlockRightPadding + (BlockIcon != null ? IconSize + IconGap : 0) + textWidth + BlockRightPadding;
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
}
