using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace SysBot.Pokemon;
public class TradeCodeStorage
{
    private const string DefaultFileName = "tradecodes.json";
    // Multi-tenant: when several bots share a process, point each bot's trade-code storage at its
    // OWN folder so they don't clobber one shared file. AsyncLocal is set per-bot at MainLoop start;
    // when unset (a normal single-bot process) it falls back to the file in the working directory —
    // i.e. the exact existing behavior, so separate WinForms bots are unaffected.
    private static readonly System.Threading.AsyncLocal<string?> DataDir = new();
    public static void SetDataDirectory(string? dir) => DataDir.Value = dir;
    private static string FileName => string.IsNullOrEmpty(DataDir.Value)
        ? DefaultFileName
        : Path.Combine(DataDir.Value, DefaultFileName);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private Dictionary<ulong, TradeCodeDetails>? _tradeCodeDetails;
    public TradeCodeStorage() => LoadFromFile();
    public bool DeleteTradeCode(ulong trainerID)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.Remove(trainerID))
        {
            SaveToFile();
            return true;
        }
        return false;
    }
    public int GetTradeCode(ulong trainerID)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            // NOTE: do NOT increment TradeCount here. GetTradeCode runs at the START of every
            // $t/$bt command (to assign the link code), BEFORE the set is parsed/validated — so
            // counting here charged the user for format mistakes / illegal mons / blocked
            // requests too, breaking the "Mistakes are free — cooldowns only apply to completed
            // trades" promise. The count is now bumped only on actual completion via
            // IncrementCompletedTrades (called from DiscordTradeNotifier.TradeFinished).
            return details.Code;
        }
        var code = GenerateRandomTradeCode();
        _tradeCodeDetails![trainerID] = new TradeCodeDetails { Code = code, TradeCount = 0 };
        SaveToFile();
        return code;
    }
    /// <summary>Increment a user's trade count. Call ONLY when a trade successfully ENTERS the
    /// queue (QueueResultAdd.Added) — never at command submission — so format mistakes, illegal
    /// mons, blocked items and queue rejections don't count ("Mistakes are free").</summary>
    public int IncrementTradeCount(ulong trainerID)
    {
        LoadFromFile();
        if (!_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            details = new TradeCodeDetails { Code = GenerateRandomTradeCode(), TradeCount = 0 };
            _tradeCodeDetails[trainerID] = details;
        }
        details.TradeCount++;
        SaveToFile();
        return details.TradeCount;
    }
    public int GetTradeCount(ulong trainerID)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            return details.TradeCount;
        }
        return 0;
    }
    public TradeCodeDetails? GetTradeDetails(ulong trainerID)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            return details;
        }
        return null;
    }
    public void UpdateTradeDetails(ulong trainerID, string ot, int tid, int sid)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            details.OT = ot;
            details.TID = tid;
            details.SID = sid;
            SaveToFile();
        }
    }

    // New overload that includes gender and language parameters
    public void UpdateTradeDetails(ulong trainerID, string ot, int tid, int sid, byte? gender = null, int? language = null)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            details.OT = ot;
            details.TID = tid;
            details.SID = sid;

            // Only update if values are provided
            if (gender.HasValue)
                details.Gender = gender;

            if (language.HasValue)
                details.Language = language;

            SaveToFile();
        }
    }

    public bool UpdateTradeCode(ulong trainerID, int newCode)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            details.Code = newCode;
            SaveToFile();
            return true;
        }
        return false;
    }
    private static int GenerateRandomTradeCode()
    {
        var settings = new TradeSettings();
        return settings.GetRandomTradeCode();
    }
    private void LoadFromFile()
    {
        if (File.Exists(FileName))
        {
            string json = File.ReadAllText(FileName);
            _tradeCodeDetails = JsonSerializer.Deserialize<Dictionary<ulong, TradeCodeDetails>>(json, SerializerOptions);
        }
        else
        {
            _tradeCodeDetails = [];
        }
    }
    private void SaveToFile()
    {
        try
        {
            string json = JsonSerializer.Serialize(_tradeCodeDetails, SerializerOptions);
            File.WriteAllText(FileName, json);
        }
        catch (IOException ex)
        {
            LogUtil.LogInfo("TradeCodeStorage", $"Error saving trade codes to file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            LogUtil.LogInfo("TradeCodeStorage", $"Access denied while saving trade codes to file: {ex.Message}");
        }
        catch (Exception ex)
        {
            LogUtil.LogInfo("TradeCodeStorage", $"An error occurred while saving trade codes to file: {ex.Message}");
        }
    }

    public void UpdateTradeDetails(ulong trainerID, string ot, int tid, int sid, string? quote = null, byte? gender = null, int? language = null)
    {
        LoadFromFile();
        if (_tradeCodeDetails!.TryGetValue(trainerID, out var details))
        {
            details.OT = ot;
            details.TID = tid;
            details.SID = sid;

            if (quote != null)
                details.Quote = quote;

            if (gender.HasValue)
                details.Gender = gender;

            if (language.HasValue)
                details.Language = language;

            SaveToFile();
        }
    }

    public class TradeCodeDetails
    {
        public int Code { get; set; }
        public string? OT { get; set; }
        public int SID { get; set; }
        public int TID { get; set; }
        public int TradeCount { get; set; }
        // Optional trainer properties
        public byte? Gender { get; set; }
        public int? Language { get; set; }
        public string? Quote { get; set; }
    }
}
