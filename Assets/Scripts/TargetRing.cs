using Godot;

public partial class TargetRing : Control
{
	[Export] public Color BaseColor   = new Color(0.9f, 0.2f, 0.2f, 1f);
	[Export] public Color ShadowColor = new Color(0.15f, 0.05f, 0.05f, 0.9f);
	[Export] public Color FillColor   = new Color(0.9f, 0.2f, 0.2f, 0.25f); // hover fill alpha

	[Export(PropertyHint.Range, "1,6,1")]   public int Thickness = 2;
	[Export(PropertyHint.Range, "12,96,1")] public int Segments  = 48;
	[Export] public Vector2 ShadowOffset    = new Vector2(0, 1);
	[Export(PropertyHint.Range, "0.3,1.0,0.01")]
	public float VerticalSquash = 0.55f;

	[Export] public bool Pulse = true;
	[Export(PropertyHint.Range, "0,0.4,0.01")] public float PulseAmount = 0.12f;
	[Export(PropertyHint.Range, "0.5,8,0.1")]  public float PulseSpeed  = 2.8f;

	[Export] public bool HoverEnabled = true; // ring reacts to hover
	[Export] public bool HoverFill    = true; // fill interior on hover
	[Export] public bool UseOwnHover  = false; // if true, ring listens to its own mouse enter/exit

	private float _t;
	private bool _hover;

	public override void _Ready()
	{
		if (UseOwnHover)
		{
			MouseFilter = MouseFilterEnum.Pass; // allow hover without eating clicks
			MouseEntered += () => SetHover(true);
			MouseExited  += () => SetHover(false);
		}
		// crisp pixels
		Position = Position.Floor();
		Size     = Size.Floor();
	}

	public void SetHover(bool on)
	{
		if (!HoverEnabled) on = false;
		if (_hover == on) return;
		_hover = on;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (Visible && Pulse)
		{
			_t += (float)delta * PulseSpeed;
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		var r = GetRect();
		var center = r.Size / 2f;

		float rx = (r.Size.X - 1) * 0.5f;
		float ry = rx * VerticalSquash;
		if (rx < 1 || ry < 1) return;

		float mul = 1f + (Pulse ? Mathf.Sin(_t) * PulseAmount : 0f);

		// subtle shadow
		DrawEllipse(center + ShadowOffset, rx * mul, ry * mul, Thickness, ShadowColor);

		// optional fill on hover
		if (_hover && HoverFill)
		{
			var fillPts = BuildEllipse(center, (rx - 1) * mul, (ry - 1) * mul);
			if (fillPts.Length >= 3)
			{
				var cols = new Color[fillPts.Length];
				for (int i = 0; i < cols.Length; i++) cols[i] = FillColor;
				DrawPolygon(fillPts, cols); // full ellipse = safe (no holes)
			}
		}

		// main ring (slightly thicker / brighter on hover)
		var ringColor = _hover ? BaseColor.Lightened(0.15f) : BaseColor;
		int thick = _hover ? Mathf.Min(Thickness + 1, 6) : Thickness;

		DrawEllipse(center, rx * mul, ry * mul, thick, ringColor);
	}

	private void DrawEllipse(Vector2 c, float rx, float ry, int thick, Color col)
	{
		for (int t = 0; t < thick; t++)
		{
			var pts = BuildEllipse(c, rx - t, ry - t);
			if (pts.Length < 3) break;
			DrawPolyline(pts, col, 1.0f, true); // AA off, width=1
		}
	}

	private Vector2[] BuildEllipse(Vector2 c, float rx, float ry)
	{
		if (rx <= 0 || ry <= 0) return System.Array.Empty<Vector2>();
		int n = Mathf.Max(12, Segments);
		var arr = new Vector2[n + 1];
		for (int i = 0; i <= n; i++)
		{
			float a = Mathf.Tau * i / n;
			arr[i] = new Vector2(c.X + rx * Mathf.Cos(a), c.Y + ry * Mathf.Sin(a)).Floor();
		}
		return arr;
	}
}
