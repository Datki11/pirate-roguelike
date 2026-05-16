// BaseCardView.cs
using Godot;

public abstract partial class BaseCardView : Control
{
	public abstract void SetData(CardData data);

	public virtual void SetTargetingFocus(bool focused) { }
}
