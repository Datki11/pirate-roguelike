using Godot;

public partial class DamagePopup : Control
{
	private const string HeartIconPath = "res://Assets/Sprites/Icons/Generated/heart_32.png";

	[Export] public FontFile Font;
	[Export] public int FontSize = 32;
	[Export] public Texture2D HeartIcon;
	[Export] public Color Color = Colors.White;
	[Export] public Color HealColor = Colors.White;
	[Export] public Color BuffColor = Colors.White;
	[Export] public Color CurseColor = Colors.White;
	[Export] public Color Outline = Colors.Black;
	[Export] public float Duration = 1.7f;
	[Export] public Vector2 Rise = new(0, -28);

	private string _leadingText = "";
	private string _valueText = "";
	private Color _use;
	private Texture2D _icon;
	private bool _tintIcon;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		TextureFilter = TextureFilterEnum.Nearest;
		Font ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/VCR_OSD_MONO_1.001.ttf");
		HeartIcon ??= LoadTexture(HeartIconPath);
		ConfigurePixelFont(Font);
		if (Size == Vector2.Zero) { CustomMinimumSize = new Vector2(160, 56); Size = CustomMinimumSize; }
		ZIndex = 1000;
	}

	public void ShowNumber(int amount, bool isHeal = false)
	{
		int value = amount < 0 ? -amount : amount;
		_leadingText = isHeal ? "+" : "-";
		_valueText = value.ToString();
		_use = isHeal ? HealColor : Color;
		_icon = HeartIcon;
		_tintIcon = true;

		StartTween();
	}

	public void ShowIconValue(int amount, Texture2D icon, Color color)
	{
		int value = amount < 0 ? -amount : amount;
		_leadingText = "";
		_valueText = $"+{value}";
		_use = Colors.White;
		_icon = icon;
		_tintIcon = false;
		StartTween();
	}

	private void StartTween()
	{
		var start = GlobalPosition.Floor();
		var end = (start + Rise).Floor();

		Modulate = Colors.White;

		var tw = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tw.Parallel().TweenProperty(this, "global_position", end, Duration);
		tw.Parallel().TweenProperty(this, "modulate:a", 0.0f, Duration);
		tw.Chain().TweenCallback(Callable.From(QueueFree));

		QueueRedraw();
	}

	public override void _Draw()
	{
		if (string.IsNullOrEmpty(_leadingText) && string.IsNullOrEmpty(_valueText) && _icon == null) return;

		var font = Font ?? GetThemeFont("font") ?? GetThemeFont("normal_font");
		if (font == null) { GD.PushWarning("DamagePopup: no font available."); return; }
		ConfigurePixelFont(font);

		var fs = Font != null ? FontSize
			   : (GetThemeFontSize("font_size") > 0 ? GetThemeFontSize("font_size") : 16);

		float leadingWidth = string.IsNullOrEmpty(_leadingText) ? 0f : font.GetStringSize(_leadingText, HorizontalAlignment.Left, -1, fs).X;
		float valueWidth = string.IsNullOrEmpty(_valueText) ? 0f : font.GetStringSize(_valueText, HorizontalAlignment.Left, -1, fs).X;
		Vector2 iconSize = _icon?.GetSize() ?? Vector2.Zero;
		float iconWidth = iconSize.X;
		float gapAfterLeading = leadingWidth > 0f && iconWidth > 0f ? 2f : 0f;
		float gapAfterIcon = iconWidth > 0f && valueWidth > 0f ? 3f : 0f;
		float totalWidth = leadingWidth + gapAfterLeading + iconWidth + gapAfterIcon + valueWidth;
		float x = Mathf.Round((Size.X - totalWidth) * 0.5f);
		float baseline = Mathf.Round((Size.Y - font.GetHeight(fs)) * 0.5f + font.GetAscent(fs));

		if (!string.IsNullOrEmpty(_leadingText))
		{
			var leadingPos = new Vector2(x, baseline);
			DrawStringOutline(font, leadingPos, _leadingText, HorizontalAlignment.Left, leadingWidth, fs, 2, Outline);
			DrawString(font, leadingPos, _leadingText, HorizontalAlignment.Left, leadingWidth, fs, _use);
			x += leadingWidth + gapAfterLeading;
		}

		if (_icon != null)
		{
			float iconY = Mathf.Round((Size.Y - iconSize.Y) * 0.5f);
			DrawTextureRect(_icon, new Rect2(new Vector2(x, iconY), iconSize), false, _tintIcon ? _use : Colors.White);
			x += iconSize.X + gapAfterIcon;
		}

		var pos = new Vector2(x, baseline);
		DrawStringOutline(font, pos, _valueText, HorizontalAlignment.Left, valueWidth, fs, 2, Outline);
		DrawString(font, pos, _valueText, HorizontalAlignment.Left, valueWidth, fs, _use);
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

	private Texture2D LoadTexture(string path)
	{
		var texture = ResourceLoader.Load<Texture2D>(path);
		if (texture != null)
			return texture;

		var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
		return image == null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
	}
}
