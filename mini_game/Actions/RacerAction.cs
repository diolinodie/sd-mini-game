using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MiniGame
{
    /// <summary>
    /// 復古街機風格賽車，一顆旋鈕就能玩。
    /// 轉動 = 方向盤，按旋鈕 = 瞬間加速（油門自動，每場 3 次），觸控條點一下 = 暫停 / 繼續，長按 = 回標題。
    /// 遊戲本體在 RacerGame。
    /// </summary>
    [PluginActionId("com.odiestudio.minigame.racer")]
    public class RacerAction : KeyAndEncoderBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    steerSensitivity = "normal",
                    steerMode = "center",
                    difficulty = "normal",
                    hiScore = "0",
                };
            }

            [JsonProperty(PropertyName = "steerSensitivity")]
            public string steerSensitivity { get; set; }

            // center = 放開旋鈕方向盤自動回正，hold = 方向盤停在轉到的位置
            [JsonProperty(PropertyName = "steerMode")]
            public string steerMode { get; set; }

            [JsonProperty(PropertyName = "difficulty")]
            public string difficulty { get; set; }

            [JsonProperty(PropertyName = "hiScore")]
            public string hiScore { get; set; }
        }

        private readonly PluginSettings settings;
        private readonly RacerGame game;

        public RacerAction(ISDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "racer, RacerAction");

            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
            }

            game = new RacerGame(Connection.DeviceId, payload.Coordinates?.Column ?? 0, b64 => Connection.SetFeedbackAsync(new Dictionary<string, string> { ["canvas"] = b64 }));
            game.HiScoreChanged = OnHiScore;
            ApplySettings();
            game.Render();
        }

        public override void Dispose()
        {
            game.Dispose();
            Logger.Instance.LogMessage(TracingLevel.INFO, "racer, Destructor called");
        }

        private void ApplySettings()
        {
            long.TryParse(settings.hiScore, out long best);
            game.Configure(settings.steerSensitivity, settings.steerMode, settings.difficulty, best);
        }

        private void OnHiScore(long best)
        {
            settings.hiScore = best.ToString();
            Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        public override void DialRotate(DialRotatePayload payload)
        {
            game.Rotate(payload.Ticks);
        }

        public override void DialDown(DialPayload payload)
        {
            game.Press();
        }

        public override void DialUp(DialPayload payload) { }

        public override void TouchPress(TouchpadPressPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer, TouchPress longPress={payload.IsLongPress}");
            if (payload.IsLongPress)
                game.Reset();
            else
                game.Tap();
        }

        public override void KeyPressed(KeyPayload payload)
        {
            game.Press();
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override void OnTick() { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "racer, ReceivedSettings");
            Tools.AutoPopulateSettings(settings, payload.Settings);
            Connection.SetSettingsAsync(JObject.FromObject(settings));
            ApplySettings();
            game.Render();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    }
}
