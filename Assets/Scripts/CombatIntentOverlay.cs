using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CombatIntentOverlay : Control
{
	[Export] public CombatManager Combat { get; set; }
	[Export] public Color FaintLineColor { get; set; } = new(1f, 0.05f, 0.04f, 0.42f);
	[Export] public Color LineColor { get; set; } = new(1f, 0.02f, 0.02f, 1f);
	[Export] public float HighlightLineWidth { get; set; } = 2f;
	[Export] public int Segments { get; set; } = 18;

	private readonly HashSet<PlayerUnit> _previewPlayers = new();

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		Vector2 viewportSize = GetViewportRect().Size;
		if (Size != viewportSize)
			Size = viewportSize;

		UpdateIntentTargetPreviews();
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (Combat == null || !Visible)
			return;

		var groups = Combat.GetEnemyIntentLines()
			.Where(line => line?.SourceEnemy != null)
			.GroupBy(line => line.SourceEnemy)
			.OrderBy(group => group.Key.GetIntentSourceAnchorCanvas().X)
			.ToList();

		for (int i = 0; i < groups.Count; i++)
			DrawIntentGroup(groups[i].Key, groups[i].ToList(), i, groups.Count);
	}

	private void UpdateIntentTargetPreviews()
	{
		if (Combat == null || !Visible)
		{
			ClearIntentTargetPreviews();
			return;
		}

		var nextPlayers = Combat.GetEnemyIntentLines()
			.Where(line => line?.SourceEnemy != null && line.SourceEnemy.IsIntentHovering() && line.TargetPlayer != null)
			.Select(line => line.TargetPlayer)
			.Distinct()
			.ToHashSet();

		foreach (var player in _previewPlayers.Where(player => !nextPlayers.Contains(player)).ToList())
		{
			if (player != null && GodotObject.IsInstanceValid(player))
				player.SetIntentTargetPreview(false);
			_previewPlayers.Remove(player);
		}

		foreach (var player in nextPlayers)
		{
			if (player == null || !GodotObject.IsInstanceValid(player))
				continue;

			player.SetIntentTargetPreview(true);
			_previewPlayers.Add(player);
		}
	}

	public void ClearIntentTargetPreviews()
	{
		foreach (var player in _previewPlayers.ToList())
		{
			if (player != null && GodotObject.IsInstanceValid(player))
				player.SetIntentTargetPreview(false);
		}
		_previewPlayers.Clear();
	}

	private void DrawIntentGroup(Enemy enemy, List<CombatIntentLine> lines, int enemyIndex, int enemyCount)
	{
		if (enemy == null || !GodotObject.IsInstanceValid(enemy) || lines.Count == 0)
			return;

		bool highlighted = enemy.IsIntentHovering();
		if (!highlighted)
			return;

		float heightRank = enemyCount <= 1 ? 0f : enemyIndex / (float)(enemyCount - 1);

		foreach (var line in lines)
		{
			Vector2[] fullPath = BuildCurve(line.Source, line.Target, CurveLift(line.Source, line.Target, heightRank));
			DrawArrowPath(fullPath, LineColor, HighlightLineWidth, arrowHead: true);
		}
	}

	private float CurveLift(Vector2 start, Vector2 end, float heightRank)
	{
		float dx = Mathf.Abs(end.X - start.X);
		float baseLift = Mathf.Clamp(dx * 0.11f + 190f, 196f, 258f);
		return baseLift + heightRank * 200f;
	}

	private Vector2[] BuildCurve(Vector2 canvasStart, Vector2 canvasEnd, float lift)
	{
		Vector2 start = ToOverlayLocal(canvasStart);
		Vector2 end = ToOverlayLocal(canvasEnd);
		int segmentCount = Mathf.Max(6, Segments);
		var points = new Vector2[segmentCount + 1];
		float topY = Mathf.Min(start.Y, end.Y) - lift;
		Vector2 c1 = new(Mathf.Lerp(start.X, end.X, 0.34f), topY);
		Vector2 c2 = new(Mathf.Lerp(start.X, end.X, 0.72f), topY);

		for (int i = 0; i <= segmentCount; i++)
		{
			float t = i / (float)segmentCount;
			points[i] = Cubic(start, c1, c2, end, t).Floor();
		}

		return points;
	}

	private Vector2 ToOverlayLocal(Vector2 canvasPoint)
		=> (GetGlobalTransformWithCanvas().AffineInverse() * canvasPoint).Floor();

	private static Vector2 Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
	{
		float u = 1f - t;
		return u * u * u * a
			+ 3f * u * u * t * b
			+ 3f * u * t * t * c
			+ t * t * t * d;
	}

	private void DrawArrowPath(Vector2[] points, Color color, float width, bool arrowHead)
	{
		if (points.Length < 2)
			return;

		float pixelWidth = Mathf.Max(1f, Mathf.Round(width));
		DrawPolyline(points, color, pixelWidth, antialiased: false);

		if (arrowHead)
			DrawArrowHead(points[^2], points[^1], color, pixelWidth);
	}

	private void DrawArrowHead(Vector2 from, Vector2 tip, Color color, float width)
	{
		Vector2 direction = (tip - from).Normalized();
		if (direction == Vector2.Zero)
			direction = Vector2.Down;

		Vector2 normal = new(-direction.Y, direction.X);
		float length = 8f + width;
		float halfWidth = 5f + width * 0.5f;
		Vector2 basePoint = tip - direction * length;
		Vector2[] head =
		{
			tip.Floor(),
			(basePoint + normal * halfWidth).Floor(),
			(basePoint - normal * halfWidth).Floor()
		};

		DrawColoredPolygon(head, color);
	}
}
