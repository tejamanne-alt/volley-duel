using System;
using Duel.Core.Sim;

namespace VolleyDuel.Sim
{
    /// <summary>
    /// The volleyball rules as a deterministic step function. Holds only immutable tables and
    /// the match config; all mutable state lives in <see cref="GameState"/>. Not thread-safe:
    /// use one instance per thread (instances are cheap; tables are shared).
    /// </summary>
    public sealed partial class VolleySim : ISimulation<GameState>
    {
        public readonly SimTables Tables;
        public readonly Tuning T;
        public readonly MatchConfig Config;
        readonly FighterDefinition def0;
        readonly FighterDefinition def1;
        readonly CourtDefinition court;
        readonly int setsToWin;
        SimEventBuffer ev;
        int stepTick;

        public VolleySim(SimTables tables, MatchConfig config)
        {
            Tables = tables;
            T = tables.Tuning;
            Config = config;
            if (config.Fighter0 >= tables.Fighters.Length || config.Fighter1 >= tables.Fighters.Length) throw new ArgumentException("Unknown fighter index");
            if (config.Court >= tables.Courts.Length) throw new ArgumentException("Unknown court index");
            def0 = tables.Fighters[config.Fighter0];
            def1 = tables.Fighters[config.Fighter1];
            court = tables.Courts[config.Court];
            setsToWin = config.SetsToWin == 0 ? T.Scoring.SetsToWin : config.SetsToWin;
        }

        public FighterDefinition Def(int player) => player == 0 ? def0 : def1;

        public CourtDefinition Court => court;

        public int SetsToWin => setsToWin;

        public int MaxStateBytes => GameState.MaxBytes;

        // ------------------------------------------------------------------ ISimulation

        public GameState CreateInitialState()
        {
            var s = new GameState
            {
                Phase = MatchPhase.Intro,
                SlowDiv = 1,
                LastToucher = GameState.NoPlayer,
                LastAttacker = GameState.NoPlayer,
                LastPointWinner = GameState.NoPlayer,
                Winner = GameState.NoPlayer,
                MatchRng = Rng.Create(Config.Seed, 1),
            };
            s.F0 = NewFighter(0);
            s.F1 = NewFighter(1);
            s.Server = (byte)s.MatchRng.NextInt(2);
            s.FirstServerThisSet = s.Server;
            PlaceForServe(ref s);
            s.Ball.Mode = BallMode.Held;
            s.Ball.Holder = s.Server;
            s.Ball.Pos = HandPosition(ref s, s.Server);
            return s;
        }

        FighterState NewFighter(int player)
        {
            var f = new FighterState
            {
                Action = FighterAction.Neutral,
                Stamina = T.Meters.StaminaMax,
                Input = InputTracker.Create(),
                RingCenter = int.MinValue / 2,
                BlockRingCenter = int.MinValue / 2,
                BlockRingReveal = int.MaxValue / 2,
                PerfectDigFrame = int.MinValue / 2,
                KnockbackUntil = int.MinValue / 2,
            };
            f.Pos = new FixVec2(FromNet(player, T.Flow.ReceiveFromNet), Fix64.Zero);
            return f;
        }

        public GameState Step(in GameState state, FrameInput p1, FrameInput p2, SimEventBuffer events)
        {
            ev = events;
            GameState s = state;
            s.Tick++;
            stepTick = s.Tick;
            int stamp = s.World + 1;
            s.F0.Input.Record(p1, stamp);
            s.F1.Input.Record(p2, stamp);
            try
            {
                if (s.Phase == MatchPhase.MatchOver) return s;
                if (s.HitStop > 0)
                {
                    s.HitStop--;
                    return s;
                }
                if (s.SlowDiv > 1)
                {
                    s.SlowPhase++;
                    if (s.SlowPhase < s.SlowDiv) return s;
                    s.SlowPhase = 0;
                    if (s.SlowFrames > 0)
                    {
                        s.SlowFrames--;
                        if (s.SlowFrames == 0) s.SlowDiv = 1;
                    }
                }
                s.World++;
                switch (s.Phase)
                {
                    case MatchPhase.Intro: StepIntro(ref s); break;
                    case MatchPhase.PreServe: StepPreServe(ref s); break;
                    case MatchPhase.Rally: StepRally(ref s); break;
                    case MatchPhase.DeadBall: StepDeadBall(ref s); break;
                    case MatchPhase.SetBreak: StepSetBreak(ref s); break;
                    case MatchPhase.Finisher: StepFinisher(ref s); break;
                }
                return s;
            }
            finally
            {
                ev = null;
            }
        }

        public int FrameOf(in GameState state) => state.Tick;

        public bool IsFinished(in GameState state) => state.Phase == MatchPhase.MatchOver;

        public int Serialize(in GameState state, Span<byte> destination) => state.Write(destination);

        public GameState Deserialize(ReadOnlySpan<byte> source) => GameState.Read(source);

        public ulong Hash(in GameState state) => SimulationHash.Of(this, state);

        // ------------------------------------------------------------------ phases

        void StepIntro(ref GameState s)
        {
            if (s.World - s.PhaseStart < T.Flow.IntroFrames) return;
            if (s.SetIndex == 0 && s.RallyIndex == 0)
            {
                Emit(VolleyEvent.MatchStart, GameState.NoPlayer, s.Server);
                Emit(VolleyEvent.SetStart, GameState.NoPlayer, s.SetIndex, s.Server);
            }
            BeginServe(ref s);
        }

        void StepPreServe(ref GameState s)
        {
            UpdateFighter(ref s, 0);
            UpdateFighter(ref s, 1);
            if (s.Phase == MatchPhase.PreServe && s.Ball.Mode == BallMode.Held) s.Ball.Pos = HandPosition(ref s, s.Server);
            UpdatePlans(ref s);
        }

        void StepRally(ref GameState s)
        {
            UpdateFighter(ref s, 0);
            if (s.Phase != MatchPhase.Rally) return;
            UpdateFighter(ref s, 1);
            if (s.Phase != MatchPhase.Rally) return;
            // Ball first, then contacts: a contact at frame F meets the ball at its frame-F position,
            // which is exactly where the plans predicted it.
            StepBall(ref s);
            if (s.Phase != MatchPhase.Rally) return;
            ResolveContacts(ref s);
            if (s.Phase != MatchPhase.Rally) return;
            UpdatePlans(ref s);
        }

        void StepDeadBall(ref GameState s)
        {
            SettleFighter(ref s, 0);
            SettleFighter(ref s, 1);
            BounceDeadBall(ref s);
            if (s.World - s.PhaseStart < T.Flow.DeadBallFrames) return;
            AfterDeadBall(ref s);
        }

        void StepSetBreak(ref GameState s)
        {
            if (s.World - s.PhaseStart < T.Flow.SetBreakFrames) return;
            StartSet(ref s);
        }

        void SetPhase(ref GameState s, MatchPhase phase)
        {
            s.Phase = phase;
            s.PhaseStart = s.World;
        }

        /// <summary>Puts the server in the service zone holding the ball and the receiver at base.</summary>
        void BeginServe(ref GameState s)
        {
            SetPhase(ref s, MatchPhase.PreServe);
            s.RallyStartWorld = s.World;
            s.Possession = s.Server;
            s.Touches = 0;
            s.TouchesSinceCross = 0;
            s.LastToucher = GameState.NoPlayer;
            s.LastTouch = TouchKind.None;
            s.LastAttacker = GameState.NoPlayer;
            PlaceForServe(ref s);
            ResetRallyFlags(ref s.F0);
            ResetRallyFlags(ref s.F1);
            s.Ball = new BallState
            {
                Mode = BallMode.Held,
                Holder = s.Server,
                LaunchSide = s.Server,
                PendingBlockFrame = int.MinValue / 2,
                DecoyUntil = int.MinValue / 2,
            };
            s.Ball.Pos = HandPosition(ref s, s.Server);
            Emit(VolleyEvent.ServeReady, s.Server);
            AnnounceSetPoint(ref s);
        }

        void PlaceForServe(ref GameState s)
        {
            for (int i = 0; i < 2; i++)
            {
                ref FighterState f = ref GameState.Fighter(ref s, i);
                Fix64 x = i == s.Server ? FromNet(i, T.Serve.StandFromNet) : FromNet(i, T.Flow.ReceiveFromNet);
                f.Pos = new FixVec2(x, Fix64.Zero);
                f.Vel = FixVec2.Zero;
                f.Action = i == s.Server ? FighterAction.ServeHold : FighterAction.Neutral;
                f.Jump = JumpKind.None;
                f.ActionStart = s.World;
                f.ActionEnd = s.World;
                ClearCommit(ref f);
            }
        }

        void ResetRallyFlags(ref FighterState f)
        {
            f.FloorSaveUsed = false;
            f.ExtraRecovery = 0;
            f.Bait = FeintKind.None;
            f.BaitStart = int.MinValue / 2;
            f.BaitEnd = int.MinValue / 2;
            f.DecoyUntil = int.MinValue / 2;
            f.ActiveTrick = TrickKind.None;
            f.ActiveTrickAmped = false;
            f.TrickUntil = int.MinValue / 2;
            f.HangUntil = int.MinValue / 2;
            f.LandInDigStance = false;
            f.BlockedLast = false;
            f.PerfectDigFrame = int.MinValue / 2;
            f.KnockbackUntil = int.MinValue / 2;
            f.BlockRingCenter = int.MinValue / 2;
            f.BlockRingReveal = int.MaxValue / 2;
            f.Ring = RingKind.None;
            f.Charging = false;
            f.SwungThisJump = false;
        }

        void StartSet(ref GameState s)
        {
            s.SetIndex++;
            s.Score0 = 0;
            s.Score1 = 0;
            s.Streak0 = 0;
            s.Streak1 = 0;
            s.RunArmed0 = false;
            s.RunArmed1 = false;
            // Spec: whoever received first in one set serves first in the next.
            s.Server = (byte)(1 - s.FirstServerThisSet);
            s.FirstServerThisSet = s.Server;
            // Spec: Stamina resets each set; Surge carries over.
            for (int i = 0; i < 2; i++)
            {
                ref FighterState f = ref GameState.Fighter(ref s, i);
                f.Stamina = T.Meters.StaminaMax;
                f.Gassed = false;
            }
            DrawWind(ref s);
            Emit(VolleyEvent.SetStart, GameState.NoPlayer, s.SetIndex, s.Server);
            BeginServe(ref s);
        }

        void DrawWind(ref GameState s)
        {
            // Local Conditions only: a light crosswind on windy courts, redrawn each set.
            s.Ball.Wind = Fix64.Zero;
        }

        Fix64 CurrentWind(ref GameState s)
        {
            if (!Config.LocalConditions || !court.Wind) return Fix64.Zero;
            // Wind direction alternates by set so neither side keeps an advantage.
            Fix64 w = T.Conditions.WindMax;
            return (s.SetIndex & 1) == 0 ? w : -w;
        }

        void AnnounceSetPoint(ref GameState s)
        {
            for (int i = 0; i < 2; i++)
            {
                int own = s.Score(i), other = s.Score(1 - i);
                if (!T.Scoring.SetPoint(own, other)) continue;
                bool matchPoint = s.SetsWon(i) + 1 >= setsToWin;
                Emit(VolleyEvent.SetPoint, i, matchPoint ? 1 : 0);
                Impact(ref s, matchPoint ? Callout.MatchPoint : Callout.SetPoint, i, new FixVec2(Fix64.Zero, T.Court.NetHeight));
                return;
            }
        }

        bool IsSetPointServe(ref GameState s)
        {
            for (int i = 0; i < 2; i++)
            {
                if (T.Scoring.SetPoint(s.Score(i), s.Score(1 - i))) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ geometry helpers

        /// <summary>+1 for player 0 (left side, facing +X), -1 for player 1. Direction toward the net and the opponent.</summary>
        public static int Dir(int player) => player == 0 ? 1 : -1;

        /// <summary>Side (player index) whose half contains <paramref name="x"/>.</summary>
        public static int SideOf(Fix64 x) => x.IsNegative ? 0 : 1;

        /// <summary>World X that is <paramref name="distance"/> from the net on <paramref name="player"/>'s side.</summary>
        public static Fix64 FromNet(int player, Fix64 distance) => player == 0 ? -distance : distance;

        /// <summary>World X that is <paramref name="distance"/> from the net on the opponent's side.</summary>
        public static Fix64 IntoOpponent(int player, Fix64 distance) => player == 0 ? distance : -distance;

        /// <summary>Signed X in the player's frame: positive toward the net from their side.</summary>
        public static Fix64 TowardNet(int player, Fix64 dx) => player == 0 ? dx : -dx;

        FixVec2 HandPosition(ref GameState s, int player)
        {
            FighterState f = s.FighterAt(player);
            FighterDefinition d = Def(player);
            return new FixVec2(f.Pos.X + T.Reach.HandForward * Dir(player), f.Pos.Y + d.Height * Fix64.FromRatio(9, 10));
        }

        /// <summary>Hand point for an airborne attack: feet plus standing reach, slightly forward.</summary>
        public FixVec2 SpikeHand(FixVec2 feet, int player) => new FixVec2(feet.X + T.Reach.HandForward * Dir(player), feet.Y + Def(player).StandingReach);

        /// <summary>Height a set should come down to for this fighter's spike, near the top of the jump.</summary>
        public Fix64 IdealHitHeight(int player)
        {
            FighterDefinition d = Def(player);
            return d.StandingReach + d.JumpHeight * Fix64.FromRatio(9, 10);
        }

        // ------------------------------------------------------------------ events, impacts, meters

        void Emit(VolleyEvent type, int actor, int a = 0, int b = 0, Fix64 x = default, Fix64 y = default)
        {
            if (ev == null) return;
            ev.Add(stepTick, (ushort)type, actor < 0 || actor > 1 ? SimEvent.NoActor : (byte)actor, a, b, x, y);
        }

        /// <summary>Fires a highlight: callout event plus the sim side of its impact recipe (hit-stop, slow motion).</summary>
        void Impact(ref GameState s, Callout callout, int beneficiary, FixVec2 at)
        {
            Emit(VolleyEvent.Callout, beneficiary, (int)callout, 0, at.X, at.Y);
            ImpactRecipe r = T.Impacts.For(callout);
            if (r == null) return;
            if (r.HitStop > s.HitStop)
            {
                s.HitStop = r.HitStop;
                Emit(VolleyEvent.HitStop, beneficiary, r.HitStop);
            }
            if (r.SlowDiv > 1 && r.SlowFrames > 0)
            {
                s.SlowDiv = (byte)r.SlowDiv;
                s.SlowFrames = (short)r.SlowFrames;
                s.SlowPhase = 0;
                Emit(VolleyEvent.SlowMotion, beneficiary, r.SlowDiv, r.SlowFrames);
            }
        }

        void GainSurge(ref GameState s, int player, int amount)
        {
            if (amount <= 0 || player < 0 || player > 1) return;
            if (Config.SimpleOf(player)) amount = (T.Meters.SimpleSurgeScale * amount).RoundToInt();
            ref FighterState f = ref GameState.Fighter(ref s, player);
            int before = f.Surge;
            int after = Math.Min(T.Meters.SurgeMax, before + amount);
            if (after == before) return;
            f.Surge = (short)after;
            Emit(VolleyEvent.SurgeGain, player, after - before, after);
        }

        bool SpendSurge(ref GameState s, int player, int amount)
        {
            ref FighterState f = ref GameState.Fighter(ref s, player);
            if (f.Surge < amount) return false;
            f.Surge = (short)(f.Surge - amount);
            Emit(VolleyEvent.SurgeSpend, player, amount, f.Surge);
            return true;
        }

        void SpendStamina(ref GameState s, int player, Fix64 amount)
        {
            ref FighterState f = ref GameState.Fighter(ref s, player);
            Fix64 cost = amount * Def(player).StaminaCostScale;
            f.Stamina = Fix64.Max(Fix64.Zero, f.Stamina - cost);
            if (f.Stamina.IsZero && !f.Gassed)
            {
                f.Gassed = true;
                Emit(VolleyEvent.Gassed, player);
            }
        }

        void RefillStamina(ref GameState s, int player, Fix64 amount)
        {
            ref FighterState f = ref GameState.Fighter(ref s, player);
            f.Stamina = Fix64.Min(T.Meters.StaminaMax, f.Stamina + amount);
            if (f.Gassed && f.Stamina >= T.Meters.GassedRecoverAt)
            {
                f.Gassed = false;
                Emit(VolleyEvent.Recovered, player);
            }
        }

        static void ClearCommit(ref FighterState f)
        {
            f.CommitKind = TouchKind.None;
            f.Choice = AttackChoice.None;
            f.CommitTrick = TrickKind.None;
            f.CommitAmped = false;
            f.CommitGrade = TimingGrade.None;
            f.CommitPress = int.MinValue / 2;
            f.CommitContact = int.MinValue / 2;
            f.CommitLock = int.MinValue / 2;
            f.CommitAim = 0;
            f.Charge = 0;
        }
    }
}
