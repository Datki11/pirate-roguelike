using Godot;

public readonly record struct TooltipEntry(
	string Title,
	string Text,
	Texture2D Icon = null,
	Color? TitleColor = null
);
