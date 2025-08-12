using Godot;
using Godot.Collections;

public partial class CardList : Control
{
	[Export] public NodePath RowPath { get; set; }          // <- HBoxContainer in your scene
	[Export] public PackedScene CardScene { get; set; }     // <- Card.tscn
	[Export] public Array<CardData> Cards { get; set; } = new();

	// Layout knobs
	[Export] public int BaselineSlots = 5;        // act like 5 must fit, always
	[Export] public int SidePadding = 24;         // L/R padding inside THIS Control
	[Export] public int Spacing = 12;             // gap between slots/cards
	[Export] public float AspectYOverX = 1.5f;    // 2:3 cards => 480/320 = 1.5
	[Export] public int MinCardWidth = 120;       // clamp so text isn’t microscopic

	private HBoxContainer _row;

	public override void _Ready()
	{
		_row = GetNode<HBoxContainer>(RowPath);
		_row.Alignment = BoxContainer.AlignmentMode.Begin;
		_row.AddThemeConstantOverride("separation", Spacing);

		// Make sure the row respects SidePadding (anchors full-rect, offsets as padding)
		_row.AnchorLeft = 0; _row.AnchorTop = 0; _row.AnchorRight = 1; _row.AnchorBottom = 1;
		_row.OffsetLeft = SidePadding; _row.OffsetRight = -SidePadding;
		_row.OffsetTop = 0; _row.OffsetBottom = 0;

		BuildSlots();
		UpdateLayout();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized)
			UpdateLayout();
	}

	private void BuildSlots()
	{
		// Clear existing
		foreach (var child in _row.GetChildren())
			(child as Node)?.QueueFree();

		// Count of slots to create: enough to show all cards, but at least BaselineSlots
		int slotCount = Mathf.Max(BaselineSlots, Cards.Count);

		for (int i = 0; i < slotCount; i++)
		{
			var slot = new Control
			{
				// Critical: NO horizontal expand; HBox will use our CustomMinimumSize as width
				SizeFlagsHorizontal = (Control.SizeFlags)0,
				SizeFlagsVertical = Control.SizeFlags.Fill,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};

			// If this slot corresponds to a real card, instance and fill it
			if (i < Cards.Count)
			{
				var card = CardScene.Instantiate<Card>();
				card.Data = Cards[i];

				// Card fills the slot without changing Card.tscn’s editor flags
				card.AnchorLeft = 0; card.AnchorTop = 0; card.AnchorRight = 1; card.AnchorBottom = 1;
				card.OffsetLeft = 0; card.OffsetTop = 0; card.OffsetRight = 0; card.OffsetBottom = 0;

				slot.AddChild(card);
			}

			_row.AddChild(slot);
		}
	}

	private void UpdateLayout()
	{
		if (_row == null || BaselineSlots <= 0)
			return;

		// Effective width inside padding (HBox is already padded via offsets)
		float rowWidth = 48;
		if (rowWidth <= 0) rowWidth = Size.X - (SidePadding * 2); // fallback

		// Compute width per slot as if exactly BaselineSlots must fit
		float gaps = Spacing * (BaselineSlots - 1);
		float usable = Mathf.Max(0, rowWidth - gaps);
		float w = Mathf.Floor(usable / BaselineSlots);
		w = Mathf.Max(w, MinCardWidth);

		Vector2 target = new(w, w * AspectYOverX);

		foreach (var child in _row.GetChildren())
		{
			if (child is Control slot)
				slot.CustomMinimumSize = target; // enough to pin the width
		}
	}
}
