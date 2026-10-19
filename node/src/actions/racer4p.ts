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

import { RacerRaceGame, Sink, SLOTS } from "../games/racer-race";
import { PLUGIN_UUID } from "../ids";
import { canvasFeedback } from "./feedback";

const logger = streamDeck.logger.createScope("racer4p");

/**
 * 設定欄位名稱跟型別（全是字串）要跟 C# 版的 PluginSettings 一樣，升級後才讀得到舊設定。
 */
type Racer4PSettings = {
	// 每個人可以調自己的手感
	steerSensitivity?: string;
	steerMode?: string;
	// 比賽長度、路上車量，四個旋鈕共用，最後改的那個生效
	raceLength?: string;
	traffic?: string;
};

type Slot = { column: number; game?: RacerRaceGame; sink?: Sink };

/**
 * 4 人同場賽車的一顆旋鈕，dial n = 玩家 Pn。4 個放在同一台 Stream Deck+ 的旋鈕 1~4 上，共用一個 RacerRaceGame。
 * 按旋鈕加入 / 加速，轉動 = 方向盤，觸控條點一下 = 開始 / 暫停，長按 = 回到開始畫面。
 */
@action({ UUID: `${PLUGIN_UUID}.racer4p` })
export class Racer4PAction extends SingletonAction<Racer4PSettings> {
	private readonly slots = new Map<string, Slot>();

	override async onWillAppear(ev: WillAppearEvent<Racer4PSettings>): Promise<void> {
		if (!ev.action.isDial()) return;

		let settings = ev.payload.settings;
		if (Object.keys(settings).length === 0) {
			settings = { steerSensitivity: "normal", steerMode: "center", raceLength: "normal", traffic: "normal" };
			await ev.action.setSettings(settings);
		}

		const column = ev.action.coordinates.column;
		const deviceId = ev.action.device.id;
		logger.info(`willAppear column=${column} device=${deviceId}`);

		this.release(ev.action.id);
		const draw = canvasFeedback(ev.action);
		if (column < 0 || column >= SLOTS) {
			this.slots.set(ev.action.id, { column });
			draw(RacerRaceGame.renderMessage("RACER 4P", "use dials 1 to 4"));
			return;
		}

		const game = RacerRaceGame.acquire(deviceId);
		this.slots.set(ev.action.id, { column, game, sink: draw });
		apply(game, column, settings);
		game.attach(column, draw);
	}

	override onWillDisappear(ev: WillDisappearEvent<Racer4PSettings>): void {
		this.release(ev.action.id);
	}

	private release(id: string): void {
		const slot = this.slots.get(id);
		if (!slot) return;
		this.slots.delete(id);
		if (slot.game && slot.sink) {
			slot.game.detach(slot.column, slot.sink);
			slot.game.release();
		}
	}

	override onDialRotate(ev: DialRotateEvent<Racer4PSettings>): void {
		const slot = this.slots.get(ev.action.id);
		slot?.game?.rotate(slot.column, ev.payload.ticks);
	}

	override onDialDown(ev: DialDownEvent<Racer4PSettings>): void {
		const slot = this.slots.get(ev.action.id);
		slot?.game?.press(slot.column);
	}

	override onTouchTap(ev: TouchTapEvent<Racer4PSettings>): void {
		const game = this.slots.get(ev.action.id)?.game;
		if (ev.payload.hold) game?.reset();
		else game?.tap();
	}

	override onDidReceiveSettings(ev: DidReceiveSettingsEvent<Racer4PSettings>): void {
		const slot = this.slots.get(ev.action.id);
		if (slot?.game) apply(slot.game, slot.column, ev.payload.settings);
	}
}

function apply(game: RacerRaceGame, column: number, s: Racer4PSettings): void {
	game.configure(column, s.steerSensitivity, s.steerMode);
	game.setRace(s.raceLength, s.traffic);
}
