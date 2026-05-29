namespace SysBot.Pokemon.Discord;

public class TradeEmbedData
{
    public string SpeciesName { get; set; } = "";
    public ushort SpeciesId { get; set; }
    public string FormName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public bool IsMysteryEgg { get; set; }
    public bool IsShiny { get; set; }
    public string ThumbnailUrl { get; set; } = "";
    public int Type1 { get; set; }
    public int Type2 { get; set; }
    public int[] IVs { get; set; } = new int[6];
    public int[] EVs { get; set; } = new int[6];
    public bool UsesAVs { get; set; }
    public string AbilityName { get; set; } = "";
    public string[] MoveNames { get; set; } = new string[4];
    public string NatureName { get; set; } = "";
    public int Level { get; set; }
    public string BallName { get; set; } = "";
    public string HeldItem { get; set; } = "";
    public bool IsLegendary { get; set; }
    public bool IsMythical { get; set; }
    public bool IsSubLegendary { get; set; }
    public bool IsParadox { get; set; }
    public bool IsAlpha { get; set; }
    public string OriginalTrainer { get; set; } = "";
    public int Gender { get; set; }
    public int BatchTradeNumber { get; set; }
    public int TotalBatchTrades { get; set; }
    public int TradeCode { get; set; }
    public ulong UserId { get; set; }
    public int UniqueTradeId { get; set; }
    public string GameType { get; set; } = "";
    public string BotName { get; set; } = "";
    public string DisplayName => !string.IsNullOrEmpty(FormName) ? $"{SpeciesName}-{FormName}" : SpeciesName;
}
