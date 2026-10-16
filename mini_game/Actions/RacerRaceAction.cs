using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace MiniGame
{
    /// <summary>
    /// 4 人同場賽車的一顆旋鈕。4 個放在同一台 Stream Deck+ 的旋鈕 1~4 上，共用一個 RacerRaceGame。
    /// 旋鈕 n = 玩家 Pn：按下 = 加入 / 按住煞車，轉動 = 方向盤。
    /// 觸控條點一下 = 開跑 / 暫停 / 繼續，長按 = 回到開始畫面。
    /// </summary>
    [PluginActionId("com.odiestudio.minigame.racer4p")]
    public class RacerRaceAction : KeyAndEncoderBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    steerSensitivity = "normal",
                    steerMode = "center",
                    raceLength = "normal",
                    traffic = "normal",
                };
            }

            // 每個人可以調自己的手感
            [JsonProperty(PropertyName = "steerSensitivity")]
            public string steerSensitivity { get; set; }

            [JsonProperty(PropertyName = "steerMode")]
            public string steerMode { get; set; }

            // 比賽長度、路上車量，四個旋鈕共用，最後改的那個生效
            [JsonProperty(PropertyName = "raceLength")]
            public string raceLength { get; set; }

            [JsonProperty(PropertyName = "traffic")]
            public string traffic { get; set; }
        }

        private readonly PluginSettings settings;
        private readonly int column;
        private readonly RacerRaceGame game;
        private readonly Action<string> sink;

        public RacerRaceAction(ISDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
            }

            column = payload.Coordinates?.Column ?? -1;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer4p, RacerRaceAction column={column} device={Connection.DeviceId}");

            if (column < 0 || column >= RacerRaceGame.SLOTS)
            {
                Connection.SetFeedbackAsync(new Dictionary<string, string> { ["canvas"] = RenderMessage("RACER 4P", "use dials 1 to 4") });
                return;
            }

            sink = b64 => Connection.SetFeedbackAsync(new Dictionary<string, string> { ["canvas"] = b64 });
            game = RacerRaceGame.Acquire(Connection.DeviceId);
            ApplySettings();
            game.Attach(column, sink);
        }

        public override void Dispose()
        {
            if (game != null)
            {
                game.Detach(column, sink);
                game.Release();
            }
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer4p, Destructor called column={column}");
        }

        private void ApplySettings()
        {
            game.Configure(column, settings.steerSensitivity, settings.steerMode);
            game.SetRace(settings.raceLength, settings.traffic);
        }

        private static string RenderMessage(string line1, string line2)
        {
            using (var bmp = new Bitmap(RacerConst.W, RacerConst.H, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var big = new Font("Arial", 14, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.Clear(Color.Black);
                g.DrawString(line1, big, Brushes.White, new RectangleF(0, 20, RacerConst.W, 30), center);
                g.DrawString(line2, small, Brushes.Gray, new RectangleF(0, 50, RacerConst.W, 30), center);
                return Tools.ImageToBase64(bmp, true);
            }
        }

        public override void DialRotate(DialRotatePayload payload)
        {
            game?.Rotate(column, payload.Ticks);
        }

        public override void DialDown(DialPayload payload)
        {
            game?.Press(column);
        }

        public override void DialUp(DialPayload payload)
        {
            game?.ReleasePress(column);
        }

        public override void TouchPress(TouchpadPressPayload payload)
        {
            if (payload.IsLongPress)
                game?.Reset();
            else
                game?.Tap();
        }

        public override void KeyPressed(KeyPayload payload)
        {
            game?.Press(column);
        }

        public override void KeyReleased(KeyPayload payload)
        {
            game?.ReleasePress(column);
        }

        public override void OnTick() { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            Connection.SetSettingsAsync(JObject.FromObject(settings));
            if (game != null) ApplySettings();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    }
}
