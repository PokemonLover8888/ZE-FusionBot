using FluentAssertions;
using SysBot.Pokemon;
using Xunit;

namespace SysBot.Tests;

public class StringTests
{
    [Theory]
    [InlineData("Anubis", "anubis")]
    [InlineData("Anu_bis", "anubis")]
    [InlineData("Anu_bis_12", "anubis12")]
    [InlineData("a-zA-Z0-9ａ-ｚＡ-Ｚ０-９", "azaz09azaz09")]
    public void TestSanitize(string input, string output)
    {
        var result = StringsUtil.Sanitize(input);
        result.Should().Be(output);
    }

    [Theory]
    // Spam-name blocking is intentionally disabled (IsSpammyString always returns false) so members
    // can use any OT/nickname. These formerly-"spammy" names are now allowed — expected: false.
    [InlineData("Anubis", false)]
    [InlineData("Kurt", false)]
    [InlineData("NzHateTV", false)]
    [InlineData("tvOakSlab", false)]
    public void TestSpammy(string input, bool state)
    {
        var result = StringsUtil.IsSpammyString(input);
        result.Should().Be(state);
    }
}
