using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CombatIntentOverlay : Control
{
	[Export] public CombatManager Combat { get; set; }
	[Export] public Color FaintLineColor { get; set; } = new(1f, 0.05f, 0.04f, 0.42f);
	[Export] public Color LineColor { get; set; } = new(1f, 0.02f, 0.02f, 1f);
	[Export] public Color FriendlyLineColor { get; set; } = new(0.22f, 0.68f, 1f, 1f);
	[Export] public float HighlightLineWidth { get; set; } = 2f;
	[Export] public int Segments { get; set; } = 18;

	private readonly Dictionary<IDamageable, bool> _previewUnits = new();
	private readonly List<CombatIntentLine> _presentationLines = new();
	private float _presentationProgress = 1f;
	private bool _presentationActive;

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

		if (_presentationActive)
		{
			var lines = GetVisibleLines().Where(line => line != null).ToList();
			for (int i = 0; i < lines.Count; i++)
				DrawIntentLine(lines[i], i, lines.Count, forceVisible: true);
			return;
		}

		var groups = GetVisibleLines()
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

		var nextUnits = GetVisibleLines()
			.Where(line => line != null
				&& (_presentationActive || (line.SourceEnemy != null && line.SourceEnemy.IsIntentHovering()))
				&& line.TargetUnit != null)
			.GroupBy(line => line.TargetUnit)
			.ToDictionary(group => group.Key, group => group.All(line => line.Friendly));

		foreach (var unit in _previewUnits.Keys.Where(unit => !nextUnits.ContainsKey(unit)).ToList())
		{
			SetIntentTargetPreview(unit, false, friendly: false);
			_previewUnits.Remove(unit);
		}

		foreach (var kvp in nextUnits)
		{
			if (kvp.Key is not Node node || !GodotObject.IsInstanceValid(node))
				continue;

			if (_previewUnits.TryGetValue(kvp.Key, out bool friendly) && friendly == kvp.Value)
				continue;

			SetIntentTargetPreview(kvp.Key, true, kvp.Value);
			_previewUnits[kvp.Key] = kvp.Value;
		}
	}

	public void ClearIntentTargetPreviews()
	{
		foreach (var unit in _previewUnits.Keys.ToList())
			SetIntentTargetPreview(unit, false, friendly: false);
		_previewUnits.Clear();
	}

	public void SetPresentationLines(IEnumerable<CombatIntentLine> lines, float progress)
	{
		_presentationLines.Clear();
		if (lines != null)
			_presentationLines.AddRange(lines.Where(line => line != null));

		_presentationProgress = Mathf.Clamp(progress, 0f, 1f);
		_presentationActive = _presentationLines.Count > 0;
		QueueRedraw();
	}

	public void ClearPresentationLines()
	{
		_presentationLines.Clear();
		_presentationProgress = 1f;
		_presentationActive = false;
		ClearIntentTargetPreviews();
		QueueRedraw();
	}

	private IEnumerable<CombatIntentLine> GetVisibleLines()
		=> _presentationActive ? _presentationLines : Combat.GetEnemyIntentLines();

	private void SetIntentTargetPreview(IDamageable unit, bool on, bool friendly)
	{
		switch (unit)
		{
			case PlayerUnit player when GodotObject.IsInstanceValid(player):
				player.SetIntentTargetPreview(on, friendly);
				break;
			case Enemy enemy when GodotObject.IsInstanceValid(enemy):
				enemy.SetIntentTargetPreview(on, friendly);
				break;
		}
	}

	private void DrawIntentGroup(Enemy enemy, List<CombatIntentLine> lines, int enemyIndex, int enemyCount)
	{
		if (enemy == null || !GodotObject.IsInstanceValid(enemy) || lines.Count == 0)
			return;

		bool highlighted = _presentationActive || enemy.IsIntentHovering();
		if (!highlighted)
			return;

		float heightRank = enemyCount <= 1 ? 0f : enemyIndex / (float)(enemyCount - 1);

		foreach (var line in lines)
			DrawIntentLine(line, enemyIndex, enemyCount, forceVisible: false);
	}

	private void DrawIntentLine(CombatIntentLine line, int lineIndex, int lineCount, bool forceVisible)
	{
		if (line == null)
			return;

		if (!forceVisible && (line.SourceEnemy == null || !line.SourceEnemy.IsIntentHovering()))
			return;

		float heightRank = lineCount <= 1 ? 0f : lineIndex / (float)(lineCount - 1);
		Vector2[] fullPath = BuildCurve(line.Source, line.Target, CurveLift(line.Source, line.Target, heightRank));
		Vector2[] visiblePath = _presentationActive ? TrimPath(fullPath, _presentationProgress) : fullPath;
		DrawArrowPath(visiblePath, line.Friendly ? FriendlyLineColor : LineColor, HighlightLineWidth, arrowHead: true);
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

	private static Vector2[] TrimPath(Vector2[] points, float progress)
	{
		if (points == null || points.Length < 2)
			return System.Array.Empty<Vector2>();

		progress = Mathf.Clamp(progress, 0f, 1f);
		if (progress >= 0.999f)
			return points;

		float scaled = Mathf.Max(0.01f, progress) * (points.Length - 1);
		int lastWhole = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, points.Length - 2);
		float segmentT = scaled - lastWhole;
		var trimmed = new Vector2[lastWhole + 2];
		for (int i = 0; i <= lastWhole; i++)
			trimmed[i] = points[i];
		trimmed[^1] = points[lastWhole].Lerp(points[lastWhole + 1], segmentT).Floor();
		return trimmed;
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
