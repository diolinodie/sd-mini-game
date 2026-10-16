using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using System;

namespace MiniGame
{
    /// <summary>
    /// 賽車地圖儀表板，放在 Stream Deck+ 上方的按鍵。
    /// 顯示同一台裝置上正在跑的賽車（4P 優先，否則是單人版）的俯視地圖、每台車的位置和名次。
    /// 放好幾顆的話依按鍵位置拼成一張大地圖（例如整排 4 顆或全部 8 顆）。
    /// 按下 = 全圖 / 跟著領先者放大 切換。
    /// </summary>
    [PluginActionId("com.sd.minigame.racermap")]
    public class RacerMapAction : KeypadBase
    {
        private readonly int col, row;
        private readonly RacerMap map;
        private readonly Action<string> sink;

        public RacerMapAction(ISDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            col = payload.Coordinates?.Column ?? 0;
            row = payload.Coordinates?.Row ?? 0;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer map, RacerMapAction col={col} row={row} device={Connection.DeviceId}");

            sink = b64 => Connection.SetImageAsync(b64);
            map = RacerMap.Acquire(Connection.DeviceId);
            map.Attach(col, row, sink);
        }

        public override void Dispose()
        {
            map.Detach(col, row, sink);
            map.Release();
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer map, Destructor called col={col} row={row}");
        }

        public override void KeyPressed(KeyPayload payload)
        {
            map.ToggleView();
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override void OnTick() { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload) { }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }
    }
}
