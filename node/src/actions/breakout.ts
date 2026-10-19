import { action } from "@elgato/streamdeck";

import { BallSpeed, BreakoutGame } from "../games/breakout";
import { PLUGIN_UUID } from "../ids";
import { DialGameAction } from "./dial-game";

/**
 * 設定欄位名稱跟型別（全是字串）要跟 C# 版的 PluginSettings 一樣，升級後才讀得到舊設定。
 */
type BreakoutSettings = {
	// 旋鈕轉一格球拍移動幾 px
	paddleStep?: string;
	ballSpeed?: string;
};

/** 打磚塊，遊戲本體在 games/breakout.ts */
@action({ UUID: `${PLUGIN_UUID}.breakout` })
export class BreakoutAction extends DialGameAction<BreakoutSettings, BreakoutGame> {
	constructor() {
		super("breakout", { paddleStep: "8", ballSpeed: "normal" });
	}

	protected override createGame(draw: (svg: string) => void): BreakoutGame {
		return new BreakoutGame(draw);
	}

	protected override applySettings(game: BreakoutGame, s: BreakoutSettings): undefined {
		const step = parseFloat(s.paddleStep ?? "");
		const speed: BallSpeed = s.ballSpeed === "slow" || s.ballSpeed === "fast" ? s.ballSpeed : "normal";
		game.configure(Number.isFinite(step) ? step : 8, speed);
		return undefined;
	}
}
