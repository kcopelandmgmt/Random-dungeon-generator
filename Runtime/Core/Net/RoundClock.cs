using System.Collections.Generic;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// Server-side: turns the match rules into timed events. Feed it the clock; it tells you which
    /// events to raise (RoundStarted at 0, ExtractsOpened at the rule's time, RoundEnded at the
    /// limit). Each fires once. Nothing here decides what the events do; MatchState does.
    /// </summary>
    public sealed class RoundClock
    {
        private readonly MatchRules _rules;
        private bool _started, _doors, _extracts, _ended;

        public RoundClock(MatchRules rules) { _rules = rules; }

        public IEnumerable<MatchEvent> Tick(float elapsedSeconds)
        {
            if (!_started && elapsedSeconds >= 0f) { _started = true; yield return new MatchEvent(MatchEventKind.RoundStarted, 0f); }
            if (!_doors && elapsedSeconds >= Combat.FightRules.StartCountdownSeconds) { _doors = true; yield return new MatchEvent(MatchEventKind.DoorsOpened, Combat.FightRules.StartCountdownSeconds); }
            if (!_extracts && elapsedSeconds >= _rules.ExtractsOpenAfterSeconds) { _extracts = true; yield return new MatchEvent(MatchEventKind.ExtractsOpened, _rules.ExtractsOpenAfterSeconds); }
            if (!_ended && elapsedSeconds >= _rules.RoundSeconds) { _ended = true; yield return new MatchEvent(MatchEventKind.RoundEnded, _rules.RoundSeconds); }
        }

        public float SecondsUntilExtracts(float elapsed) => System.Math.Max(0f, _rules.ExtractsOpenAfterSeconds - elapsed);
        public float SecondsRemaining(float elapsed) => System.Math.Max(0f, _rules.RoundSeconds - elapsed);
    }
}
