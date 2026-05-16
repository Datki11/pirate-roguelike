using Godot;

public static class RichTextInlineIcon
{
	public const int DefaultSize = 16;
	public const int DefaultWidth = 16;
	public const int DefaultHeight = 18;
	public const string DefaultAlign = "center,center";

	public static string Build(Texture2D texture, int width = DefaultWidth, int height = DefaultHeight, string align = DefaultAlign)
	{
		if (texture == null || string.IsNullOrWhiteSpace(texture.ResourcePath))
			return "";

		return Build(texture.ResourcePath, width, height, align);
	}

	public static string Build(string path, int width = DefaultWidth, int height = DefaultHeight, string align = DefaultAlign)
	{
		if (string.IsNullOrWhiteSpace(path))
			return "";

		int iconWidth = Mathf.Max(1, width);
		int iconHeight = Mathf.Max(1, height);
		string iconAlign = string.IsNullOrWhiteSpace(align) ? DefaultAlign : align;
		return $"[img width={iconWidth} height={iconHeight} align={iconAlign}]{path}[/img]";
	}
}
