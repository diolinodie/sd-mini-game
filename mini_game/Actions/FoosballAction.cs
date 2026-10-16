using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MiniGame
{
    /// <summary>
    /// 手足球機的一顆旋鈕。4 個放在同一台 Stream Deck+ 的旋鈕 1~4 上，共用一個 FoosballGame。
    /// 紅隊用旋鈕 1、3，藍隊用旋鈕 2、4。轉動 = 桿子上下移動，按下 = 踢球，按住不放 = 踢完維持抬腳讓球過去。
    /// 觸控條點一下 = 暫停 / 繼續，長按 = 回到開始畫面。
    /// </summary>
    [PluginActionId("com.sd.minigame.foosball")]
    public class FoosballAction : KeyAndEncoderBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    rodStep = "4",
                    goalsToWin = "5",
                };
            }

            // 每格旋鈕桿子移動幾 px，每個人可以調自己的手感
            [JsonProperty(PropertyName = "rodStep")]
            public string rodStep { get; set; }

            // 幾分獲勝，四個旋鈕共用，最後改的那個生效
            [JsonProperty(PropertyName = "goalsToWin")]
            public string goalsToWin { get; set; }
        }

        private readonly PluginSettings settings;
        private readonly int column;
        private readonly FoosballGame game;
        private readonly Action<string> sink;

        public FoosballAction(ISDConnection connection, InitialPayload payload) : base(connection, payload)
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
            Logger.Instance.LogMessage(TracingLevel.INFO, $"foosball, FoosballAction column={column} device={Connection.DeviceId}");

            if (column < 0 || column >= FoosballGame.SLOTS)
            {
                // 例如放在有 6 顆旋鈕的機種的第 5、6 顆
                Connection.SetFeedbackAsync(new Dictionary<string, string>
                {
                    ["canvas"] = FoosballGame.RenderMessage("FOOSBALL", "use dials 1 to 4"),
                });
                return;
            }

            sink = b64 => Connection.SetFeedbackAsync(new Dictionary<string, string> { ["canvas"] = b64 });
            game = FoosballGame.Acquire(Connection.DeviceId);
            game.SetGoalsToWin(GoalsToWin());
            game.Attach(column, sink);
        }

        public override void Dispose()
        {
            if (game != null)
            {
                game.Detach(column, sink);
                game.Release();
            }
            Logger.Instance.LogMessage(TracingLevel.INFO, $"foosball, Destructor called column={column}");
        }

        private double RodStep()
        {
            return double.TryParse(settings.rodStep, out double v) && v > 0 ? v : 4;
        }

        private int GoalsToWin()
        {
            return int.TryParse(settings.goalsToWin, out int v) && v > 0 ? v : 5;
        }

        public override void DialRotate(DialRotatePayload payload)
        {
            game?.Rotate(column, payload.Ticks, RodStep());
        }

        public override void DialDown(DialPayload payload)
        {
            game?.Press(column);
        }

        public override void DialUp(DialPayload payload)
        {
            game?.DialReleased(column);
        }

        public override void TouchPress(TouchpadPressPayload payload)
        {
            if (payload.IsLongPress)
                game?.Reset();
            else
                game?.TogglePause();
        }

        public override void KeyPressed(KeyPayload payload)
        {
            game?.Press(column);
        }

        public override void KeyReleased(KeyPayload payload)
        {
            game?.DialReleased(column);
        }

        public override void OnTick() { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            Connection.SetSettingsAsync(JObject.FromObject(settings));
            game?.SetGoalsToWin(GoalsToWin());
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    }
}
