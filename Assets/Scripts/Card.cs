using Godot;

public partial class Card : BaseCardView
{
	// Drag the child nodes here in the Inspector (safer than hard-coded paths)
	[Export] public NodePath TitlePath { get; set; }
	[Export] public NodePath ArtPath { get; set; }
	[Export] public NodePath BodyPath { get; set; }

	// Optional direct exports if you want to set values without a CardData
	[Export] public string TitleText { get; set; } = "";
	[Export(PropertyHint.MultilineText)] public string DescriptionText { get; set; } = "";
	[Export] public Texture2D ArtTexture { get; set; }

	// Data-driven option
	[Export]
	public CardData Data
	{
		get => _data;
		set { _data = value; if (IsInsideTree()) ApplyData(_data); }
	}

	private CardData _data;

	private Label _title;
	private TextureRect _art;
	private RichTextLabel _body;
	
	
	public override void SetData(CardData data)
	{
		Data = data;
		ApplyData(data);
	}

	public override void _Ready()
	{
		_title = GetNode<Label>(TitlePath);
		_art = GetNode<TextureRect>(ArtPath);
		_body = GetNode<RichTextLabel>(BodyPath);

		// Apply direct exports first (lets you preview without a CardData)
		if (!string.IsNullOrEmpty(TitleText)) _title.Text = TitleText;
		if (!string.IsNullOrEmpty(DescriptionText)) _body.Text = DescriptionText;
		if (ArtTexture != null) _art.Texture = ArtTexture;

		// If a CardData was assigned, it wins
		if (_data != null) ApplyData(_data);

		// Ensures text is visible if your theme is light-on-light
		if (!_body.HasThemeColorOverride("default_color"))
			_body.AddThemeColorOverride("default_color", Colors.Black);
	}

	private void ApplyData(CardData d)
	{
		if (d == null) return;

		_title.Text = d.Title ?? "";
		_body.Text = d.Description ?? "";
		_art.Texture = d.Art;

		// Example: tint frame by rarity (optional)
		// var frame = GetNode<Panel>("Frame");
		// var sb = frame.GetThemeStylebox("panel") as StyleBoxFlat;
		// if (sb != null) sb.BorderColor = d.Rarity switch
		// {
		//     Rarity.Common    => new Color("808080"),
		//     Rarity.Uncommon  => new Color("2ecc71"),
		//     Rarity.Rare      => new Color("3498db"),
		//     Rarity.Legendary => new Color("f1c40f"),
		//     _ => Colors.Gray
		// };
	}
}
