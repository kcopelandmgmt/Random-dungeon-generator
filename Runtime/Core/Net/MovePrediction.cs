using System;
using System.Collections.Generic;
using DungeonGen.Core.Combat;

namespace DungeonGen.Core.Net
{
    /// <summary>What may stop the local player walking this frame, as far as the client knows it (the server's gates in <c>MatchServer.Apply</c>).</summary>
    public struct MoveGate
    {
        /// <summary>The countdown is over (or the round is a tutorial): the spawn is no longer sealed.</summary>
        public bool DoorsOpen;
        /// <summary>Downed or dead: the server ignores movement.</summary>
        public bool DownOrDead;
        /// <summary>The server's channel for this player (loot, door, extract, shrine...), from <see cref="SelfSnapshot.ChannelName"/>; empty when none.</summary>
        public string Channel;
        /// <summary>Kilograms carried and the capacity, from <see cref="SelfItems"/>; capacity 0 means unknown (no carry slow-down predicted).</summary>
        public float LoadKg, CapacityKg;
        /// <summary>Invisible walks at 0.6 speed.</summary>
        public bool Invisible;
        /// <summary>The shut doors the client's view knows (solid unless the body passes doors).</summary>
        public ISet<GridPoint> ClosedDoors;

        /// <summary>Does the server move this player by its intent now? Any channel but a prayer holds the player still; walking away breaks a prayer.</summary>
        public bool Walks => DoorsOpen && !DownOrDead && (string.IsNullOrEmpty(Channel) || Channel == MatchServer.ShrineChannel);
    }

    /// <summary>What a reconcile did.</summary>
    public enum PredictionCorrection { None, Smooth, Snap, Started }

    /// <summary>
    /// Client-side prediction of the local player's own walking (2026-10-26 online play beyond the LAN). Read-only toward the round: it moves a private
    /// copy of the player's body in a scratch <see cref="FightSim"/> over the client's layout with the server's own <see cref="FightSim.Move"/>, so the
    /// speed (sheet move speed, <see cref="Combatant.CarrySpeedScale"/> from <see cref="Items.CarryRules.MoveSpeedFactor"/>, Invisible) and the collision
    /// (walls, pits, pillars, shut doors, sliding along a wall) are the server's, not a copy of them.
    /// <para>
    /// <see cref="Step"/> applies the move input the moment it is sent. <see cref="Reconcile"/> compares an authoritative snapshot position with where the
    /// prediction stood one round trip earlier (the moment the server saw), so moving at the predicted speed reads as no error at all. An error over
    /// <see cref="SnapCells"/> (a dash, a knock-back, a resurrection) snaps; a smaller one is applied at once to the logical position and drawn away over
    /// <see cref="SmoothSeconds"/> (<see cref="Shown"/>). When the server keeps the player still while the prediction walks (a stun, a root or a wind-up the
    /// wire does not carry), prediction holds for <see cref="HoldSeconds"/> so it does not keep running ahead and being pulled back.
    /// </para>
    /// </summary>
    public sealed class MovePredictor
    {
        public const float SnapCells = 1.5f, SmoothSeconds = 0.1f, DeadbandCells = 0.08f, HistorySeconds = 2f;
        /// <summary>Server moved less than this fraction of what was predicted over a <see cref="StallWindowSeconds"/> window: hold.</summary>
        public const float StallFraction = 0.15f, StallWindowSeconds = 0.25f, StallMinCells = 0.3f, HoldSeconds = 0.4f;

        private readonly FightSim _sim;
        private readonly PlayerStats _sheet;
        private readonly int _id;
        private Combatant _body;
        private readonly List<(float t, Vec2 p)> _history = new List<(float, Vec2)>();
        private Vec2 _offset; private float _offsetAt;
        private float _heldUntil = -1f;
        // Stall window: what the prediction and the server each moved between consecutive reconciles.
        private bool _hasLast; private Vec2 _lastServer; private float _lastPast;
        private float _windowPredicted, _windowServer, _windowSeconds;

        public MovePredictor(DungeonLayout layout, PlayerStats sheet, int playerId = 1)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _id = playerId;
            _sim = new FightSim(layout, 0);
        }

        /// <summary>The logical predicted position (cells).</summary>
        public Vec2 Position { get; private set; }
        public bool Started { get; private set; }
        public int Snaps { get; private set; }
        public int Corrections { get; private set; }
        public int Holds { get; private set; }
        /// <summary>The last reconcile's error, in cells.</summary>
        public float LastError { get; private set; }
        public bool Held(float now) => now < _heldUntil;

        /// <summary>Start (or restart) at an authoritative position.</summary>
        public void Reset(Vec2 at, float now)
        {
            Position = at; Started = true;
            _history.Clear(); _history.Add((now, at));
            _offset = default; _hasLast = false; _windowPredicted = _windowServer = _windowSeconds = 0f; _heldUntil = -1f;
        }

        private Combatant Body()
        {
            // The scratch body may step on a trap: a hurt or held body is replaced, so only the sheet and the gate decide how it walks.
            if (_body == null || !_body.CanMove || _body.Health < _body.MaxHealth || _body.Status.SlowFraction > 0f)
                _body = _sim.Add(Combatant.Player(_id, 0, _sheet, Position));
            if (_sim.Events.Count > 256) _sim.Events.Clear();
            return _body;
        }

        /// <summary>Walk <paramref name="dt"/> seconds with the move input (grid axes, as in <see cref="PlayerIntent.Move"/>) at the server's speed rules. Returns the new position.</summary>
        public Vec2 Step(float now, float dt, Vec2 move, MoveGate gate)
        {
            if (!Started) return Position;
            if (dt > 0f && move.Length > 0.05f && gate.Walks && !Held(now))
            {
                var body = Body();
                body.Position = Position;
                body.CarrySpeedScale = gate.CapacityKg > 0f ? Items.CarryRules.MoveSpeedFactor(gate.LoadKg, gate.CapacityKg) : 1f;
                bool invisible = body.Has(BuffKind.Invisible);
                if (gate.Invisible && !invisible) body.Buffs.Add(new Buff { Kind = BuffKind.Invisible, Until = float.MaxValue });
                else if (!gate.Invisible && invisible) body.Buffs.RemoveAll(b => b.Kind == BuffKind.Invisible);
                _sim.ClosedDoors = gate.ClosedDoors ?? new HashSet<GridPoint>();
                _sim.Move(_id, Position + move.Normalized * 10f, dt);   // what MatchServer.Apply does with an intent's move
                Position = body.Position;
            }
            Record(now);
            return Position;
        }

        private void Record(float now)
        {
            if (_history.Count > 0 && _history[_history.Count - 1].t >= now) _history[_history.Count - 1] = (now, Position);
            else _history.Add((now, Position));
            int drop = 0;
            while (drop < _history.Count - 2 && _history[drop + 1].t < now - HistorySeconds) drop++;
            if (drop > 0) _history.RemoveRange(0, drop);
        }

        /// <summary>Where the prediction stood at time <paramref name="t"/> (linear between recorded frames; the oldest or newest outside them).</summary>
        public Vec2 PositionAt(float t)
        {
            if (_history.Count == 0) return Position;
            if (t <= _history[0].t) return _history[0].p;
            for (int i = _history.Count - 1; i > 0; i--)
            {
                var b = _history[i]; var a = _history[i - 1];
                if (t >= a.t)
                {
                    if (t >= b.t) return b.p;
                    float f = (t - a.t) / Math.Max(1e-6f, b.t - a.t);
                    return a.p + (b.p - a.p) * f;
                }
            }
            return _history[0].p;
        }

        /// <summary>
        /// An authoritative position arrived. <paramref name="lagSeconds"/> is how long ago, on the prediction's clock, the server stood where it says
        /// (the round trip plus half a server tick). Corrects the logical position and the history by the error.
        /// </summary>
        public PredictionCorrection Reconcile(float now, Vec2 server, float lagSeconds)
        {
            if (!Started) { Reset(server, now); return PredictionCorrection.Started; }
            float pastAt = now - Math.Max(0f, lagSeconds);
            var past = PositionAt(pastAt);
            WatchForStall(now, server, pastAt);
            var error = server - past;
            LastError = error.Length;
            if (LastError < DeadbandCells) return PredictionCorrection.None;
            if (LastError > SnapCells)
            {
                var carried = Position + error;   // keep what was walked since, if that lands on floor
                if (Walkable(carried)) { Position = carried; Shift(error); }
                else { Position = server; RebaseHistory(now); }
                _offset = default; Snaps++;
                return PredictionCorrection.Snap;
            }
            var corrected = Position + error;
            if (!Walkable(corrected)) { corrected = server; }
            var applied = corrected - Position;
            _offset = CurrentOffset(now) + applied; _offsetAt = now;
            Position = corrected;
            Shift(applied);
            Corrections++;
            return PredictionCorrection.Smooth;
        }

        /// <summary>The position to draw: the logical one, minus what is left of the last correction (it fades over <see cref="SmoothSeconds"/>).</summary>
        public Vec2 Shown(float now) => Position - CurrentOffset(now);

        private Vec2 CurrentOffset(float now)
        {
            float f = SmoothSeconds <= 0f ? 0f : 1f - (now - _offsetAt) / SmoothSeconds;
            return f <= 0f ? default : _offset * Math.Min(1f, f);
        }

        private void Shift(Vec2 by)
        {
            for (int i = 0; i < _history.Count; i++) _history[i] = (_history[i].t, _history[i].p + by);
        }

        /// <summary>After a snap the old path means nothing: the history restarts at the new position.</summary>
        private void RebaseHistory(float now) { _history.Clear(); _history.Add((now, Position)); _hasLast = false; _windowPredicted = _windowServer = _windowSeconds = 0f; }

        private bool Walkable(Vec2 p) => _sim.Walkable(p, Body());

        private void WatchForStall(float now, Vec2 server, float pastAt)
        {
            if (_hasLast && pastAt > _lastPast)
            {
                _windowPredicted += Vec2.Distance(PositionAt(pastAt), PositionAt(_lastPast));
                _windowServer += Vec2.Distance(server, _lastServer);
                _windowSeconds += pastAt - _lastPast;
                if (Held(now) && Vec2.Distance(server, _lastServer) > 0.05f) _heldUntil = -1f;   // the server moves us again
                if (_windowSeconds >= StallWindowSeconds)
                {
                    if (_windowPredicted >= StallMinCells && _windowServer < StallFraction * _windowPredicted) { _heldUntil = now + HoldSeconds; Holds++; }
                    _windowPredicted = _windowServer = _windowSeconds = 0f;
                }
            }
            _hasLast = true; _lastServer = server; _lastPast = pastAt;
        }
    }
}
