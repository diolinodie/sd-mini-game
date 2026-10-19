import { Color, Colors, hex } from "../gfx/color";
import { Font, Svg } from "../gfx/svg";
import { ordinal } from "./racer-driver";
import { HudYellow } from "./racer-renderer";
import { MapPoint, RacerTrack, SEG_LEN, START_GATE, THEMES } from "./racer-track";

/** 地圖上的一台車 */
export type RacerMarker = {
	readonly color: Color;
	readonly z: number;
	readonly label: string;
	/** 名次，0 = 不排名 */
	readonly place: number;
	readonly finished: boolean;
};

/** 可以被地圖顯示的比賽（單人版、4P 版） */
export interface RacerSource {
	readonly deviceId: string;
	/** 數字大的優先：4P = 1，單人 = 0 */
	readonly mapPriority: number;
	/** 正在跑（不是標題畫面） */
	readonly mapActive: boolean;
	/** 回傳賽道並填入車子位置，status 是地圖角落的狀態文字 */
	collectMarkers(markers: RacerMarker[]): { track: RacerTrack; status?: string };
}

const T = 144; // 每顆按鍵的圖大小
const FRAME_MS = 250;
const FOLLOW_ZOOM = 3.5;

type Tile = { col: number; row: number; sink: (svg: string) => void; lastSent?: string };

/**
 * 地圖儀表板：放在 Stream Deck+ 上方按鍵的 Racer Map action 共用一個，
 * 把同一台裝置上的賽車畫成俯視地圖。放了幾顆按鍵就依位置拼成一張大圖。
 */
export class RacerMap {
	// #region 比賽登記

	private static readonly sources: RacerSource[] = [];

	static register(src: RacerSource): void {
		if (!RacerMap.sources.includes(src)) RacerMap.sources.push(src);
	}

	static unregister(src: RacerSource): void {
		const i = RacerMap.sources.indexOf(src);
		if (i >= 0) RacerMap.sources.splice(i, 1);
	}

	// #endregion

	// #region 共用實例（每台裝置一個）

	private static readonly maps = new Map<string, RacerMap>();
	private refCount = 0;

	static acquire(deviceId: string): RacerMap {
		let map = RacerMap.maps.get(deviceId);
		if (!map) {
			map = new RacerMap(deviceId);
			RacerMap.maps.set(deviceId, map);
		}
		map.refCount++;
		return map;
	}

	release(): void {
		if (--this.refCount > 0) return;
		RacerMap.maps.delete(this.deviceId);
		this.disposed = true;
		this.stopTimer();
	}

	// #endregion

	private readonly tiles: Tile[] = [];
	private follow = false;
	private timer: ReturnType<typeof setInterval> | undefined;
	private disposed = false;

	private constructor(private readonly deviceId: string) {}

	attach(col: number, row: number, sink: (svg: string) => void): void {
		this.removeTiles((t) => t.col === col && t.row === row);
		this.tiles.push({ col, row, sink });
		// 拼圖範圍變了，每格都要重送
		for (const t of this.tiles) t.lastSent = undefined;
		if (!this.disposed) {
			this.stopTimer();
			this.timer = setInterval(() => this.render(), FRAME_MS);
			this.render();
		}
	}

	detach(col: number, row: number, sink: (svg: string) => void): void {
		this.removeTiles((t) => t.col === col && t.row === row && t.sink === sink);
		for (const t of this.tiles) t.lastSent = undefined;
		if (this.tiles.length === 0) this.stopTimer();
	}

	/** 按鍵按下：全圖 ↔ 跟著領先者放大 */
	toggleView(): void {
		this.follow = !this.follow;
		for (const t of this.tiles) t.lastSent = undefined;
		this.render();
	}

	private removeTiles(pred: (t: Tile) => boolean): void {
		for (let i = this.tiles.length - 1; i >= 0; i--) if (pred(this.tiles[i])) this.tiles.splice(i, 1);
	}

	private stopTimer(): void {
		if (this.timer !== undefined) clearInterval(this.timer);
		this.timer = undefined;
	}

	private render(): void {
		if (this.disposed || this.tiles.length === 0) return;

		const markers: RacerMarker[] = [];
		let track: RacerTrack | undefined;
		let status: string | undefined;
		const all = RacerMap.sources.filter((s) => s.deviceId === this.deviceId);
		const active = all.filter((s) => s.mapActive);
		if (active.length > 0) {
			const top = Math.max(...active.map((s) => s.mapPriority));
			for (const s of active.filter((s) => s.mapPriority === top)) {
				const r = s.collectMarkers(markers);
				if (!track) {
					track = r.track;
					status = r.status;
				}
			}
		} else if (all.length > 0) {
			// 沒人在跑：顯示賽道，車子都在起點
			const s = [...all].sort((a, b) => b.mapPriority - a.mapPriority)[0];
			({ track, status } = s.collectMarkers(markers));
		}

		const minC = Math.min(...this.tiles.map((t) => t.col));
		const maxC = Math.max(...this.tiles.map((t) => t.col));
		const minR = Math.min(...this.tiles.map((t) => t.row));
		const maxR = Math.max(...this.tiles.map((t) => t.row));
		const cw = (maxC - minC + 1) * T;
		const ch = (maxR - minR + 1) * T;

		const g = new Svg(cw, ch, true);
		draw(g, cw, ch, track, markers, status, this.follow);

		for (const t of this.tiles) {
			const svg = g.toString({ x: (t.col - minC) * T, y: (t.row - minR) * T, w: T, h: T });
			if (svg === t.lastSent) continue;
			t.lastSent = svg;
			t.sink(svg);
		}
	}
}

function segIndex(track: RacerTrack, z: number): number {
	return Math.max(0, Math.min(track.goalIndex, Math.trunc(z / SEG_LEN)));
}

function polyline(pts: readonly MapPoint[], from: number, to: number, map: (p: MapPoint) => MapPoint): number[] {
	const STEP = 4;
	const list: number[] = [];
	for (let i = from; i < to; i += STEP) {
		const p = map(pts[i]);
		list.push(p.x, p.y);
	}
	const end = map(pts[to]);
	list.push(end.x, end.y);
	if (list.length < 4) list.push(list[0], list[1]);
	return list;
}

function draw(g: Svg, cw: number, ch: number, track: RacerTrack | undefined, markers: RacerMarker[], status: string | undefined, followView: boolean): void {
	g.clear(Colors.Black);
	g.fillRectGradient(0, 0, cw, ch, hex(0x1a1e3a), hex(0x0c0e1a));

	const u = T / 144;
	const titleFont: Font = { size: 20 * u, bold: true, italic: true };
	const font: Font = { size: 13 * u, bold: true };

	if (!track) {
		g.drawStringIn("RACER MAP", titleFont, hex(0xff8c20), { x: 0, y: 0, w: cw, h: ch * 0.6 }, "center", "center");
		g.drawStringIn("no race running", font, Colors.Gray, { x: 0, y: ch * 0.45, w: cw, h: ch * 0.4 }, "center", "center");
		return;
	}

	const pts = track.getMapPoints();
	const b = track.getMapBounds();
	const margin = 14 * u;
	let scale = Math.min((cw - margin * 2) / b.w, (ch - margin * 2) / b.h);
	let ox = b.x + b.w / 2;
	let oy = b.y + b.h / 2;

	if (followView && markers.length > 0) {
		const lead = [...markers].sort((a, c) => (a.place === 0 ? Infinity : a.place) - (c.place === 0 ? Infinity : c.place) || c.z - a.z)[0];
		const p = pts[segIndex(track, lead.z)];
		ox = p.x;
		oy = p.y;
		scale *= FOLLOW_ZOOM;
	}

	const map = (p: MapPoint): MapPoint => ({ x: (p.x - ox) * scale + cw / 2, y: (p.y - oy) * scale + ch / 2 });

	// 賽道：每關用那一關的地面顏色
	const goal = track.goalIndex;
	const roadW = Math.max(3, 7 * u);
	g.drawPolyline(polyline(pts, 0, goal, map), hex(0x080810), roadW + 3 * u);
	for (let s = 0; s < track.checkpoint.length; s++) {
		g.drawPolyline(polyline(pts, track.stageStart[s], track.checkpoint[s], map), THEMES[s].grassLight, roadW);
	}

	// 起點、檢查點、終點
	const start = map(pts[START_GATE]);
	const fr = 5 * u;
	g.fillEllipse(start.x - fr, start.y - fr, fr * 2, fr * 2, Colors.White);
	for (let s = 0; s < track.checkpoint.length - 1; s++) {
		const c = map(pts[track.checkpoint[s]]);
		g.fillRect(c.x - fr, c.y - fr, fr * 2, fr * 2, hex(0x2040c0));
		g.drawRect(c.x - fr, c.y - fr, fr * 2, fr * 2, Colors.White);
	}
	const gp = map(pts[goal]);
	const q = fr;
	for (let i = 0; i < 4; i++) {
		g.fillRect(gp.x - q + (i % 2) * q, gp.y - q + Math.trunc(i / 2) * q, q, q, i === 0 || i === 3 ? Colors.White : Colors.Black);
	}
	g.drawRect(gp.x - q, gp.y - q, q * 2, q * 2, Colors.White);

	// 車子：後面的先畫，領先的蓋在上面
	const r = 9 * u;
	const labelFont: Font = { size: 11 * u, bold: true };
	for (const m of [...markers].sort((a, c) => a.z - c.z)) {
		const p = map(pts[segIndex(track, m.z)]);
		g.fillEllipse(p.x - r, p.y - r, r * 2, r * 2, m.color);
		g.drawEllipse(p.x - r, p.y - r, r * 2, r * 2, Colors.White, 2 * u);
		if (m.label) {
			g.drawStringIn(m.label, labelFont, Colors.Black, { x: p.x - r + 1, y: p.y - r + 1, w: r * 2, h: r * 2 }, "center", "center");
			g.drawStringIn(m.label, labelFont, Colors.White, { x: p.x - r, y: p.y - r, w: r * 2, h: r * 2 }, "center", "center");
		}
	}

	// 名次表
	const ranked = markers.filter((m) => m.place > 0).sort((a, c) => a.place - c.place);
	let y = 4 * u;
	for (const m of ranked) {
		g.fillRect(4 * u, y + 2 * u, 10 * u, 10 * u, m.color);
		const line = `${ordinal(m.place)} ${m.label}${m.finished ? " FIN" : ""}`;
		g.drawString(line, font, Colors.Black, 17 * u + 1, y + 1);
		g.drawString(line, font, Colors.White, 17 * u, y);
		y += 15 * u;
	}

	if (status) {
		g.drawStringIn(status, font, Colors.Black, { x: 1, y: 1, w: cw - 4 * u, h: ch - 3 * u }, "far", "far");
		g.drawStringIn(status, font, HudYellow, { x: 0, y: 0, w: cw - 4 * u, h: ch - 3 * u }, "far", "far");
	}
}
