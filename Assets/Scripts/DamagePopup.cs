using Godot;

public partial class DamagePopup : Control
{
	[Export] public FontFile Font;       // assign m3x6.ttf (or rely on Theme)
	[Export] public int FontSize = 16;
	[Export] public Color Color = new(1f, 0.3f, 0.3f);
	[Export] public Color HealColor = new(0.3f, 1f, 0.3f);
	[Export] public Color Outline = Colors.Black;
	[Export] public float Duration = 0.6f;
	[Export] public Vector2 Rise = new(0, -18);

	private string _text = "";
	private Color _use;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		if (Size == Vector2.Zero) { CustomMinimumSize = new Vector2(32, 16); Size = CustomMinimumSize; }
		ZIndex = 1000;
	}

	public void ShowNumber(int amount, bool isHeal = false)
	{
		_text = amount.ToString();
		_use  = isHeal ? HealColor : Color;

		var start = GlobalPosition.Floor();
		var end   = (start + Rise).Floor();

		Modulate = Colors.White;
		Scale    = Vector2.One;

		var tw = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tw.Parallel().TweenProperty(this, "global_position", end, Duration);
		tw.Parallel().TweenProperty(this, "modulate:a", 0.0f, Duration);
		tw.Chain().TweenCallback(Callable.From(QueueFree));

		QueueRedraw();
	}

	public override void _Draw()
	{
		if (string.IsNullOrEmpty(_text)) return;

		var font = Font ?? GetThemeFont("font") ?? GetThemeFont("normal_font");
		if (font == null) { GD.PushWarning("DamagePopup: no font available."); return; }

		var fs = Font != null ? FontSize
			   : (GetThemeFontSize("font_size") > 0 ? GetThemeFontSize("font_size") : 16);

		float baseline = Mathf.Round((Size.Y - font.GetHeight(fs)) * 0.5f + font.GetAscent(fs));
		var pos = new Vector2(0, baseline);

		DrawStringOutline(font, pos, _text, HorizontalAlignment.Center, Size.X, fs, 1, Outline);
		DrawString(font, pos, _text, HorizontalAlignment.Center, Size.X, fs, _use);
	}
}
