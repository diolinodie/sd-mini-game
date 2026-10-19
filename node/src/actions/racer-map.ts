import streamDeck, { action, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";

import { RacerMap } from "../games/racer-map";
import { PLUGIN_UUID } from "../ids";

const logger = streamDeck.logger.createScope("racer map");

type Tile = { col: number; row: number; map: RacerMap; sink: (svg: string) => void };

/**
 * 賽車的即時俯視地圖，放在 Stream Deck+ 上方的按鍵。放幾顆就依位置拼成一張大圖。
 * 按下 = 全圖 ↔ 跟著領先者放大。地圖本體在 games/racer-map.ts。
 */
@action({ UUID: `${PLUGIN_UUID}.racermap` })
export class RacerMapAction extends SingletonAction {
	private readonly tiles = new Map<string, Tile>();

	override onWillAppear(ev: WillAppearEvent): void {
		if (!ev.action.isKey()) return;
		const key = ev.action;
		const col = key.coordinates?.column ?? 0;
		const row = key.coordinates?.row ?? 0;
		logger.info(`willAppear col=${col} row=${row} device=${key.device.id}`);

		this.release(key.id);
		const sink = (svg: string) => {
			key.setImage("data:image/svg+xml;base64," + Buffer.from(svg).toString("base64")).catch((e) => logger.error(`setImage failed: ${e}`));
		};
		const map = RacerMap.acquire(key.device.id);
		this.tiles.set(key.id, { col, row, map, sink });
		map.attach(col, row, sink);
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		this.release(ev.action.id);
	}

	private release(id: string): void {
		const t = this.tiles.get(id);
		if (!t) return;
		this.tiles.delete(id);
		t.map.detach(t.col, t.row, t.sink);
		t.map.release();
	}

	override onKeyDown(ev: KeyDownEvent): void {
		this.tiles.get(ev.action.id)?.map.toggleView();
	}
}
