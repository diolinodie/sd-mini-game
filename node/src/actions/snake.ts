import streamDeck, {
	action,
	DialDownEvent,
	DialRotateEvent,
	DidReceiveSettingsEvent,
	SingletonAction,
	TouchTapEvent,
	WillAppearEvent,
	WillDisappearEvent,
} from "@elgato/streamdeck";

import { SnakeGame, SnakeSpeed } from "../games/snake";
import { PLUGIN_UUID } from "../ids";
import { canvasFeedback } from "./feedback";

const logger = streamDeck.logger.createScope("snake");

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

const DEFAULT_SETTINGS: Required<SnakeSettings> = { speed: "normal", wrap: "false", best: "0" };

/**
 * 貪食蛇，遊戲本體在 games/snake.ts。
 * SingletonAction 一個類別管所有放上去的旋鈕，所以用 action id 對應各自的遊戲。
 */
@action({ UUID: `${PLUGIN_UUID}.snake` })
export class SnakeAction extends SingletonAction<SnakeSettings> {
	private readonly games = new Map<string, { game: SnakeGame; settings: SnakeSettings }>();

	override async onWillAppear(ev: WillAppearEvent<SnakeSettings>): Promise<void> {
		if (!ev.action.isDial()) return;
		logger.info("willAppear");

		let settings = ev.payload.settings;
		if (Object.keys(settings).length === 0) {
			settings = { ...DEFAULT_SETTINGS };
			await ev.action.setSettings(settings);
		}

		const action = ev.action;
		const game = new SnakeGame(canvasFeedback(action));
		const entry = { game, settings };
		game.onBest = (best) => {
			entry.settings = { ...entry.settings, best: String(best) };
			action.setSettings(entry.settings).catch((e) => logger.error(`setSettings failed: ${e}`));
		};
		this.games.get(action.id)?.game.dispose();
		this.games.set(action.id, entry);
		apply(game, settings);
		game.render();
	}

	override onWillDisappear(ev: WillDisappearEvent<SnakeSettings>): void {
		this.games.get(ev.action.id)?.game.dispose();
		this.games.delete(ev.action.id);
	}

	override onDialRotate(ev: DialRotateEvent<SnakeSettings>): void {
		this.games.get(ev.action.id)?.game.rotate(ev.payload.ticks);
	}

	override onDialDown(ev: DialDownEvent<SnakeSettings>): void {
		this.games.get(ev.action.id)?.game.press();
	}

	override onTouchTap(ev: TouchTapEvent<SnakeSettings>): void {
		const game = this.games.get(ev.action.id)?.game;
		if (!game) return;
		if (ev.payload.hold) game.restart();
		else game.press();
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<SnakeSettings>): Promise<void> {
		const entry = this.games.get(ev.action.id);
		if (!entry) return;

		// PI 送來的設定不含最高分，保留遊戲這邊的
		apply(entry.game, ev.payload.settings);
		const best = String(entry.game.bestScore);
		entry.settings = { ...ev.payload.settings, best };
		if (ev.payload.settings.best !== best) await ev.action.setSettings(entry.settings);
		entry.game.render();
	}
}

function apply(game: SnakeGame, s: SnakeSettings): void {
	const speed: SnakeSpeed = s.speed === "slow" || s.speed === "fast" ? s.speed : "normal";
	const best = parseInt(s.best ?? "0", 10);
	game.configure(speed, s.wrap?.toLowerCase() === "true", Number.isFinite(best) ? best : 0);
}
