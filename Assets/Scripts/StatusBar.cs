using Godot;
using System.Collections.Generic;

public partial class StatusBar : Control
{
	private const int IconSize = 16;
	private const int IconHeight = 18;
	private const int Gap = 2;
	private const int EntryGap = 5;

	private static readonly Dictionary<string, string> IconPaths = new()
	{
		["protector"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/shield_padded.png",
		["bleed"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/bleed_padded.png",
		["weak"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/weak_padded.png",
		["regen"] = "res://Assets/Sprites/Icons/placeholder-icons/icons/16x16/regen_padded.png"
	};

	private static readonly Dictionary<string, Texture2D> Icons = new();
	private readonly Dictionary<string, int> _statuses = new();
	private readonly Dictionary<string, PulseState> _pulses = new();

	[Export] public FontFile PixelFont { get; set; }
	[Export] public int PixelFontSize { get; set; } = 16;
	[Export] public Color TextColor { get; set; } = Colors.White;
	[Export] public Color OutlineColor { get; set; } = Colors.Black;
	[Export] public Color NegativeFlashColor { get; set; } = new(1f, 0.12f, 0.08f);
	[Export] public Color PositiveFlashColor { get; set; } = new(0.22f, 0.68f, 1f);

	private sealed class PulseState
	{
		public float Scale = 1f;
		public float Flash;
		public Color FlashColor;
		public Tween Tween;
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		PixelFont ??= ResourceLoader.Load<FontFile>("res://Assets/Fonts/Minecraft.ttf");
		ConfigurePixelFont(PixelFont);
	}

	public void SetStatuses(IReadOnlyDictionary<string, int> statuses, int block)
	{
		_statuses.Clear();
		if (statuses != null)
		{
			foreach (var pair in statuses)
			{
				if (pair.Value > 0)
					_statuses[pair.Key] = pair.Value;
			}
		}

		Visible = _statuses.Count > 0;
		QueueRedraw();
	}

	public void PlayStatusPulse(string id, bool negative)
	{
		if (string.IsNullOrWhiteSpace(id) || !_statuses.ContainsKey(id))
			return;

		if (!_pulses.TryGetValue(id, out var pulse))
		{
			pulse = new PulseState();
			_pulses[id] = pulse;
		}

		pulse.Tween?.Kill();
		pulse.Scale = 1f;
		pulse.Flash = 0f;
		pulse.FlashColor = negative ? NegativeFlashColor : PositiveFlashColor;

		pulse.Tween = CreateTween();
		pulse.Tween.SetTrans(Tween.TransitionType.Quad);
		pulse.Tween.SetEase(Tween.EaseType.Out);
		pulse.Tween.Parallel().TweenMethod(Callable.From<float>(value =>
		{
			pulse.Scale = value;
			QueueRedraw();
		}), 1f, 2f, 0.09f);
		pulse.Tween.Parallel().TweenMethod(Callable.From<float>(value =>
		{
			pulse.Flash = value;
			QueueRedraw();
		}), 0f, 1f, 0.06f);
		pulse.Tween.TweenMethod(Callable.From<float>(value =>
		{
			pulse.Scale = value;
			QueueRedraw();
		}), 2f, 1f, 0.14f);
		pulse.Tween.Parallel().TweenMethod(Callable.From<float>(value =>
		{
			pulse.Flash = value;
			QueueRedraw();
		}), 1f, 0f, 0.16f);
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_statuses.Count == 0)
			return;

		Font font = PixelFont ?? GetThemeFont("font") ?? GetThemeFont("normal_font");
		if (font == null)
			return;

		ConfigurePixelFont(font);
		float x = Mathf.Max(0f, Mathf.Round((Size.X - GetContentWidth(font, PixelFontSize)) * 0.5f));
		foreach (string id in new[] { "protector", "bleed", "weak", "regen" })
		{
			if (!_statuses.TryGetValue(id, out int amount) || amount <= 0)
				continue;

			Texture2D icon = GetIcon(id);
			Rect2 entryRect = GetEntryRect(font, id, amount, x);
			Vector2 center = entryRect.Position + entryRect.Size * 0.5f;
			float scale = 1f;
			Color drawColor = TextColor;
			if (_pulses.TryGetValue(id, out var pulse))
			{
				scale = Mathf.Max(0.01f, pulse.Scale);
				drawColor = TextColor.Lerp(pulse.FlashColor, Mathf.Clamp(pulse.Flash, 0f, 1f));
				DrawSetTransform((center * (1f - scale)).Floor(), 0f, new Vector2(scale, scale));
			}

			if (icon != null)
				DrawTextureRect(icon, new Rect2(new Vector2(x, 0), new Vector2(IconSize, IconHeight)), false, drawColor);

			string text = amount.ToString();
			float textWidth = font.GetStringSize(text, HorizontalAlignment.Left, -1, PixelFontSize).X;
			float baseline = Mathf.Round((Size.Y - font.GetHeight(PixelFontSize)) * 0.5f + font.GetAscent(PixelFontSize));
			var textPos = new Vector2(x + IconSize + Gap, baseline);
			DrawStringOutline(font, textPos, text, HorizontalAlignment.Left, textWidth, PixelFontSize, 1, OutlineColor);
			DrawString(font, textPos, text, HorizontalAlignment.Left, textWidth, PixelFontSize, drawColor);

			if (scale != 1f)
				DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

			x += IconSize + Gap + textWidth + EntryGap;
		}
	}

	private float GetContentWidth(Font font, int fs)
	{
		float width = 0f;
		foreach (string id in new[] { "protector", "bleed", "weak", "regen" })
		{
			if (!_statuses.TryGetValue(id, out int amount) || amount <= 0)
				continue;

			float textWidth = font.GetStringSize(amount.ToString(), HorizontalAlignment.Left, -1, fs).X;
			if (width > 0f)
				width += EntryGap;
			width += IconSize + Gap + textWidth;
		}

		return width;
	}

	private Rect2 GetEntryRect(Font font, string id, int amount, float x)
	{
		float textWidth = font.GetStringSize(amount.ToString(), HorizontalAlignment.Left, -1, PixelFontSize).X;
		return new Rect2(new Vector2(x, 0), new Vector2(IconSize + Gap + textWidth, Size.Y));
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
