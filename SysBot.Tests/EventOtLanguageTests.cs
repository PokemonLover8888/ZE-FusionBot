using FluentAssertions;
using PKHeX.Core;
using SysBot.Pokemon;
using System.Linq;
using Xunit;

namespace SysBot.Tests;

/// <summary>
/// Region-locked events carry a fixed OT in that region's script. A shiny Diancie, for example,
/// was only ever distributed in Korea and Japan, so its OT is genuinely '올스타'. Left on a Latin
/// language the mon is an English Pokemon holding Korean text in its OT field, which nothing can
/// render — it shows as "???". The trade path switches such a mon to a language that can display
/// its own OT, but only when PKHeX confirms the result is still legal.
/// </summary>
public class EventOtLanguageTests
{
    static EventOtLanguageTests() => AutoLegalityWrapper.EnsureInitialized(new Pokemon.LegalitySettings());

    [Fact]
    public void EveryShinyDiancieEvent_HasANonLatinOT()
    {
        // The premise: there is no Latin-script shiny Diancie event to fall back on, so a
        // Korean/Japanese OT on an event shiny Diancie is correct, not a bug.
        var shiny = EncounterEvent.GetAllEvents()
            .Where(e => e.Species == (ushort)Species.Diancie && e.Shiny == Shiny.Always)
            .OfType<WC6>()
            .ToList();

        shiny.Should().NotBeEmpty();
        shiny.Should().OnlyContain(w => w.OriginalTrainerName.Any(c => c >= 128));
    }

    [Theory]
    [InlineData("Garchomp\nJolly Nature")]
    [InlineData("Pikachu")]
    public void OrdinaryMon_KeepsItsLatinOT(string showdown)
    {
        // Guard: the language switch must only ever fire on non-Latin OTs. A normal trade
        // must come through with a plain Latin OT and remain legal.
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var pk = sav.GetLegalForTrade(AutoLegalityWrapper.GetTemplate(new ShowdownSet(showdown)), out _);

        pk.OriginalTrainerName.All(c => c < 128).Should().BeTrue();
        new LegalityAnalysis(pk).Valid.Should().BeTrue();
    }
}
