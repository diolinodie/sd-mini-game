import streamDeck, {
	action,
	DialDownEvent,
	DialRotateEvent,
	DialUpEvent,
	DidReceiveSettingsEvent,
	SingletonAction,
	TouchTapEvent,
	WillAppearEvent,
	WillDisappearEvent,
} from "@elgato/streamdeck";

import { FoosballGame, Sink, SLOTS } from "../games/foosball";
import { PLUGIN_UUID } from "../ids";
import { canvasFeedback } from "./feedback";

const logger = streamDeck.logger.createScope("foosball");

/**
 * 設定欄位名稱跟型別（全是字串）要跟 C# 版的 PluginSettings 一樣，升級後才讀得到舊設定。
 */
type FoosballSettings = {
	// 每格旋鈕桿子移動幾 px，每個人可以調自己的手感
	rodStep?: string;
	// 幾分獲勝，四個旋鈕共用，最後改的那個生效
	goalsToWin?: string;
};

/** 一顆旋鈕：它在第幾格、共用哪一場比賽 */
type Slot = { column: number; game?: FoosballGame; sink?: Sink; settings: FoosballSettings };

/**
 * 手足球機的一顆旋鈕。4 個放在同一台 Stream Deck+ 的旋鈕 1~4 上，共用一個 FoosballGame。
 * 紅隊用旋鈕 1、3，藍隊用旋鈕 2、4。轉動 = 桿子上下移動，按下 = 踢球，按住不放 = 踢完維持抬腳讓球過去。
 * 觸控條點一下 = 暫停 / 繼續，長按 = 回到開始畫面。
 */
@action({ UUID: `${PLUGIN_UUID}.foosball` })
export class FoosballAction extends SingletonAction<FoosballSettings> {
	private readonly slots = new Map<string, Slot>();

	override async onWillAppear(ev: WillAppearEvent<FoosballSettings>): Promise<void> {
		if (!ev.action.isDial()) return;

		let settings = ev.payload.settings;
		if (Object.keys(settings).length === 0) {
			settings = { rodStep: "4", goalsToWin: "5" };
			await ev.action.setSettings(settings);
		}

		const column = ev.action.coordinates.column;
		const deviceId = ev.action.device.id;
		logger.info(`willAppear column=${column} device=${deviceId}`);

		this.release(ev.action.id);
		const draw = canvasFeedback(ev.action);
		if (column < 0 || column >= SLOTS) {
			// 例如放在有 6 顆旋鈕的機種的第 5、6 顆
			this.slots.set(ev.action.id, { column, settings });
			draw(FoosballGame.renderMessage("FOOSBALL", "use dials 1 to 4"));
			return;
		}

		const game = FoosballGame.acquire(deviceId);
		this.slots.set(ev.action.id, { column, game, sink: draw, settings });
		game.setGoalsToWin(goalsToWin(settings));
		game.attach(column, draw);
	}

	override onWillDisappear(ev: WillDisappearEvent<FoosballSettings>): void {
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

	override onDialRotate(ev: DialRotateEvent<FoosballSettings>): void {
		const slot = this.slots.get(ev.action.id);
		slot?.game?.rotate(slot.column, ev.payload.ticks, rodStep(slot.settings));
	}

	override onDialDown(ev: DialDownEvent<FoosballSettings>): void {
		const slot = this.slots.get(ev.action.id);
		slot?.game?.press(slot.column);
	}

	override onDialUp(ev: DialUpEvent<FoosballSettings>): void {
		const slot = this.slots.get(ev.action.id);
		slot?.game?.dialReleased(slot.column);
	}

	override onTouchTap(ev: TouchTapEvent<FoosballSettings>): void {
		const game = this.slots.get(ev.action.id)?.game;
		if (ev.payload.hold) game?.reset();
		else game?.togglePause();
	}

	override onDidReceiveSettings(ev: DidReceiveSettingsEvent<FoosballSettings>): void {
		const slot = this.slots.get(ev.action.id);
		if (!slot) return;
		slot.settings = ev.payload.settings;
		slot.game?.setGoalsToWin(goalsToWin(slot.settings));
	}
}

function rodStep(s: FoosballSettings): number {
	const v = parseFloat(s.rodStep ?? "");
	return Number.isFinite(v) && v > 0 ? v : 4;
}

function goalsToWin(s: FoosballSettings): number {
	const v = parseInt(s.goalsToWin ?? "", 10);
	return Number.isFinite(v) && v > 0 ? v : 5;
}
