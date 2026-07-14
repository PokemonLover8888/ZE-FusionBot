using FluentAssertions;
using PKHeX.Core;
using SysBot.Pokemon;
using Xunit;

namespace SysBot.Tests;

public class NatureLockTests
{
    static NatureLockTests() => AutoLegalityWrapper.EnsureInitialized(new Pokemon.LegalitySettings());

    /// <summary>
    /// Nature-locked encounters (Bloodmoon Ursaluna can only be Hardy) must keep their actual Nature
    /// and show a requested nature as a mint. Writing the request onto the actual Nature makes the mon
    /// illegal, which voids its HOME tracker and gets the deposit rejected.
    /// </summary>
    [Theory]
    [InlineData(Nature.Modest)]
    [InlineData(Nature.Adamant)]
    [InlineData(Nature.Timid)]
    public void NatureLockedEncounter_IsMinted_NotOverwritten(Nature requested)
    {
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var set = new ShowdownSet($"Ursaluna-Bloodmoon\n{requested} Nature");
        var pk = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(set), out _);

        pk.Nature.Should().Be(Nature.Hardy);
        pk.StatNature.Should().Be(requested);
        new LegalityAnalysis(pk).Valid.Should().BeTrue();
    }
}
