using System;
using System.Collections.Generic;
using System.Linq;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Net
{
    /// <summary>
    /// Which tutorial objective the player is on, from what actually happened: match events, the team's snapshots and the (mirrored) round state.
    /// Pure: no clock of its own, no engine. Every lesson latches the moment its condition is seen, in any order, so a player who loots before
    /// clearing a room is not made to do it twice; the card shows the first lesson not yet done. A death ends it as <see cref="TutorialStep.Failed"/>,
    /// the extract as <see cref="TutorialStep.Done"/>.
    /// </summary>
    public sealed class TutorialTracker
    {
        public int LocalPlayer { get; }
        /// <summary>Mob packs in the dungeon outside boss rooms: how many the mob lesson asks for (at most two).</summary>
        public int PackCount { get; }
        /// <summary>How far (cells) the player must walk from where they first stood for the move lesson.</summary>
        public const float MoveCells = 2f;

        private readonly DungeonLayout _layout;
        private readonly HashSet<TutorialStep> _done = new HashSet<TutorialStep>();
        private readonly HashSet<int> _packs = new HashSet<int>();
        private Vec2? _start;
        private bool _failed, _extracted;

        public TutorialTracker(int localPlayer, DungeonLayout layout)
        {
            LocalPlayer = localPlayer; _layout = layout;
            PackCount = Math.Max(1, Math.Min(2, layout?.Rooms.Count(r => r.Mobs != null && r.Mobs.Count > 0 && !r.IsBossRoom) ?? 2));
        }

        // ---- what the card reads ----

        public TutorialStep Step
        {
            get
            {
                if (_extracted) return TutorialStep.Done;
                if (_failed) return TutorialStep.Failed;
                foreach (var s in TutorialScript.Lessons) if (!_done.Contains(s)) return s;
                return TutorialStep.Done;
            }
        }

        public bool IsDone(TutorialStep s) => s == TutorialStep.Done ? _extracted : _done.Contains(s);
        /// <summary>Lessons completed, 0..7.</summary>
        public int LessonsDone => TutorialScript.Lessons.Count(_done.Contains);
        public bool Finished => _extracted || _failed;

        public int PacksCleared => _packs.Count;
        public int Souls { get; private set; }
        public bool UltimateArmed { get; private set; }
        public bool UltimateUsed { get; private set; }
        public bool ChestOpened { get; private set; }
        public float LoadKg { get; private set; }
        public float CapacityKg { get; private set; }
        public bool ExtractsOpen { get; private set; }
        /// <summary>Round time of the latest snapshot or state seen.</summary>
        public float Now { get; private set; }
        /// <summary>When the room boss fell (round time), or null.</summary>
        public float? BossKilledAt { get; private set; }
        /// <summary>Seconds until the extract opens, or 0 once it is open (or null before the boss dies: the card says to kill it first).</summary>
        public float? ExtractOpensIn => ExtractsOpen ? 0f : BossKilledAt.HasValue ? Math.Max(0f, Math.Min(BossKilledAt.Value + TutorialScript.ExtractDelayAfterBoss, TutorialScript.ExtractsFallbackSeconds) - Now) : (float?)null;

        /// <summary>A short line of live progress for the current lesson ("Packs cleared 1 / 2 · Souls 1"), or "".</summary>
        public string Progress
        {
            get
            {
                switch (Step)
                {
                    case TutorialStep.KillMobs: return $"Packs cleared {Math.Min(PacksCleared, PackCount)} / {PackCount}   ·   Souls {Souls}";
                    case TutorialStep.LootChest: return CapacityKg > 0f ? $"Carrying {LoadKg:0.0} / {CapacityKg:0} kg{(LoadKg > CapacityKg ? "   ·   overloaded: you are slower" : "")}" : (ChestOpened ? "The chest is open: take an item" : "");
                    case TutorialStep.ArmUltimate: return $"Souls {Souls} / {FightRules.SoulsToArmUltimate}";
                    case TutorialStep.KillBoss: return UltimateUsed ? "Ultimate unleashed" : UltimateArmed ? "Ultimate armed: ready" : "Ultimate not armed";
                    case TutorialStep.Extract:
                        var t = ExtractOpensIn;
                        return t.HasValue && t.Value > 0f ? $"The extract opens in {(int)t.Value / 60}:{(int)t.Value % 60:00}" : "The extract is open";
                    default: return "";
                }
            }
        }

        // ---- what it is fed ----

        /// <summary>A match event the player's team heard (or the host saw). Returns true when the current step changed.</summary>
        public bool Observe(MatchEvent e)
        {
            var before = Step;
            switch (e.Kind)
            {
                case MatchEventKind.DoorOpened: if (e.B == LocalPlayer) Done(TutorialStep.OpenDoor); break;
                case MatchEventKind.MobPackCleared: _packs.Add(e.A); if (_packs.Count >= PackCount) Done(TutorialStep.KillMobs); break;
                case MatchEventKind.ContainerOpened: if (e.B == LocalPlayer) ChestOpened = true; break;
                case MatchEventKind.ItemTaken: if (e.B == LocalPlayer && e.A < MatchState.BroughtItemIdBase) Done(TutorialStep.LootChest); break;
                case MatchEventKind.UltimateArmed: if (e.A == LocalPlayer) { UltimateArmed = true; Done(TutorialStep.ArmUltimate); Done(TutorialStep.KillMobs); } break;
                case MatchEventKind.UltimateUsed: if (e.A == LocalPlayer) UltimateUsed = true; break;
                case MatchEventKind.BossKilled:
                    if (IsRoomBoss(e.A)) { BossKilledAt = BossKilledAt ?? e.Time; Done(TutorialStep.KillBoss); }
                    break;
                case MatchEventKind.ExtractsOpened: ExtractsOpen = true; break;
                case MatchEventKind.PlayerExtracted: if (e.A == LocalPlayer) { _extracted = true; Done(TutorialStep.Extract); } break;
                case MatchEventKind.PlayerDied: case MatchEventKind.PlayerFinished: if (e.A == LocalPlayer && !_extracted) _failed = true; break;
            }
            Now = Math.Max(Now, e.Time);
            return Step != before;
        }

        /// <summary>The team's snapshot: where the player stands (the move lesson), their load (the weight line). Returns true when the current step changed.</summary>
        public bool Observe(TeamSnapshot snap)
        {
            if (snap == null) return false;
            var before = Step;
            Now = Math.Max(Now, snap.Time);
            foreach (var a in snap.Avatars)
            {
                if (a.Id != LocalPlayer) continue;
                var p = new Vec2(a.X, a.Y);
                if (!_start.HasValue) _start = p;
                else if (Vec2.Distance(p, _start.Value) >= MoveCells) Done(TutorialStep.Move);
                if (a.Dead && !_extracted) _failed = true;
            }
            foreach (var s in snap.Selves)
            {
                if (s.PlayerId != LocalPlayer) continue;
                if (s.Items != null) { LoadKg = s.Items.LoadKg; CapacityKg = s.Items.CapacityKg; }
                if (s.UltimateArmed) UltimateArmed = true;
            }
            return Step != before;
        }

        /// <summary>The round state (the host's, or a client's mirror): souls, the armed ultimate, the boss, the extract, an end the events may have missed. Returns true when the step changed.</summary>
        public bool Observe(MatchState st)
        {
            if (st == null) return false;
            var before = Step;
            Now = Math.Max(Now, st.Time);
            Souls = st.SoulsOf(LocalPlayer);
            if (st.UltimateCharges.TryGetValue(LocalPlayer, out int charges) && charges > 0) { UltimateArmed = true; Done(TutorialStep.ArmUltimate); }
            if (Souls >= FightRules.SoulsToArmUltimate || UltimateArmed) Done(TutorialStep.KillMobs);
            foreach (int pack in st.ClearedPacks) _packs.Add(pack);
            if (_packs.Count >= PackCount) Done(TutorialStep.KillMobs);
            if (!BossKilledAt.HasValue && st.DeadBosses.Any(IsRoomBoss)) { BossKilledAt = TutorialScript.BossKilledAt(st) ?? st.Time; Done(TutorialStep.KillBoss); }
            if (st.ExtractsOpen) ExtractsOpen = true;
            if (st.ExtractedPlayers.Contains(LocalPlayer)) { _extracted = true; Done(TutorialStep.Extract); }
            else if (st.DeadPlayers.Contains(LocalPlayer)) _failed = true;
            return Step != before;
        }

        private bool IsRoomBoss(int room) => _layout == null ? room >= 0 : room >= 0 && room < _layout.Rooms.Count && _layout.Rooms[room].Type == RoomType.Boss;

        private void Done(TutorialStep s) => _done.Add(s);
    }
}
