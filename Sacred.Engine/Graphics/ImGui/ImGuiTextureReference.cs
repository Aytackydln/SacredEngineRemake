namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Positive descriptor slots use ordinary alpha; negative slots request additive RGB.
/// A zero-alpha shader result supplies ONE/ONE color through the premultiplied UI blend.</summary>
internal static class ImGuiTextureReference
{
    public static nint Additive(int slot) => -checked((nint)slot);
    public static int Slot(nint reference) => checked((int)(reference < 0 ? -reference : reference));
}
