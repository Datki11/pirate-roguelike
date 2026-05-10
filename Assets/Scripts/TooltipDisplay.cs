using Godot;
using System.Collections.Generic;

public partial class TooltipDisplay : Control
{
	[Export] public NodePath HoverSourcePath { get; set; }
	[Export] public bool TrackHoverSource { get; set; } = true;
	[Export] public FontFile PixelFont { get; set; }
	[Export] public int FontSize { get; set; } = 10;
	[Export] public Vector2 PanelSize { get; set; } = new(132, 44);
	[Export] public int Padding { get; set; } = 4;
	[Export] public int Gap { get; set; } = 4;
	[Export] public Color PanelColor { get; set; } = new(0.94f, 0.94f, 0.94f, 0.88f);
	[Export] public Color TextColor { get; set; } = Colors.Black;
	[Export] public Color BorderColor { get; set; } = Colors.Black;

	private readonly List<TooltipEntry> _entries = new();
	private Control _hoverSource;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ConfigurePixelFont(PixelFont);
		_hoverSource = GetNodeOrNull<Control>(HoverSourcePath);
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!TrackHoverSource) return;
		bool shouldShow = _entries.Count > 0 && IsHoveringSource();
		if (Visible != shouldShow) Visible = shouldShow;
	}

	public void SetHoverSource(Control source)
	{
		_hoverSource = source;
	}

	public void SetEntries(IEnumerable<TooltipEntry> entries)
	{
		_entries.Clear();
		foreach (var entry in entries)
		{
			if (string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.Text))
				continue;
			if (HasTitle(entry.Title))
				continue;
			_entries.Add(entry);
		}
		Rebuild();
	}

	public void ShowTips()
	{
		TrackHoverSource = false;
		Visible = _entries.Count > 0;
	}

	public void HideTips()
	{
		TrackHoverSource = false;
		Visible = false;
	}

	private bool IsHoveringSource()
	{
		var source = _hoverSource ?? GetParent<Control>();
		if (source == null || !GodotObject.IsInstanceValid(source))
			return false;

		return source.GetGlobalRect().HasPoint(GetGlobalMousePosition());
	}

	private bool HasTitle(string title)
	{
		foreach (var entry in _entries)
			if (entry.Title == title) return true;
		return false;
	}

	private void Rebuild()
	{
		foreach (var child in GetChildren())
			child.QueueFree();

		float y = 0;
		foreach (var entry in _entries)
		{
			var panel = CreatePanel(entry, y);
			AddChild(panel);
			y += panel.CustomMinimumSize.Y + Gap;
		}
	}

	private Control CreatePanel(TooltipEntry entry, float y)
	{
		var panel = new Panel
		{
			Position = new Vector2(0, y).Floor(),
			CustomMinimumSize = PanelSize,
			Size = PanelSize,
			MouseFilter = MouseFilterEnum.Ignore
		};
		panel.AddThemeStyleboxOverride("panel", CreateStyle());

		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			Text = BuildTooltipText(entry),
			ScrollActive = false,
			FitContent = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore
		};
		label.SetAnchorsPreset(LayoutPreset.FullRect);
		label.OffsetLeft = Padding;
		label.OffsetTop = Padding;
		label.OffsetRight = -Padding;
		label.OffsetBottom = -Padding;
		if (PixelFont != null) label.AddThemeFontOverride("normal_font", PixelFont);
		label.AddThemeFontSizeOverride("normal_font_size", FontSize);
		label.AddThemeColorOverride("default_color", TextColor);
		ConfigurePixelFont(label.GetThemeFont("normal_font"));
		panel.AddChild(label);
		return panel;
	}

	private string BuildTooltipText(TooltipEntry entry)
	{
		string icon = "";
		if (entry.Icon != null && !string.IsNullOrWhiteSpace(entry.Icon.ResourcePath))
			icon = $"[img]{entry.Icon.ResourcePath}[/img] ";

		Color titleColor = entry.TitleColor ?? TextColor;
		return $"{icon}[color=#{titleColor.ToHtml(false)}][b]{entry.Title}[/b][/color]\n{entry.Text}";
	}

	private StyleBoxFlat CreateStyle()
	{
		return new StyleBoxFlat
		{
			BgColor = PanelColor,
			BorderColor = BorderColor,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			AntiAliasing = false
		};
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
