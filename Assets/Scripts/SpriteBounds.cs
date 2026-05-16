using Godot;
using System.Collections.Generic;

public static class SpriteBounds
{
	private const float OpaqueAlphaThreshold = 0.05f;
	private static readonly Dictionary<Texture2D, Rect2> TextureRectCache = new();

	public static bool TryGetSprite2DRect(Sprite2D sprite, out Rect2 rect)
	{
		rect = default;
		if (sprite?.Texture == null)
			return false;

		return TryGetSprite2DRect(sprite.Texture, sprite.Position, sprite.Offset, sprite.Scale, sprite.Centered, sprite.FlipH, out rect);
	}

	public static bool TryGetSprite2DRect(Texture2D texture, Vector2 position, Vector2 offset, Vector2 scaleValue, bool centered, bool flipH, out Rect2 rect)
	{
		rect = default;
		if (texture == null)
			return false;

		Vector2 textureSize = texture.GetSize();
		if (textureSize.X <= 0f || textureSize.Y <= 0f)
			return false;

		Rect2 textureRect = GetOpaqueTextureRectOrFull(texture, textureSize);
		if (flipH)
			textureRect.Position = new Vector2(textureSize.X - textureRect.End.X, textureRect.Position.Y);

		Vector2 scale = scaleValue.Abs();
		Vector2 topLeft = position + offset * scale;
		if (centered)
			topLeft -= textureSize * scale * 0.5f;

		rect = new Rect2(
			topLeft + textureRect.Position * scale,
			textureRect.Size * scale
		);
		return true;
	}

	public static bool TryGetTextureRect(TextureRect sprite, out Rect2 rect)
	{
		rect = default;
		if (sprite == null)
			return false;

		return TryGetTextureRect(sprite.Texture, sprite.Position, sprite.Size, sprite.FlipH, out rect);
	}

	public static bool TryGetTextureRect(Texture2D texture, Vector2 position, Vector2 controlSize, bool flipH, out Rect2 rect)
	{
		rect = default;
		Vector2 textureSize = texture?.GetSize() ?? Vector2.Zero;
		if ((controlSize.X <= 0f || controlSize.Y <= 0f) && textureSize.X > 0f && textureSize.Y > 0f)
			controlSize = textureSize;
		if (controlSize.X <= 0f || controlSize.Y <= 0f)
			return false;
		if (texture == null || textureSize.X <= 0f || textureSize.Y <= 0f)
		{
			rect = new Rect2(position, controlSize);
			return true;
		}

		Rect2 textureRect = GetOpaqueTextureRectOrFull(texture, textureSize);
		if (flipH)
			textureRect.Position = new Vector2(textureSize.X - textureRect.End.X, textureRect.Position.Y);

		Vector2 scale = new(controlSize.X / textureSize.X, controlSize.Y / textureSize.Y);
		rect = new Rect2(
			position + textureRect.Position * scale,
			textureRect.Size * scale
		);
		return true;
	}

	private static Rect2 GetOpaqueTextureRectOrFull(Texture2D texture, Vector2 textureSize)
	{
		if (TextureRectCache.TryGetValue(texture, out Rect2 cachedRect))
			return cachedRect;

		Rect2 textureRect = TryGetOpaqueTextureRect(texture, out Rect2 opaqueRect)
			? opaqueRect
			: new Rect2(Vector2.Zero, textureSize);
		TextureRectCache[texture] = textureRect;
		return textureRect;
	}

	private static bool TryGetOpaqueTextureRect(Texture2D texture, out Rect2 rect)
	{
		rect = default;
		if (texture == null)
			return false;

		Image image;
		Rect2 sourceRect;
		if (texture is AtlasTexture atlas && atlas.Atlas != null)
		{
			image = atlas.Atlas.GetImage();
			sourceRect = atlas.Region;
			if (sourceRect.Size.X <= 0f || sourceRect.Size.Y <= 0f)
				sourceRect = new Rect2(Vector2.Zero, texture.GetSize());
		}
		else
		{
			image = texture.GetImage();
			sourceRect = new Rect2(Vector2.Zero, texture.GetSize());
		}

		if (image == null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
			return false;
		if (image.IsCompressed() && image.Decompress() != Error.Ok)
			return false;

		int x0 = Mathf.Clamp(Mathf.FloorToInt(sourceRect.Position.X), 0, image.GetWidth());
		int y0 = Mathf.Clamp(Mathf.FloorToInt(sourceRect.Position.Y), 0, image.GetHeight());
		int x1 = Mathf.Clamp(Mathf.CeilToInt(sourceRect.End.X), x0, image.GetWidth());
		int y1 = Mathf.Clamp(Mathf.CeilToInt(sourceRect.End.Y), y0, image.GetHeight());
		if (x1 <= x0 || y1 <= y0)
			return false;

		int minX = x1;
		int minY = y1;
		int maxX = x0 - 1;
		int maxY = y0 - 1;

		for (int y = y0; y < y1; y++)
		{
			for (int x = x0; x < x1; x++)
			{
				if (image.GetPixel(x, y).A <= OpaqueAlphaThreshold)
					continue;

				if (x < minX) minX = x;
				if (y < minY) minY = y;
				if (x > maxX) maxX = x;
				if (y > maxY) maxY = y;
			}
		}

		if (maxX < minX || maxY < minY)
			return false;

		rect = new Rect2(
			new Vector2(minX - x0, minY - y0),
			new Vector2(maxX - minX + 1, maxY - minY + 1)
		);
		return true;
	}
}
