using SysBot.Base;
using System;

namespace SysBot.Pokemon;

/// <summary>
/// Tracks the state of the bot and what it should execute next.
/// </summary>
[Serializable]
public sealed class PokeBotState : BotState<PokeRoutineType, SwitchConnectionConfig>
{
    /// <summary>
    /// Multi-tenant only: the bot's own folder, so per-bot files (trade codes) stay isolated when
    /// several bots share one process. Null for normal single-bot processes. Not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DataFolder { get; set; }

    /// <inheritdoc/>
    public override void Initialize() => Resume();

    /// <inheritdoc/>
    public override void IterateNextRoutine() => CurrentRoutineType = NextRoutineType;

    /// <inheritdoc/>
    public override void Pause() => NextRoutineType = PokeRoutineType.Idle;

    /// <inheritdoc/>
    public override void Resume() => NextRoutineType = InitialRoutine;
}
