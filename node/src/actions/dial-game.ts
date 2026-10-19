import streamDeck, {
	DialAction,
	DialDownEvent,
	DialRotateEvent,
	DidReceiveSettingsEvent,
	SingletonAction,
	TouchTapEvent,
	WillAppearEvent,
	WillDisappearEvent,
} from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

import { canvasFeedback } from "./feedback";

/** 一顆旋鈕玩一場的遊戲 */
export interface DialGame {
	rotate(ticks: number): void;
	/** 旋鈕按下 */
	press(): void;
	/** 觸控條點一下（hold = 長按） */
	touch(hold: boolean): void;
	render(): void;
	dispose(): void;
}

export type DialGameEntry<S extends JsonObject, G extends DialGame> = {
	readonly game: G;
	readonly action: DialAction<S>;
	/** 最新的設定，存檔時以這份為底，不會蓋掉 PI 後來改的值 */
	settings: S;
};

/**
 * 一顆旋鈕一場遊戲的 action 共用流程，對應 C# 每個 KeyAndEncoderBase 子類別裡重複的那一段：
 * 出現時建遊戲（沒設定就寫預設值）、消失時收掉、旋鈕和觸控事件轉給遊戲、PI 改設定時套用。
 * SingletonAction 一個類別管所有放上去的旋鈕，所以用 action id 對應各自的遊戲。
 */
export abstract class DialGameAction<S extends JsonObject, G extends DialGame> extends SingletonAction<S> {
	private readonly entries = new Map<string, DialGameEntry<S, G>>();
	protected readonly logger;

	constructor(
		name: string,
		private readonly defaults: S,
	) {
		super();
		this.logger = streamDeck.logger.createScope(name);
	}

	/** 建立遊戲；draw 收 SVG 字串，entry 在遊戲建好之後才拿得到（存設定用） */
	protected abstract createGame(dial: DialAction<S>, draw: (svg: string) => void, entry: () => DialGameEntry<S, G>): G;

	/** 把設定套到遊戲上，回傳要改寫的欄位（不用寫回就回傳 undefined） */
	protected abstract applySettings(game: G, settings: S): Partial<S> | undefined;

	/** 存一部分設定（例如最高分），其他欄位保留最新值 */
	protected save(entry: DialGameEntry<S, G>, patch: Partial<S>): void {
		entry.settings = { ...entry.settings, ...patch };
		entry.action.setSettings(entry.settings).catch((e) => this.logger.error(`setSettings failed: ${e}`));
	}

	override async onWillAppear(ev: WillAppearEvent<S>): Promise<void> {
		if (!ev.action.isDial()) return;
		this.logger.info("willAppear");

		let settings = ev.payload.settings;
		if (Object.keys(settings).length === 0) {
			settings = { ...this.defaults };
			await ev.action.setSettings(settings);
		}

		const action = ev.action;
		let entry: DialGameEntry<S, G> | undefined;
		const game = this.createGame(action, canvasFeedback(action), () => entry!);
		entry = { game, action, settings };

		this.entries.get(action.id)?.game.dispose();
		this.entries.set(action.id, entry);
		const write = this.applySettings(game, settings);
		if (write) this.save(entry, write);
		game.render();
	}

	override onWillDisappear(ev: WillDisappearEvent<S>): void {
		this.entries.get(ev.action.id)?.game.dispose();
		this.entries.delete(ev.action.id);
	}

	override onDialRotate(ev: DialRotateEvent<S>): void {
		this.entries.get(ev.action.id)?.game.rotate(ev.payload.ticks);
	}

	override onDialDown(ev: DialDownEvent<S>): void {
		this.entries.get(ev.action.id)?.game.press();
	}

	override onTouchTap(ev: TouchTapEvent<S>): void {
		this.entries.get(ev.action.id)?.game.touch(ev.payload.hold);
	}

	override onDidReceiveSettings(ev: DidReceiveSettingsEvent<S>): void {
		const entry = this.entries.get(ev.action.id);
		if (!entry) return;

		// 跟 C# 的 AutoPopulateSettings 一樣只蓋掉 PI 送來的欄位：PI 不會送最高分這類欄位，要補回去存起來
		const merged = { ...entry.settings, ...ev.payload.settings };
		entry.settings = merged;
		const write = this.applySettings(entry.game, merged);
		const missing = Object.keys(merged).some((k) => !(k in ev.payload.settings));
		if (write || missing) this.save(entry, write ?? {});
		entry.game.render();
	}
}
