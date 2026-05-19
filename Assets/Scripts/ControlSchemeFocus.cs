using Godot;

public static class ControlSchemeFocus
{
	private const float ControllerAxisThreshold = 0.55f;

	public enum Scheme
	{
		KeyboardMouse,
		Xbox
	}

	private static Scheme _focusedScheme = Scheme.KeyboardMouse;
	private static ulong _revision;

	public static Scheme FocusedScheme => _focusedScheme;
	public static bool IsXboxFocused => _focusedScheme == Scheme.Xbox;
	public static ulong Revision => _revision;

	public static void UpdateFromInput(InputEvent e)
	{
		if (IsControllerInput(e))
			SetFocusedScheme(Scheme.Xbox);
		else if (IsKeyboardMouseInput(e))
			SetFocusedScheme(Scheme.KeyboardMouse);
	}

	private static void SetFocusedScheme(Scheme scheme)
	{
		if (_focusedScheme == scheme)
			return;

		_focusedScheme = scheme;
		_revision++;
	}

	private static bool IsControllerInput(InputEvent e)
	{
		if (e is InputEventJoypadButton button)
			return button.Pressed;

		return e is InputEventJoypadMotion motion && Mathf.Abs(motion.AxisValue) >= ControllerAxisThreshold;
	}

	private static bool IsKeyboardMouseInput(InputEvent e)
	{
		if (e is InputEventKey key)
			return key.Pressed && !key.Echo;

		if (e is InputEventMouseMotion motion)
			return motion.Relative.LengthSquared() > 0f;

		return e is InputEventMouseButton button && button.Pressed;
	}
}
