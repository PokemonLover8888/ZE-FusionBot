using PKHeX.Core;

namespace SysBot.Pokemon.Helpers;

/// <summary>
/// Works out which bot a member should have asked, when the one they asked cannot produce a
/// HOME-acceptable copy of what they requested.
///
/// HOME issues a Pokemon its tracker on its FIRST upload from the game it was born in. A Pokemon whose
/// origin game is NOT the game this bot runs could only have got here by passing through HOME already,
/// so HOME expects it to carry a real tracker. Ours has none, and a fabricated one is a forgery HOME
/// rejects outright (2-ALZTA-0005 / 10015). Either way the deposit fails.
///
/// The fix isn't code — it's asking the right bot. Eternatus is a Sword/Shield event: on a SwSh bot it
/// is native, so HOME assigns it a real tracker on upload and it works every time, unlimited. From an
/// SV bot the same Eternatus is a transfer, and HOME refuses it. Nothing about the Pokemon is wrong; it
/// came through the wrong door. So tell the member which door.
/// </summary>
public static class HomeOriginAdvisor
{
    /// <summary>Is this Pokemon's origin game the same game this bot trades in?</summary>
    public static bool IsNativeToBot(PKM pk) => pk switch
    {
        PK9 => pk.Version is GameVersion.SL or GameVersion.VL,
        PK8 => pk.Version is GameVersion.SW or GameVersion.SH,
        PB8 => pk.Version is GameVersion.BD or GameVersion.SP,
        PA8 => pk.Version is GameVersion.PLA,
        PA9 => pk.Version is GameVersion.ZA,
        PB7 => pk.Version is GameVersion.GP or GameVersion.GE,
        _ => true,   // unknown format — say nothing rather than cry wolf
    };

    /// <summary>Which of our bots can natively produce a Pokemon from this origin game? Null if none can.</summary>
    public static string? BotFor(GameVersion origin) => origin switch
    {
        GameVersion.SW or GameVersion.SH => "**Sword/Shield** bots (Celebi-SWSH, Jirachi-SWSH)",
        GameVersion.BD or GameVersion.SP => "**BDSP** bots (Dialga, Giratina, Rayquaza)",
        GameVersion.SL or GameVersion.VL => "**Scarlet/Violet** bots (Mew-SV, Meloetta-SV)",
        GameVersion.PLA => "**Legends: Arceus** bots (Arceus-PLA, Landorus-PLA)",
        GameVersion.ZA => "**Legends: Z-A** bots (Diance, Floette, Hoopa, Groudon-ZA)",
        GameVersion.GP or GameVersion.GE => "**Let's Go** bots (Flareon-LGPE, Glaceon-LGPE)",
        _ => null,   // Pokemon GO, or an older generation — no bot runs that game
    };

    public static string DescribeVersion(GameVersion v) => v switch
    {
        GameVersion.SW or GameVersion.SH => "Sword/Shield",
        GameVersion.BD or GameVersion.SP => "Brilliant Diamond/Shining Pearl",
        GameVersion.SL or GameVersion.VL => "Scarlet/Violet",
        GameVersion.PLA => "Legends: Arceus",
        GameVersion.ZA => "Legends: Z-A",
        GameVersion.GP or GameVersion.GE => "Let's Go",
        GameVersion.GO => "Pokémon GO",
        _ => v.ToString(),
    };

    /// <summary>
    /// The message shown when a bot declines a request it cannot make HOME-acceptable. Names the exact
    /// bot to ask, rather than leaving the member to guess.
    /// </summary>
    public static string BuildDeclineMessage(PKM pk, string speciesName, string thisGame)
    {
        var origin = pk.Version;
        var suggestion = BotFor(origin);

        var why =
            $"**{speciesName} can't be made HOME-ready by a {thisGame} bot.**\n" +
            $"It originates in **{DescribeVersion(origin)}**, and HOME only gives a Pokémon its tracker when " +
            $"it's uploaded from the game it was born in. Sent from here, HOME would reject it.\n\n";

        if (suggestion is not null)
            return why +
                   $"Request it from the {suggestion} instead — it'll go into HOME every time, " +
                   $"and you can get as many as you like.";

        // No bot runs its origin game (Pokemon GO, or a Gen 6/7 event). The archive is the only route.
        return why +
               $"None of our bots run {DescribeVersion(origin)}, so this one can only come from the " +
               $"**PKM Universe Archives** bot — try `/archive`. Those are real Pokémon with genuine HOME " +
               $"trackers, but each one is one-of-a-kind, so stock is limited.";
    }
}
