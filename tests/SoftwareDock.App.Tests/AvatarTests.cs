using SoftwareDock.App.ViewModels;

namespace SoftwareDock.App.Tests;

public class AvatarTests
{
    [Theory]
    [InlineData("Visual Studio Code", "VS")]
    [InlineData("Git", "GI")]
    [InlineData("7-Zip 24.08 (x64)", "7Z")]
    [InlineData("Notepad++ (64-bit x64)", "N6")]
    [InlineData("", "?")]
    public void Initials(string name, string expected) => Assert.Equal(expected, Avatar.Initials(name));

    [Fact]
    public void Colour_IsStableForAName() => Assert.Same(Avatar.BrushFor("Git"), Avatar.BrushFor("Git"));
}
