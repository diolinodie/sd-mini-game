import { action, DialAction } from "@elgato/streamdeck";

import { RacerGame } from "../games/racer";
import { PLUGIN_UUID } from "../ids";
import { DialGameAction, DialGameEntry } from "./dial-game";

/**
 * 設定欄位名稱跟型別（全是字串）要跟 C# 版的 PluginSettings 一樣，升級後才讀得到舊設定。
 */
type RacerSettings = {
	steerSensitivity?: string;
	// center = 放開旋鈕方向盤自動回正，hold = 方向盤停在轉到的位置
	steerMode?: string;
	difficulty?: string;
	hiScore?: string;
};

/**
 * 復古街機風格賽車，一顆旋鈕就能玩。
 * 轉動 = 方向盤，按旋鈕 = 瞬間加速（油門自動，每場 3 次），觸控條點一下 = 暫停 / 繼續，長按 = 回標題。
 * 遊戲本體在 games/racer.ts。
 */
@action({ UUID: `${PLUGIN_UUID}.racer` })
export class RacerAction extends DialGameAction<RacerSettings, RacerGame> {
	constructor() {
		super("racer", { steerSensitivity: "normal", steerMode: "center", difficulty: "normal", hiScore: "0" });
	}

	protected override createGame(dial: DialAction<RacerSettings>, draw: (svg: string) => void, entry: () => DialGameEntry<RacerSettings, RacerGame>): RacerGame {
		// 裝置、欄位給 Racer Map 用：同一台裝置的地圖才看得到這場比賽，欄位決定車子顏色
		const game = new RacerGame(dial.device.id, dial.coordinates.column, draw);
		game.onHiScore = (best) => this.save(entry(), { hiScore: String(best) });
		return game;
	}

	protected override applySettings(game: RacerGame, s: RacerSettings): undefined {
		const best = parseInt(s.hiScore ?? "0", 10);
		game.configure(s.steerSensitivity, s.steerMode, s.difficulty, Number.isFinite(best) ? best : 0);
		return undefined;
	}
}
