using Godot;
using System.Collections.Generic;

public partial class TooltipDisplay : Control
{
	private const int TooltipCanvasLayer = 1000;
	private const int TooltipZIndex = 4096;
	private const string TooltipLayerName = "GlobalTooltipLayer";
	private const string TooltipRootName = "GlobalTooltipRoot";

	public static Node ModalTooltipScope { get; set; }

	private static CanvasLayer _tooltipLayer;
	private static Control _tooltipRoot;

	[Export] public NodePath HoverSourcePath { get; set; }
	[Export] public bool TrackHoverSource { get; set; } = true;
	[Export] public FontFile PixelFont { get; set; }
	[Export] public int FontSize { get; set; } = 16;
	[Export] public FontFile BoldPixelFont { get; set; }
	[Export] public int BoldFontSize { get; set; } = 20;
	[Export] public int IconWidth { get; set; } = RichTextInlineIcon.DefaultWidth;
	[Export] public int IconHeight { get; set; } = RichTextInlineIcon.DefaultHeight;
	[Export] public string IconAlign { get; set; } = RichTextInlineIcon.DefaultAlign;
	[Export] public Vector2 PanelSize { get; set; } = new(220, 48);
	[Export] public int Padding { get; set; } = 8;
	[Export] public int Gap { get; set; } = 4;
	[Export] public int ScreenMargin { get; set; } = 4;
	[Export] public Color PanelColor { get; set; } = new(0.94f, 0.94f, 0.94f, 0.88f);
	[Export] public Color TextColor { get; set; } = Colors.Black;
	[Export] public Color BorderColor { get; set; } = Colors.Black;

	private readonly List<TooltipEntry> _entries = new();
	private Control _hoverSource;
	private Control _popupRoot;
	private bool _needsRebuild;
	private bool _rebuildDeferred;
	private bool _suppressed;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ConfigurePixelFont(PixelFont);
		ConfigurePixelFont(BoldPixelFont);
		_hoverSource = GetNodeOrNull<Control>(HoverSourcePath);
		SetTipsVisible(false);
		if (_entries.Count > 0)
			QueueRebuild();
	}

	public override void _ExitTree()
	{
		SetTipsVisible(false);
		_needsRebuild = false;
		_rebuildDeferred = false;
		if (_popupRoot != null && GodotObject.IsInstanceValid(_popupRoot))
			_popupRoot.QueueFree();
		_popupRoot = null;
	}

	public override void _Process(double delta)
	{
		if (_needsRebuild && !_rebuildDeferred)
			RebuildIfNeeded();

		if (!TrackHoverSource) return;
		bool shouldShow = !_suppressed && _entries.Count > 0 && IsHoveringSource();
		if (shouldShow)
		{
			RefreshLayout();
		}
		SetTipsVisible(shouldShow);
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
		QueueRebuild();
	}

	public void ShowTips()
	{
		TrackHoverSource = false;
		RebuildIfNeeded();
		RefreshLayout();
		SetTipsVisible(_entries.Count > 0);
	}

	public void HideTips()
	{
		TrackHoverSource = false;
		SetTipsVisible(false);
	}

	public void SetSuppressed(bool suppressed)
	{
		if (_suppressed == suppressed)
			return;

		_suppressed = suppressed;
		if (_suppressed)
			SetTipsVisible(false);
	}

	private bool IsHoveringSource()
	{
		var source = _hoverSource ?? GetParent<Control>();
		if (source == null || !GodotObject.IsInstanceValid(source))
			return false;

		if (Deck.IsDrawPileModalOpen && !IsInModalScope(source))
			return false;

		return source.GetGlobalRect().HasPoint(GetGlobalMousePosition());
	}

	private static bool IsInModalScope(Node node)
	{
		return ModalTooltipScope != null
			&& GodotObject.IsInstanceValid(ModalTooltipScope)
			&& (node == ModalTooltipScope || ModalTooltipScope.IsAncestorOf(node));
	}

	private bool HasTitle(string title)
	{
		foreach (var entry in _entries)
			if (entry.Title == title) return true;
		return false;
	}

	private void Rebuild()
	{
		var popupRoot = EnsurePopupRoot();
		if (popupRoot == null)
		{
			_needsRebuild = true;
			return;
		}

		foreach (var child in popupRoot.GetChildren())
		{
			popupRoot.RemoveChild(child);
			child.QueueFree();
		}

		foreach (var entry in _entries)
		{
			var panel = CreatePanel(entry);
			popupRoot.AddChild(panel);
		}

		RefreshLayout();
	}

	private void QueueRebuild()
	{
		_needsRebuild = true;
		if (!IsInsideTree() || _rebuildDeferred)
			return;

		_rebuildDeferred = true;
		CallDeferred(nameof(RebuildIfNeeded));
	}

	private void RebuildIfNeeded()
	{
		_rebuildDeferred = false;
		if (!_needsRebuild)
			return;

		_needsRebuild = false;
		Rebuild();
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
		label.AddThemeConstantOverride("line_separation", 2);
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
		var popupRoot = EnsurePopupRoot();
		if (popupRoot == null)
			return;

		float y = 0;
		float contentWidth = Mathf.Max(1, PanelSize.X - Padding * 2);
		foreach (var child in popupRoot.GetChildren())
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
		popupRoot.CustomMinimumSize = CustomMinimumSize;
		popupRoot.Size = Size;
		popupRoot.GlobalPosition = GetTooltipScreenPosition().Floor();
	}

	private Vector2 GetTooltipScreenPosition()
	{
		Vector2 position = GetGlobalTransformWithCanvas().Origin;
		Vector2 viewportSize = GetViewportRect().Size;
		float margin = Mathf.Max(0, ScreenMargin);

		if (viewportSize.X > 0f && Size.X > 0f)
			position.X = Mathf.Clamp(position.X, margin, Mathf.Max(margin, viewportSize.X - Size.X - margin));

		if (viewportSize.Y > 0f && Size.Y > 0f)
			position.Y = Mathf.Clamp(position.Y, margin, Mathf.Max(margin, viewportSize.Y - Size.Y - margin));

		return position;
	}

	private Control EnsurePopupRoot()
	{
		if (_popupRoot != null && GodotObject.IsInstanceValid(_popupRoot))
			return _popupRoot;

		var tooltipRoot = EnsureTooltipRoot();
		if (tooltipRoot == null)
			return null;

		_popupRoot = new Control
		{
			Name = $"{Name}Popup",
			MouseFilter = MouseFilterEnum.Ignore,
			Visible = false,
			ZAsRelative = false,
			ZIndex = TooltipZIndex
		};
		tooltipRoot.AddChild(_popupRoot);
		return _popupRoot;
	}

	private Control EnsureTooltipRoot()
	{
		if (_tooltipRoot != null && GodotObject.IsInstanceValid(_tooltipRoot))
		{
			if (_tooltipLayer != null && GodotObject.IsInstanceValid(_tooltipLayer))
				_tooltipLayer.Layer = TooltipCanvasLayer;
			_tooltipRoot.ZIndex = TooltipZIndex;
			return _tooltipRoot;
		}

		var tree = GetTree();
		if (tree == null)
			return null;

		var root = tree.Root;
		_tooltipLayer = root.GetNodeOrNull<CanvasLayer>(TooltipLayerName);
		if (_tooltipLayer == null || !GodotObject.IsInstanceValid(_tooltipLayer))
		{
			_tooltipLayer = new CanvasLayer
			{
				Name = TooltipLayerName,
				Layer = TooltipCanvasLayer
			};
			root.AddChild(_tooltipLayer);
		}
		else
		{
			_tooltipLayer.Layer = TooltipCanvasLayer;
		}

		_tooltipRoot = _tooltipLayer.GetNodeOrNull<Control>(TooltipRootName);
		if (_tooltipRoot == null || !GodotObject.IsInstanceValid(_tooltipRoot))
		{
			_tooltipRoot = new Control
			{
				Name = TooltipRootName,
				MouseFilter = MouseFilterEnum.Ignore,
				ZAsRelative = false,
				ZIndex = TooltipZIndex
			};
			_tooltipLayer.AddChild(_tooltipRoot);
		}

		return _tooltipRoot;
	}

	private void SetTipsVisible(bool visible)
	{
		Visible = visible;

		if (_popupRoot == null || !GodotObject.IsInstanceValid(_popupRoot))
			return;

		_popupRoot.Visible = visible && _entries.Count > 0;
		if (_popupRoot.Visible && _popupRoot.GetParent() is Node parent)
			parent.MoveChild(_popupRoot, parent.GetChildCount() - 1);
	}

	private string BuildTooltipText(TooltipEntry entry)
	{
		string icon = "";
		if (entry.Icon != null)
		{
			string iconBbcode = RichTextInlineIcon.Build(entry.Icon, IconWidth, IconHeight, IconAlign);
			if (!string.IsNullOrEmpty(iconBbcode))
				icon = $"{iconBbcode} ";
		}

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
