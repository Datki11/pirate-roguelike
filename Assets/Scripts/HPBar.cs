using Godot;

public partial class HPBar : Control
{
	[Export] public int Max = 10;
	[Export] public int Value = 10;
	[Export] public Color Back = new Color(0,0,0,1);
	[Export] public Color Fill = new Color(0.1f,0.8f,0.1f,1);

	// Text config
	[Export] public bool ShowText = true;
	[Export] public Color TextColor = Colors.White;

	// Drop your m3x6.ttf here; leave null to use theme default
	[Export] public FontFile PixelFont;
	[Export] public int PixelFontSize = 16;

	public override void _Ready()
	{
		ConfigurePixelFont(PixelFont);
	}

	public void Set(int value, int max)
	{
		Max = Mathf.Max(1, max);
		Value = Mathf.Clamp(value, 0, Max);
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

		string s = $"{Value}/{Max}";
		float baseline = Mathf.Round((size.Y - font.GetHeight(fs)) * 0.5f + font.GetAscent(fs));
		var pos = new Vector2(0, baseline);

		// 1px outline for readability, centered horizontally
		DrawStringOutline(font, pos, s, HorizontalAlignment.Center, size.X, fs, 1, Colors.Black);
		DrawString(font,         pos, s, HorizontalAlignment.Center, size.X, fs, TextColor);
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
}
