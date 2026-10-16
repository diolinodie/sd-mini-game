using System;
using System.Drawing;

namespace MiniGame
{
    /// <summary>賽車共用的常數：畫面投影與車子物理</summary>
    internal static class RacerConst
    {
        public const int W = 200;
        public const int H = 100;
        public const int HORIZON = 40;                     // 平路時地平線的 y
        public const double K = W / 2.0;                   // 投影比例
        public const double ROAD_W = 2000;                 // 半個路寬
        public const double CAM_H = 1000;
        public const double PLAYER_Z = CAM_H * K / (H - HORIZON);   // 玩家車子在相機前方多遠，剛好是路面碰到畫面底部的地方
        public const double PLAYER_CAR_W = 50.4;           // 玩家車子在 PLAYER_Z 時畫出來的寬度（px）

        public const double SEG_LEN = RacerTrack.SEG_LEN;
        public const double MAX_SPEED = SEG_LEN * 60;       // 每秒 60 段
        public const double TOP_KMH = 293;
        public const double PLAYER_W = 0.42;               // 以半個路寬為單位
        public const double STEER_RATE = 2.6;              // 方向盤打滿時每秒橫移幾個半路寬
        public const double CENTRIFUGAL = 0.3;
        public const double OFFROAD_LIMIT = MAX_SPEED * 0.3;
        public const double CRASH_TIME = 1.6;
        public const double STEER_CENTER_DELAY = 0.18;     // 停止轉動多久後方向盤開始回正
        public const double STEER_CENTER_RATE = 3.0;

        public static readonly Color[] PlayerColors =
        {
            Color.FromArgb(0xD8, 0x10, 0x18),
            Color.FromArgb(0x20, 0x60, 0xE0),
            Color.FromArgb(0xE8, 0xB8, 0x10),
            Color.FromArgb(0x20, 0xA0, 0x40),
        };

        public static double SteerSensitivity(string s)
        {
            switch (s)
            {
                case "low": return 0.18;
                case "high": return 0.4;
                default: return 0.28;
            }
        }

        public static string Ordinal(int place)
        {
            switch (place)
            {
                case 1: return "1st";
                case 2: return "2nd";
                case 3: return "3rd";
                default: return place + "th";
            }
        }
    }

    /// <summary>
    /// 一台玩家車子的狀態與物理：方向盤、油門 / 煞車、離心力、出界、撞景物、撞路上的車。
    /// 呼叫端要自己鎖。
    /// </summary>
    internal sealed class RacerDriver
    {
        public double Position;           // 相機的 z，車子在 Position + PLAYER_Z
        public double X;                  // 橫向位置，-1 ~ 1 是路面
        public double Speed;
        public double Steer;              // -1 ~ 1
        public double LastSteerTime = -99;
        public bool Braking;
        public double CrashTimer;
        public double ShakeTimer;
        public double FarOffset, NearOffset;

        public int Stage;
        public int BgFrom, BgTo;
        public double BgFade = 1;         // 0 → 1，換關時背景淡入

        public double SteerSens = 0.28;
        public bool AutoCenter = true;

        public string Msg1, Msg2;
        public double MsgTimer;

        public double Z => Position + RacerConst.PLAYER_Z;
        public double Kmh => Speed / RacerConst.MAX_SPEED * RacerConst.TOP_KMH;
        public bool OffRoad => Math.Abs(X) > 1.05;
        public int Lean => Steer < -0.3 ? -1 : Steer > 0.3 ? 1 : 0;

        public void Reset(double position = 0, double x = 0)
        {
            Position = position;
            X = x;
            Speed = 0;
            Steer = 0;
            Braking = false;
            CrashTimer = 0;
            ShakeTimer = 0;
            FarOffset = NearOffset = 0;
            Stage = 0;
            BgFrom = BgTo = 0;
            BgFade = 1;
            MsgTimer = 0;
        }

        public void Rotate(int ticks, double now)
        {
            Steer = Math.Max(-1, Math.Min(1, Steer + ticks * SteerSens));
            LastSteerTime = now;
        }

        public void ShowMessage(string m1, string m2, double seconds)
        {
            Msg1 = m1;
            Msg2 = m2;
            MsgTimer = seconds;
        }

        /// <summary>計時器、方向盤回正，倒數時也要跑</summary>
        public void Tick(double dt, double now)
        {
            if (MsgTimer > 0) MsgTimer -= dt;
            if (ShakeTimer > 0) ShakeTimer -= dt;
            if (BgFade < 1) BgFade = Math.Min(1, BgFade + dt / 1.5);

            if (AutoCenter && now - LastSteerTime > RacerConst.STEER_CENTER_DELAY)
            {
                double step = RacerConst.STEER_CENTER_RATE * dt;
                Steer = Math.Abs(Steer) <= step ? 0 : Steer - Math.Sign(Steer) * step;
            }
        }

        /// <summary>一幀的物理。canAccel = false 時鬆油門滑行，coastDecel 是滑行減速度（相對極速 / 秒）</summary>
        public void Drive(RacerTrack track, double dt, bool canAccel, double coastDecel)
        {
            const double MAX = RacerConst.MAX_SPEED;
            RacerSegment seg = track.SegmentAt(Z);
            double speedPct = Speed / MAX;
            double startPos = Position;

            Position += Speed * dt;

            if (CrashTimer > 0)
            {
                // 翻車中：不能開，慢慢把車放回路邊
                CrashTimer -= dt;
                Speed = 0;
                double target = Math.Max(-0.8, Math.Min(0.8, X));
                X += (target - X) * Math.Min(1, dt * 3);
            }
            else
            {
                // 方向盤，太慢的時候轉不太動
                if (Speed > 0)
                    X += dt * RacerConst.STEER_RATE * Steer * Math.Min(1, 0.25 + speedPct);
                // 彎道把車往外甩
                X -= dt * speedPct * speedPct * seg.Curve * RacerConst.CENTRIFUGAL;

                if (Braking)
                    Speed -= MAX * 0.9 * dt;
                else if (canAccel)
                    Speed += MAX * (0.22 - 0.12 * speedPct) * dt;   // 0 到極速大約 6.5 秒
                else
                    Speed -= MAX * coastDecel * dt;

                if (OffRoad && Speed > RacerConst.OFFROAD_LIMIT)
                    Speed = Math.Max(RacerConst.OFFROAD_LIMIT, Speed - MAX * 0.7 * dt);

                if (OffRoad)
                    CheckSpriteHit(seg);
                CheckCarHit(seg);
            }

            X = Math.Max(-2.6, Math.Min(2.6, X));
            Speed = Math.Max(0, Math.Min(MAX, Speed));

            double segMoved = (Position - startPos) / RacerConst.SEG_LEN;
            FarOffset += seg.Curve * segMoved * 0.12;
            NearOffset += seg.Curve * segMoved * 0.3;
        }

        public bool ReachedCheckpoint(RacerTrack track)
        {
            return Stage < track.Checkpoint.Length && (int)(Z / RacerConst.SEG_LEN) >= track.Checkpoint[Stage];
        }

        /// <summary>過了檢查點，進下一關（背景淡入下一關的配色）</summary>
        public void NextStage()
        {
            Stage++;
            BgFrom = BgTo;
            BgTo = Stage;
            BgFade = 0;
        }

        /// <summary>這一關跑了幾成，0 ~ 1</summary>
        public double StageProgress(RacerTrack track)
        {
            int s = Math.Min(Stage, track.Checkpoint.Length - 1);
            int from = track.StageStart[s], to = track.Checkpoint[s];
            return Math.Max(0, Math.Min(1, (Z / RacerConst.SEG_LEN - from) / (to - from)));
        }

        public void Crash()
        {
            CrashTimer = RacerConst.CRASH_TIME;
            Speed = 0;
            Braking = false;
            ShakeTimer = 0.5;
        }

        private void CheckSpriteHit(RacerSegment seg)
        {
            if (seg.Sprites == null) return;
            foreach (RacerSpriteItem sp in seg.Sprites)
            {
                double sw = RacerTrack.SpriteWidth(sp.Type) / RacerConst.ROAD_W;
                double sx = sp.Offset + sw / 2 * (sp.Offset > 0 ? 1 : -1);
                if (!Overlap(X, RacerConst.PLAYER_W * 0.8, sx, sw * RacerTrack.SpriteHitRatio(sp.Type)))
                    continue;

                if (Speed > RacerConst.MAX_SPEED * 0.25)
                {
                    Crash();
                }
                else
                {
                    // 慢慢撞到就停住，退一點
                    Speed = 0;
                    Position = Math.Max(0, Position - RacerConst.SEG_LEN * 0.5);
                    ShakeTimer = 0.3;
                }
                return;
            }
        }

        private void CheckCarHit(RacerSegment seg)
        {
            if (seg.Cars == null) return;
            foreach (RacerCar car in seg.Cars)
            {
                double cw = RacerTrack.CarWidth(car.Type) / RacerConst.ROAD_W;
                if (Speed <= car.Speed || !Overlap(X, RacerConst.PLAYER_W * 0.85, car.Offset, cw * 0.85))
                    continue;

                if (Speed - car.Speed > RacerConst.MAX_SPEED * 0.45)
                {
                    Crash();
                }
                else
                {
                    Speed = car.Speed * 0.7;
                    ShakeTimer = 0.4;
                }
                Position = car.Z - RacerConst.PLAYER_Z - RacerConst.SEG_LEN * 0.3;
                return;
            }
        }

        public static bool Overlap(double x1, double w1, double x2, double w2)
        {
            return !(x1 + w1 / 2 < x2 - w2 / 2 || x1 - w1 / 2 > x2 + w2 / 2);
        }
    }

    /// <summary>畫在別人畫面裡的對手車</summary>
    internal struct RacerRival
    {
        public double Z;
        public double X;
        public Color Color;
        public int Lean;
        public double CrashTimer;
    }
}
