using Godot;
using System.Collections.Generic;

public partial class TooltipDisplay : Control
{
	[Export] public NodePath HoverSourcePath { get; set; }
	[Export] public bool TrackHoverSource { get; set; } = true;
	[Export] public FontFile PixelFont { get; set; }
	[Export] public int FontSize { get; set; } = 16;
	[Export] public FontFile BoldPixelFont { get; set; }
	[Export] public int BoldFontSize { get; set; } = 20;
	[Export] public Vector2 PanelSize { get; set; } = new(220, 48);
	[Export] public int Padding { get; set; } = 8;
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
		ConfigurePixelFont(BoldPixelFont);
		_hoverSource = GetNodeOrNull<Control>(HoverSourcePath);
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!TrackHoverSource) return;
		bool shouldShow = _entries.Count > 0 && IsHoveringSource();
		if (shouldShow)
		{
			RefreshLayout();
		}
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
		RefreshLayout();
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

		foreach (var entry in _entries)
		{
			var panel = CreatePanel(entry);
			AddChild(panel);
		}

		RefreshLayout();
	}

	private Control CreatePanel(TooltipEntry entry)
	{
		float contentWidth = Mathf.Max(1, PanelSize.X - Padding * 2);
		var panel = new Panel
		{
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
			FitContent = true,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore
		};
		if (PixelFont != null) label.AddThemeFontOverride("normal_font", PixelFont);
		if (BoldPixelFont != null) label.AddThemeFontOverride("bold_font", BoldPixelFont);
		label.AddThemeFontSizeOverride("normal_font_size", FontSize);
		label.AddThemeFontSizeOverride("bold_font_size", BoldFontSize);
		label.AddThemeColorOverride("default_color", TextColor);
		ConfigurePixelFont(label.GetThemeFont("normal_font"));
		ConfigurePixelFont(label.GetThemeFont("bold_font"));

		panel.AddChild(label);

		label.SetAnchorsPreset(LayoutPreset.TopLeft);
		label.Position = new Vector2(Padding, Padding);
		label.Size = new Vector2(contentWidth, Mathf.Max(1, PanelSize.Y - Padding * 2));
		return panel;
	}

	private void RefreshLayout()
	{
		float y = 0;
		float contentWidth = Mathf.Max(1, PanelSize.X - Padding * 2);
		foreach (var child in GetChildren())
		{
			if (child is not Panel panel)
				continue;

			var label = panel.GetChildCount() > 0 ? panel.GetChildOrNull<RichTextLabel>(0) : null;
			if (label == null)
				continue;

			label.Position = new Vector2(Padding, Padding);
			label.Size = new Vector2(contentWidth, Mathf.Max(1, label.Size.Y));

			float labelHeight = Mathf.Ceil(label.GetContentHeight());
			float panelHeight = Mathf.Max(PanelSize.Y, labelHeight + Padding * 2);
			panel.Position = new Vector2(0, y).Floor();
			panel.CustomMinimumSize = new Vector2(PanelSize.X, panelHeight);
			panel.Size = panel.CustomMinimumSize;
			label.Size = new Vector2(contentWidth, Mathf.Max(1, panelHeight - Padding * 2));

			y += panelHeight + Gap;
		}

		float totalHeight = Mathf.Max(0, y - Gap);
		CustomMinimumSize = new Vector2(PanelSize.X, totalHeight);
		Size = CustomMinimumSize;
	}

	private string BuildTooltipText(TooltipEntry entry)
	{
		string icon = "";
		if (entry.Icon != null && !string.IsNullOrWhiteSpace(entry.Icon.ResourcePath))
			icon = $"[img=16x16]{entry.Icon.ResourcePath}[/img] ";

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
