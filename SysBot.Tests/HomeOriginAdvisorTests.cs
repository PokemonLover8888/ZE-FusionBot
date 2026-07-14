using FluentAssertions;
using PKHeX.Core;
using SysBot.Pokemon;
using SysBot.Pokemon.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace SysBot.Tests;

public class HomeOriginAdvisorTests
{
    private readonly ITestOutputHelper _out;
    public HomeOriginAdvisorTests(ITestOutputHelper o) => _out = o;
    static HomeOriginAdvisorTests() => AutoLegalityWrapper.EnsureInitialized(new Pokemon.LegalitySettings());

    [Fact]
    public void EternatusFromAnSvBot_IsDeclinedAndPointsAtSwSh()
    {
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var pk = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet("Eternatus\nShiny: Yes")), out _);

        HomeOriginAdvisor.IsNativeToBot(pk).Should().BeFalse("Eternatus is a Sword/Shield event, not an SV native");

        var msg = HomeOriginAdvisor.BuildDeclineMessage(pk, "Eternatus", "Scarlet/Violet");
        _out.WriteLine(msg);
        msg.Should().Contain("Sword/Shield");
        msg.Should().Contain("Celebi-SWSH");
    }

    [Fact]
    public void EternatusFromASwShBot_IsFine()
    {
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK8>();
        var pk = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet("Eternatus\nShiny: Yes")), out _);

        HomeOriginAdvisor.IsNativeToBot(pk).Should().BeTrue("a SwSh bot CAN make a HOME-ready Eternatus");
        pk.IsShiny.Should().BeTrue();
    }

    [Fact]
    public void GoOnlyPokemon_SendsThemToTheArchive()
    {
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var pk = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet("Diancie\nShiny: Yes")), out _);

        HomeOriginAdvisor.IsNativeToBot(pk).Should().BeFalse();
        var msg = HomeOriginAdvisor.BuildDeclineMessage(pk, "Diancie", "Scarlet/Violet");
        _out.WriteLine(msg);
        msg.Should().Contain("Archives");
    }

    [Theory]
    [InlineData("Garchomp")]
    [InlineData("Gengar")]
    public void OrdinarySvPokemon_IsNativeAndNotDeclined(string species)
    {
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var pk = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet($"{species}\nShiny: Yes")), out _);
        HomeOriginAdvisor.IsNativeToBot(pk).Should().BeTrue($"{species} is obtainable in SV — must NOT be declined");
    }
}
