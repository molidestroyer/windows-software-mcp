using SoftwareDock.App.Shell;

namespace SoftwareDock.App.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Win+Shift+I", HotkeyModifiers.Windows | HotkeyModifiers.Shift, 'I', "Win+Shift+I")]
    [InlineData("shift + win + i", HotkeyModifiers.Windows | HotkeyModifiers.Shift, 'I', "Win+Shift+I")]
    [InlineData("Ctrl+Alt+F12", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x7B, "Ctrl+Alt+F12")]
    [InlineData("Alt+Space", HotkeyModifiers.Alt, 0x20, "Alt+Space")]
    public void TryParse_ValidGestures(string text, HotkeyModifiers modifiers, int virtualKey, string display)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(new HotkeyGesture(modifiers, virtualKey, display), gesture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I")]
    [InlineData("Ctrl+Ctrl+I")]
    [InlineData("Hyper+I")]
    [InlineData("Ctrl+Shift+Banana")]
    public void TryParse_Invalid(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void Modifiers_MatchWin32ModConstants()
    {
        // RegisterHotKey's MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN.
        Assert.Equal(0x1, (int)HotkeyModifiers.Alt);
        Assert.Equal(0x2, (int)HotkeyModifiers.Control);
        Assert.Equal(0x4, (int)HotkeyModifiers.Shift);
        Assert.Equal(0x8, (int)HotkeyModifiers.Windows);
    }
}
