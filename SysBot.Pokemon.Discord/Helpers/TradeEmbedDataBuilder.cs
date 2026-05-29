using PKHeX.Core;
using System;
using System.Collections.Generic;

namespace SysBot.Pokemon.Discord;

public static class TradeEmbedDataBuilder
{
    private static readonly HashSet<ushort> ParadoxSpecies = new()
    {
        984, 985, 986, 987, 988, 989, 990, 991, 992, 993,
        994, 995, 1005, 1006, 1009, 1010, 1020, 1021, 1022, 1023
    };

    public static TradeEmbedData Build<T>(T pk, int tradeCode, ulong userId, int uniqueTradeId, int batchTradeNumber, int totalBatchTrades, bool isMysteryEgg, string botName = "") where T : PKM, new()
    {
        var strings = GameInfo.GetStrings("en");
        var data = new TradeEmbedData
        {
            SpeciesId = pk.Species,
            SpeciesName = strings.Species[pk.Species],
            Nickname = pk.IsNicknamed ? pk.Nickname : "",
            IsMysteryEgg = isMysteryEgg,
            IsShiny = pk.IsShiny,
            Type1 = pk.PersonalInfo.Type1,
            Type2 = pk.PersonalInfo.Type2,
            Gender = pk.Gender,
            Level = pk.CurrentLevel,
            NatureName = strings.natures[(int)pk.Nature],
            BallName = strings.balllist[pk.Ball],
            HeldItem = pk.HeldItem > 0 ? strings.itemlist[pk.HeldItem] : "",
            OriginalTrainer = pk.OriginalTrainerName,
            IsLegendary = SpeciesCategory.IsLegendary(pk.Species),
            IsMythical = SpeciesCategory.IsMythical(pk.Species),
            IsSubLegendary = SpeciesCategory.IsSubLegendary(pk.Species),
            IsParadox = ParadoxSpecies.Contains(pk.Species),
            BatchTradeNumber = batchTradeNumber,
            TotalBatchTrades = totalBatchTrades,
            TradeCode = tradeCode,
            UserId = userId,
            UniqueTradeId = uniqueTradeId,
            BotName = botName,
            GameType = typeof(T).Name
        };

        data.FormName = ShowdownParsing.GetStringFromForm(pk.Form, strings, pk.Species, pk.Context);

        Span<int> ivs = stackalloc int[6];
        pk.GetIVs(ivs);
        data.IVs = ivs.ToArray();

        int[] evs = new int[6];
        pk.GetEVs(evs);
        data.EVs = evs;

        data.UsesAVs = pk is PB7;
        if (pk is not PB7)
            data.AbilityName = strings.abilitylist[pk.Ability];

        ushort[] moves = new ushort[4];
        pk.GetMoves(moves.AsSpan());
        data.MoveNames = new string[4];
        for (int i = 0; i < 4; i++)
            data.MoveNames[i] = moves[i] > 0 ? strings.movelist[moves[i]] : "";

        if (pk is IAlpha alpha)
            data.IsAlpha = alpha.IsAlpha;

        // Thumbnail URL
        if (isMysteryEgg)
        {
            data.ThumbnailUrl = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/490.png";
        }
        else if (pk.Species > 0)
        {
            if (!pk.IsShiny && pk.Form == 0)
                data.ThumbnailUrl = $"https://creator.pkm-universe.com/assets/floating/{pk.Species}.gif";
            else
                data.ThumbnailUrl = GetHomeSpriteUrl(pk.Species, pk.Form, pk.IsShiny);
        }

        return data;
    }

    private static string GetHomeSpriteUrl(int species, int form, bool shiny)
    {
        string baseUrl = shiny
            ? "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/shiny/"
            : "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/";
        return $"{baseUrl}{species}.png";
    }
}
