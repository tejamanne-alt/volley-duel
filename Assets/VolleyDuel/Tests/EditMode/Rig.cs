using System;
using System.Collections.Generic;
using Duel.Core.Sim;
using VolleyDuel.Sim;

namespace VolleyDuel.Tests
{
    /// <summary>
    /// Scenario rig for rule tests: builds a match, places fighters and the ball, and steps the real
    /// sim with scripted presses on exact world frames. Every test goes through VolleySim.Step.
    /// </summary>
    public sealed class Rig
    {
        static SimTables cached;

        public readonly SimTables Tables;
        public readonly VolleySim Sim;
        public GameState S;
        public readonly List<SimEvent> Events = new List<SimEvent>();
        readonly SimEventBuffer buffer = new SimEventBuffer();
        readonly List<Script> scripts = new List<Script>();

        struct Script
        {
            public int Player;
            public InputButtons Button;
            public int Frame;
            public int Hold;
            public int Stick;
            public int StickFrom;
            public int StickUntil;
        }

        public static SimTables LoadTables()
        {
            if (cached != null) return cached;
            cached = SimTables.Load(FindData());
            return cached;
        }

        static IDataSource FindData()
        {
#if UNITY_5_3_OR_NEWER
            return new DirectoryDataSource(System.IO.Path.Combine(UnityEngine.Application.dataPath, "VolleyDuel", "Resources", "Data"));
#else
            string overrideDir = Environment.GetEnvironmentVariable("VOLLEYDUEL_REPO");
            return DirectoryDataSource.FindFrom(string.IsNullOrEmpty(overrideDir) ? AppContext.BaseDirectory : overrideDir);
#endif
        }

        public Rig(string fighter0 = "rafa", string fighter1 = "rafa", uint seed = 7, bool simple0 = false, bool simple1 = false)
        {
            Tables = LoadTables();
            var config = MatchConfig.Create(Tables.FighterIndex(fighter0), Tables.FighterIndex(fighter1), 0, seed);
            config.Simple0 = simple0;
            config.Simple1 = simple1;
            Sim = new VolleySim(Tables, config);
            S = Sim.CreateInitialState();
        }

        public Tuning T => Tables.Tuning;

        public FighterDefinition Def(int i) => Sim.Def(i);

        public ref FighterState F(int i) => ref GameState.Fighter(ref S, i);

        /// <summary>Skips the intro so the match sits in PreServe with <paramref name="server"/> holding the ball.</summary>
        public Rig ToServe(int server)
        {
            Run(T.Flow.IntroFrames + 2);
            if (S.Server != server)
            {
                // Rebuild the serve for the requested server.
                S.Server = (byte)server;
                S.FirstServerThisSet = (byte)server;
                S.Ball.Holder = (byte)server;
                F(server).Action = FighterAction.ServeHold;
                F(server).Pos = new FixVec2(VolleySim.FromNet(server, T.Serve.StandFromNet), Fix64.Zero);
                F(1 - server).Action = FighterAction.Neutral;
                F(1 - server).Pos = new FixVec2(VolleySim.FromNet(1 - server, T.Flow.ReceiveFromNet), Fix64.Zero);
            }
            return this;
        }

        /// <summary>Puts the match straight into a rally with the ball in flight.</summary>
        public Rig InRally(FixVec2 ballPos, FixVec2 ballVel, int lastToucher, TouchKind lastTouch, int possession, int touches, BallFlags flags = BallFlags.None)
        {
            Run(T.Flow.IntroFrames + 2);
            S.Phase = MatchPhase.Rally;
            S.PhaseStart = S.World;
            S.Ball.Mode = BallMode.Flight;
            S.Ball.Pos = ballPos;
            S.Ball.Vel = ballVel;
            S.Ball.FlightStart = S.World;
            S.Ball.Flags = flags;
            S.Ball.Spin = BallSpin.None;
            S.Ball.LaunchSide = (byte)(lastToucher <= 1 ? lastToucher : VolleySim.SideOf(ballPos.X));
            S.Ball.PendingBlockFrame = int.MinValue / 2;
            S.Ball.DecoyUntil = int.MinValue / 2;
            S.LastToucher = (byte)lastToucher;
            S.LastTouch = lastTouch;
            S.LastSender = (byte)lastToucher;
            S.LastSendKind = lastTouch;
            S.Possession = (byte)possession;
            S.Touches = (byte)touches;
            S.TouchesSinceCross = 0;
            for (int i = 0; i < 2; i++)
            {
                F(i).Action = FighterAction.Neutral;
                F(i).Vel = FixVec2.Zero;
                F(i).Pos.Y = Fix64.Zero;
            }
            return this;
        }

        public Rig Place(int player, Fix64 x, FighterAction action = FighterAction.Neutral)
        {
            F(player).Pos = new FixVec2(x, Fix64.Zero);
            F(player).Action = action;
            F(player).Vel = FixVec2.Zero;
            return this;
        }

        /// <summary>Presses (and optionally holds) a button so the press is stamped on world frame <paramref name="frame"/>.</summary>
        public Rig PressAt(int player, InputButtons button, int frame, int hold = 1)
        {
            scripts.Add(new Script { Player = player, Button = button, Frame = frame, Hold = hold, StickFrom = int.MaxValue });
            return this;
        }

        /// <summary>Holds the stick at <paramref name="stick"/> while world frames are in [from, until].</summary>
        public Rig StickAt(int player, int stick, int from, int until)
        {
            scripts.Add(new Script { Player = player, Button = InputButtons.None, Frame = int.MinValue, Stick = stick, StickFrom = from, StickUntil = until });
            return this;
        }

        FrameInput ScriptedInput(int player)
        {
            int next = S.World + 1;
            InputButtons b = InputButtons.None;
            int stick = 0;
            foreach (Script sc in scripts)
            {
                if (sc.Player != player) continue;
                if (sc.Button != InputButtons.None && next >= sc.Frame && next < sc.Frame + sc.Hold) b |= sc.Button;
                if (next >= sc.StickFrom && next <= sc.StickUntil) stick = sc.Stick;
            }
            return FrameInput.Create(stick, 0, b);
        }

        public void Step()
        {
            buffer.Clear();
            S = Sim.Step(S, ScriptedInput(0), ScriptedInput(1), buffer);
            for (int i = 0; i < buffer.Count; i++) Events.Add(buffer[i]);
        }

        public Rig Run(int ticks)
        {
            for (int k = 0; k < ticks; k++) Step();
            return this;
        }

        /// <summary>Steps until the world frame reaches <paramref name="frame"/> (hit-stop ticks included).</summary>
        public Rig RunToWorld(int frame, int maxTicks = 5000)
        {
            for (int k = 0; k < maxTicks && S.World < frame; k++) Step();
            return this;
        }

        /// <summary>Steps until a predicate holds or the tick budget runs out.</summary>
        public bool RunUntil(Func<Rig, bool> done, int maxTicks = 3000)
        {
            for (int k = 0; k < maxTicks; k++)
            {
                if (done(this)) return true;
                Step();
            }
            return done(this);
        }

        public bool RunUntilPoint(int maxTicks = 3000) => RunUntil(r => r.Has(VolleyEvent.Point), maxTicks);

        public bool Has(VolleyEvent type) => Events.Exists(e => e.Type == (ushort)type);

        public SimEvent First(VolleyEvent type) => Events.Find(e => e.Type == (ushort)type);

        public List<SimEvent> All(VolleyEvent type) => Events.FindAll(e => e.Type == (ushort)type);

        public bool HasCallout(Callout c) => Events.Exists(e => e.Type == (ushort)VolleyEvent.Callout && e.A == (int)c);

        public static Fix64 X(string v) => Fix64.Parse(v);

        public static FixVec2 V(string x, string y) => new FixVec2(Fix64.Parse(x), Fix64.Parse(y));
    }
}
