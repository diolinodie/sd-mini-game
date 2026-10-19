import { Colors, hex } from "../gfx/color";
import { Svg } from "../gfx/svg";
import { H, MAX_SPEED, ordinal, overlap, PLAYER_COLORS, PLAYER_W, RacerDriver, RacerRival, steerSensitivity, W } from "./racer-driver";
import { RacerMap, RacerMarker, RacerSource } from "./racer-map";
import { Grey, HudYellow, RacerRenderer } from "./racer-renderer";
import { RacerTrack, SEG_LEN, THEMES } from "./racer-track";

/**
 * 4 人同場賽車。同一台 Stream Deck+ 上的 4 個 Racer 4P action 共用一個實例，
 * 每顆旋鈕一位玩家，觸控條每一格是那位玩家自己的車後視角（4 分割畫面），彼此看得到對方的車。
 * 開始畫面按旋鈕加入，第一個人加入後倒數 ENTRY_TIME 秒開跑（全員加入或點觸控條就馬上開跑）。
 * 沒有時間限制，比名次；第一名到終點後其他人還有 FINISH_WAIT 秒，沒到的依位置排名。
 */

export const SLOTS = 4;

const STAGE_SEGS = 1600;
const TRACK_SEED = 2024;
const FRAME_MS = 40; // 25 fps
const ENTRY_TIME = 10;
const FINISH_WAIT = 20;
const GRID_GAP = 600; // 前後排距離
const H_HUD = H - 14;

const Orange = hex(0xff8c20);
const Red = hex(0xff5030);

type GameState = "lobby" | "countdown" | "racing" | "paused" | "results";

export type Sink = (svg: string) => void;

class Player {
	readonly d = new RacerDriver();
	joined = false;
	finished = false;
	finishTime = 0;
	place = 0;
	sink?: Sink;
	lastSent?: string;
}

const now = () => performance.now() / 1000;

/** 1'23"45 */
function formatTime(t: number): string {
	const cs = Math.trunc(t * 100);
	const two = (v: number) => String(v).padStart(2, "0");
	return `${Math.trunc(cs / 6000)}'${two(Math.trunc(cs / 100) % 60)}"${two(cs % 100)}`;
}

export class RacerRaceGame implements RacerSource {
	// #region 共用實例（每台裝置一個）

	private static readonly games = new Map<string, RacerRaceGame>();
	private refCount = 0;

	static acquire(deviceId: string): RacerRaceGame {
		let game = RacerRaceGame.games.get(deviceId);
		if (!game) {
			game = new RacerRaceGame(deviceId);
			RacerRaceGame.games.set(deviceId, game);
		}
		game.refCount++;
		return game;
	}

	release(): void {
		if (--this.refCount > 0) return;
		RacerRaceGame.games.delete(this.deviceId);
		RacerMap.unregister(this);
		this.disposed = true;
		this.stopTimer();
	}

	// #endregion

	private readonly renderer = new RacerRenderer();
	private readonly players: Player[] = Array.from({ length: SLOTS }, () => new Player());

	private track!: RacerTrack;
	private state: GameState = "lobby";
	private pausedFrom: GameState = "racing";
	private countdown = 0;
	private entryTimer = -1;
	private raceTime = 0;
	private finishWait = -1;
	private finishCount = 0;
	private stages = 3;
	private traffic = "normal";

	private timer: ReturnType<typeof setInterval> | undefined;
	private lastFrame = 0;
	private disposed = false;

	private constructor(readonly deviceId: string) {
		this.buildTrack();
		this.placeGrid();
		RacerMap.register(this);
	}

	// #region 給 action 呼叫的介面

	attach(column: number, sink: Sink): void {
		this.players[column].sink = sink;
		this.players[column].lastSent = undefined;
		this.renderIfIdle();
	}

	detach(column: number, sink: Sink): void {
		const p = this.players[column];
		if (p.sink !== sink) return;
		p.sink = undefined;
		p.lastSent = undefined;
		if (this.state === "lobby") p.joined = false;
	}

	configure(column: number, sensitivity: string | undefined, steerMode: string | undefined): void {
		this.players[column].d.steerSens = steerSensitivity(sensitivity);
		this.players[column].d.autoCenter = steerMode !== "hold";
	}

	/** 比賽長度、路上車量，四個旋鈕共用，最後改的生效；比賽中改的下一場生效 */
	setRace(length: string | undefined, trafficLevel: string | undefined): void {
		const n = length === "short" ? 2 : length === "long" ? 5 : 3;
		const t = trafficLevel ?? "normal";
		if (n === this.stages && t === this.traffic) return;
		this.stages = n;
		this.traffic = t;
		if (this.state === "lobby") {
			this.buildTrack();
			this.placeGrid();
		}
		this.renderIfIdle();
	}

	rotate(column: number, ticks: number): void {
		const p = this.players[column];
		if (!p.joined || p.finished) return;
		if (this.state !== "countdown" && this.state !== "racing") return;
		p.d.rotate(ticks, now());
	}

	/** 旋鈕按下：加入 / 繼續 / 加速 */
	press(column: number): void {
		switch (this.state) {
			case "lobby":
				this.join(column);
				break;
			case "results":
				this.backToLobby();
				this.join(column);
				break;
			case "paused":
				this.resume();
				break;
			case "racing": {
				const p = this.players[column];
				if (p.joined && !p.finished && p.d.boost()) p.d.showMessage("BOOST!", undefined, 0.8);
				break;
			}
		}
		this.renderIfIdle();
	}

	/** 觸控條點一下：開始 / 暫停 / 繼續 */
	tap(): void {
		switch (this.state) {
			case "lobby":
				if (this.players.some((p) => p.joined)) this.startCountdown();
				break;
			case "results":
				this.backToLobby();
				break;
			case "paused":
				this.resume();
				break;
			default:
				this.pausedFrom = this.state;
				this.state = "paused";
				this.stopTimer();
				break;
		}
		this.renderIfIdle();
	}

	/** 觸控條長按：回到開始畫面 */
	reset(): void {
		this.backToLobby();
		this.renderIfIdle();
	}

	/** 給放在 1~4 以外位置的 action 用的提示畫面 */
	static renderMessage(line1: string, line2: string): string {
		const g = new Svg(W, H);
		g.clear(Colors.Black);
		g.drawStringIn(line1, { size: 14, bold: true }, Colors.White, { x: 0, y: 20, w: W, h: 30 }, "center", "center");
		g.drawStringIn(line2, { size: 10 }, Colors.Gray, { x: 0, y: 50, w: W, h: 30 }, "center", "center");
		return g.toString();
	}

	// #endregion

	// #region 地圖

	readonly mapPriority = 1;

	get mapActive(): boolean {
		return this.state !== "lobby" || this.players.some((p) => p.joined);
	}

	collectMarkers(markers: RacerMarker[]): { track: RacerTrack; status: string } {
		this.players.forEach((p, i) => {
			if (!p.joined) return;
			markers.push({
				color: PLAYER_COLORS[i],
				z: p.d.z,
				label: `P${i + 1}`,
				place: this.state === "lobby" ? 0 : p.place,
				finished: p.finished,
			});
		});
		let status: string;
		switch (this.state) {
			case "lobby":
				status = this.entryTimer >= 0 ? `ENTRY ${Math.ceil(this.entryTimer)}` : "ENTRY";
				break;
			case "countdown":
				status = "READY";
				break;
			case "results":
				status = "RESULT";
				break;
			case "paused":
				status = "PAUSED";
				break;
			default:
				status = formatTime(this.raceTime);
				break;
		}
		return { track: this.track, status };
	}

	// #endregion

	// #region 比賽流程

	private buildTrack(): void {
		const cars = this.traffic === "none" ? 0 : this.traffic === "heavy" ? 55 : 30;
		this.track = RacerTrack.build(TRACK_SEED, this.stages, STAGE_SEGS, cars, MAX_SPEED);
	}

	/** 起跑位置：旋鈕 1、2 第一排，3、4 第二排 */
	private placeGrid(): void {
		this.players.forEach((p, i) => {
			p.d.reset(i < 2 ? GRID_GAP : 0, i % 2 === 0 ? -0.5 : 0.5);
			p.finished = false;
			p.place = 0;
		});
	}

	private join(column: number): void {
		const p = this.players[column];
		if (p.joined) return;
		p.joined = true;

		const allIn = this.players.every((x) => !x.sink || x.joined);
		if (allIn) {
			this.startCountdown();
			return;
		}
		if (this.entryTimer < 0) this.entryTimer = ENTRY_TIME;
		this.startTimer();
	}

	private startCountdown(): void {
		// 路上的車重新排
		this.buildTrack();
		this.placeGrid();
		this.entryTimer = -1;
		this.raceTime = 0;
		this.finishWait = -1;
		this.finishCount = 0;
		this.state = "countdown";
		this.countdown = 3;
		this.startTimer();
	}

	private backToLobby(): void {
		this.stopTimer();
		this.state = "lobby";
		this.entryTimer = -1;
		for (const p of this.players) p.joined = false;
		this.buildTrack();
		this.placeGrid();
	}

	private resume(): void {
		this.state = this.pausedFrom;
		this.startTimer();
	}

	private startTimer(): void {
		if (this.disposed || this.timer !== undefined) return;
		this.lastFrame = now();
		this.timer = setInterval(() => this.onFrame(), FRAME_MS);
		this.onFrame();
	}

	private stopTimer(): void {
		if (this.timer !== undefined) clearInterval(this.timer);
		this.timer = undefined;
	}

	private onFrame(): void {
		if (this.timer === undefined) return;
		const t = now();
		const dt = t - this.lastFrame;
		this.lastFrame = t;
		this.update(Math.max(0, Math.min(0.1, dt)));
		this.renderAndSend();
	}

	private update(dt: number): void {
		const t = now();
		for (const p of this.players) if (p.joined) p.d.tick(dt, t);

		switch (this.state) {
			case "lobby":
				if (this.entryTimer >= 0) {
					this.entryTimer -= dt;
					if (this.entryTimer <= 0) this.startCountdown();
				}
				return;

			case "countdown":
				this.countdown -= dt;
				if (this.countdown <= 0) {
					this.state = "racing";
					for (const p of this.players) if (p.joined) p.d.showMessage("GO!", undefined, 0.8);
				}
				return;

			case "racing":
				break;

			default:
				return;
		}

		this.raceTime += dt;
		this.track.updateCars(dt);

		const last = this.track.checkpoint.length - 1;
		for (const p of this.players) {
			if (!p.joined) continue;
			p.d.drive(this.track, dt, !p.finished, 0.35);
			if (p.finished || !p.d.reachedCheckpoint(this.track)) continue;

			if (p.d.stage === last) {
				p.finished = true;
				p.finishTime = this.raceTime;
				p.place = ++this.finishCount;
				p.d.boostTimer = 0;
				p.d.showMessage("GOAL!", `${ordinal(p.place)}  ${formatTime(this.raceTime)}`, 99);
				if (this.finishWait < 0) this.finishWait = FINISH_WAIT;
			} else {
				p.d.nextStage();
				p.d.showMessage("CHECKPOINT!", THEMES[p.d.stage].name, 2.5);
			}
		}

		this.playerCollisions();
		this.updatePlaces();

		if (this.finishWait >= 0) {
			this.finishWait -= dt;
			if (this.finishWait <= 0 || this.players.every((p) => !p.joined || p.finished)) {
				this.state = "results";
				this.updatePlaces();
				this.stopTimer();
			}
		}
	}

	/** 玩家之間互撞：往兩邊推開，後面撞上來的減速 */
	private playerCollisions(): void {
		for (let i = 0; i < SLOTS; i++) {
			for (let j = i + 1; j < SLOTS; j++) {
				const a = this.players[i];
				const b = this.players[j];
				if (!a.joined || !b.joined || a.d.crashTimer > 0 || b.d.crashTimer > 0) continue;
				const dz = a.d.z - b.d.z;
				if (Math.abs(dz) > SEG_LEN * 0.9) continue;
				if (!overlap(a.d.x, PLAYER_W * 0.9, b.d.x, PLAYER_W * 0.9)) continue;

				const side = a.d.x >= b.d.x ? 1 : -1;
				a.d.x += side * 0.12;
				b.d.x -= side * 0.12;
				const rear = dz < 0 ? a.d : b.d;
				const front = dz < 0 ? b.d : a.d;
				if (rear.speed > front.speed) rear.speed = front.speed * 0.95;
				a.d.shakeTimer = b.d.shakeTimer = 0.25;
			}
		}
	}

	/** 名次：到終點的照到達順序，其他的照位置 */
	private updatePlaces(): void {
		let place = this.finishCount;
		const running = this.players.filter((p) => p.joined && !p.finished).sort((a, b) => b.d.z - a.d.z);
		for (const p of running) p.place = ++place;
	}

	// #endregion

	// #region 繪圖

	private renderIfIdle(): void {
		if (this.timer === undefined) this.renderAndSend();
	}

	renderAndSend(): void {
		if (this.disposed) return;
		this.players.forEach((p, i) => {
			if (!p.sink) return;
			const svg = this.renderSlot(i);
			if (svg === p.lastSent) return;
			p.lastSent = svg;
			p.sink(svg);
		});
	}

	private renderSlot(slot: number): string {
		const me = this.players[slot];
		let view = slot;

		// 比賽中沒加入的格子：看領先者的畫面
		const spectating = !me.joined && this.state !== "lobby";
		if (spectating) {
			const lead = this.players.filter((p) => p.joined).sort((a, b) => a.place - b.place)[0];
			if (lead) view = this.players.indexOf(lead);
		}

		const rivals: RacerRival[] = [];
		this.players.forEach((o, i) => {
			if (i === view) return;
			// 開始畫面也把其他格的車畫出來，看得到起跑排位
			if (!o.joined && this.state !== "lobby") return;
			rivals.push({ z: o.d.z, x: o.d.x, color: PLAYER_COLORS[i], lean: o.d.lean, crashTimer: o.d.crashTimer });
		});

		return this.renderer.render(this.track, this.players[view].d, PLAYER_COLORS[view], rivals, (r) => this.drawHud(r, slot, view, spectating));
	}

	private drawHud(r: RacerRenderer, slot: number, view: number, spectating: boolean): void {
		const me = this.players[slot];
		const v = this.players[view];
		const myColor = PLAYER_COLORS[slot];
		const tag = `P${slot + 1}`;

		switch (this.state) {
			case "lobby": {
				r.band(12, 56);
				r.textIn("SUNSET RACER 4P", r.big, Orange, 12, 18);
				if (me.joined) {
					r.textIn(`${tag} READY`, r.title, myColor, 28, 24);
				} else {
					r.textIn(tag, r.big, myColor, 28, 14);
					r.textIn("press dial to join", r.mid, Colors.White, 41, 13);
				}
				const entry = this.entryTimer >= 0 ? `start in ${Math.ceil(this.entryTimer)}   tap strip = go` : `${this.stages} stages`;
				r.textIn(entry, r.small, HudYellow, 55, 12);
				return;
			}

			case "results": {
				r.band(14, 72);
				if (me.joined) {
					r.textIn(ordinal(me.place), r.huge, myColor, 14, 30);
					r.textIn(me.finished ? `${tag}  ${formatTime(me.finishTime)}` : `${tag}  DNF`, r.mid, Colors.White, 42, 14);
				} else {
					r.textIn("RESULT", r.title, HudYellow, 16, 26);
				}
				const line = this.players
					.map((p, i) => ({ p, i }))
					.filter((x) => x.p.joined)
					.sort((a, b) => a.p.place - b.p.place)
					.map((x) => `${x.p.place}.P${x.i + 1}`)
					.join("  ");
				r.textIn(line, r.small, Grey, 58, 12);
				r.textIn("press dial for next race", r.small, Grey, 70, 12);
				return;
			}
		}

		// 比賽中的 HUD
		if (spectating) {
			r.text("WATCHING", r.small, Grey, 2, 0);
			r.text(`P${view + 1}`, r.big, PLAYER_COLORS[view], 2, 8);
		} else {
			r.text(this.state === "countdown" ? tag : ordinal(v.place), r.title, myColor, 2, -2);
			r.text(tag, r.small, Colors.White, 4, 18);
		}
		r.text("TIME", r.small, HudYellow, W / 2, 0, "center");
		r.text(formatTime(this.raceTime), r.mid, HudYellow, W / 2, 8, "center");
		r.stageBar(`STAGE ${Math.min(v.d.stage, this.stages - 1) + 1}/${this.stages}`, v.d.stageProgress(this.track));
		r.speedo(v.d.kmh);
		r.boostPips(v.d, now());

		if (this.state === "countdown") {
			r.textIn(String(Math.ceil(this.countdown)), r.huge, HudYellow, 22, 32);
			return;
		}
		if (this.state === "paused") {
			r.band(30, 30);
			r.textIn("PAUSED", r.big, Colors.White, 30, 18);
			r.textIn("tap strip to resume", r.small, Grey, 46, 12);
			return;
		}

		if (!spectating) r.message(me.d, now());
		if (this.finishWait >= 0 && !v.finished) r.text(`FINISH IN ${Math.ceil(this.finishWait)}`, r.mid, Red, W - 3, H_HUD, "far");
	}

	// #endregion
}
