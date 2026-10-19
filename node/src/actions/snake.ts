import { action } from "@elgato/streamdeck";

import { SnakeGame, SnakeSpeed } from "../games/snake";
import { PLUGIN_UUID } from "../ids";
import { DialGameAction, DialGameEntry } from "./dial-game";

/**
 * 設定欄位名稱跟型別（全是字串）要跟 C# 版的 PluginSettings 一樣，升級後才讀得到舊設定。
 */
type SnakeSettings = {
	speed?: string;
	// "true" = 穿牆從另一邊出來，"false" = 撞牆就死
	wrap?: string;
	// 最高分，存在按鍵設定裡，重開 Stream Deck 也還在
	best?: string;
};

/** 貪食蛇，遊戲本體在 games/snake.ts */
@action({ UUID: `${PLUGIN_UUID}.snake` })
export class SnakeAction extends DialGameAction<SnakeSettings, SnakeGame> {
	constructor() {
		super("snake", { speed: "normal", wrap: "false", best: "0" });
	}

	protected override createGame(draw: (svg: string) => void, entry: () => DialGameEntry<SnakeSettings, SnakeGame>): SnakeGame {
		const game = new SnakeGame(draw);
		game.onBest = (best) => this.save(entry(), { best: String(best) });
		return game;
	}

	protected override applySettings(game: SnakeGame, s: SnakeSettings): Partial<SnakeSettings> | undefined {
		const speed: SnakeSpeed = s.speed === "slow" || s.speed === "fast" ? s.speed : "normal";
		const best = parseInt(s.best ?? "0", 10);
		game.configure(speed, s.wrap?.toLowerCase() === "true", Number.isFinite(best) ? best : 0);

		// PI 送來的設定不含最高分，保留遊戲這邊的
		const keep = String(game.bestScore);
		return s.best === keep ? undefined : { best: keep };
	}
}
