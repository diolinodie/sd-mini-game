import type { DialGame } from "../actions/dial-game";
import { Color, Colors, hex } from "../gfx/color";
import { MAX_SPEED, PLAYER_COLORS, RacerDriver, steerSensitivity, W } from "./racer-driver";
import { RacerMap, RacerMarker, RacerSource } from "./racer-map";
import { Grey, HudBlue, HudYellow, RacerRenderer } from "./racer-renderer";
import { RacerTrack, THEMES } from "./racer-track";

/**
 * 單人版復古街機風格賽車，畫在一格觸控條 200x100 上。
 * 油門自動踩，轉旋鈕 = 方向盤，按旋鈕 = 瞬間加速（每場 3 次）。
 * 五關各有不同景色，每關結尾的檢查點加時間，時間用完就結束，跑完第五關到終點。
 * 車子物理在 RacerDriver，畫面在 RacerRenderer。
 */

type GameState = "idle" | "countdown" | "playing" | "paused" | "timeUp" | "goal" | "gameOver" | "finished";

const STAGES = 5;
const STAGE_SEGS = 2100;
const TRACK_SEED = 1986;
const FRAME_MS = 40; // 25 fps

const Orange = hex(0xff8c20);
const Red = hex(0xff5030);

const now = () => performance.now() / 1000;

export class RacerGame implements DialGame, RacerSource {
	/** 破紀錄時呼叫，讓外面存進設定 */
	onHiScore?: (best: number) => void;

	private readonly driver = new RacerDriver();
	private readonly renderer = new RacerRenderer();
	private readonly carColor: Color = PLAYER_COLORS[0];

	private track!: RacerTrack;
	private state: GameState = "idle";
	private pausedFrom: GameState = "playing";

	private timeLeft = 0;
	private countdown = 0;
	private stateTimer = 0;
	private score = 0;
	private hiScore = 0;
	private timeBonus = 0;
	private difficulty = "normal";

	private timer: ReturnType<typeof setInterval> | undefined;
	private lastFrame = 0;
	private disposed = false;

	constructor(
		readonly deviceId: string,
		private readonly column: number,
		private readonly draw: (svg: string) => void,
	) {
		this.buildTrack();
		this.resetRun();
		RacerMap.register(this);
	}

	dispose(): void {
		RacerMap.unregister(this);
		this.disposed = true;
		this.stopTimer();
	}

	// #region 地圖

	readonly mapPriority = 0;

	get mapActive(): boolean {
		return this.state !== "idle";
	}

	collectMarkers(markers: RacerMarker[]): { track: RacerTrack } {
		const slot = this.column >= 0 && this.column < PLAYER_COLORS.length ? this.column : 0;
		markers.push({
			color: PLAYER_COLORS[slot],
			z: this.driver.z,
			label: String(this.column + 1),
			place: 0,
			finished: this.state === "goal" || this.state === "finished",
		});
		return { track: this.track };
	}

	// #endregion

	// #region 對外操作

	configure(sensitivity: string | undefined, steerMode: string | undefined, diff: string | undefined, best: number): void {
		this.driver.steerSens = steerSensitivity(sensitivity);
		this.driver.autoCenter = steerMode !== "hold";
		if (best > this.hiScore) this.hiScore = best;

		diff = diff ?? "normal";
		if (diff !== this.difficulty) {
			this.difficulty = diff;
			// 比賽中不換賽道，下一場生效
			if (this.state === "idle") {
				this.buildTrack();
				this.resetRun();
			}
		}
	}

	rotate(ticks: number): void {
		const s = this.state;
		if (s === "idle" || s === "gameOver" || s === "finished" || s === "paused") return;
		this.driver.rotate(ticks, now());
		if (this.timer === undefined) this.render();
	}

	/** 旋鈕按下：開始 / 繼續 / 加速 */
	press(): void {
		switch (this.state) {
			case "idle":
			case "gameOver":
			case "finished":
				this.newGame();
				break;
			case "paused":
				this.resume();
				break;
			case "playing":
				if (this.driver.boost()) this.driver.showMessage("BOOST!", undefined, 0.8);
				break;
		}
		this.render();
	}

	/** 觸控條點一下 = 暫停 / 繼續 / 開始，長按 = 回到標題畫面 */
	touch(hold: boolean): void {
		if (hold) {
			this.stopTimer();
			this.buildTrack();
			this.resetRun();
			this.state = "idle";
		} else {
			switch (this.state) {
				case "idle":
				case "gameOver":
				case "finished":
					this.newGame();
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
		}
		this.render();
	}

	// #endregion

	// #region 遊戲流程

	private settingsFor(): { startTime: number; extend: number; cars: number } {
		switch (this.difficulty) {
			case "easy":
				return { startTime: 75, extend: 62, cars: 25 };
			case "hard":
				return { startTime: 52, extend: 46, cars: 60 };
			default:
				return { startTime: 62, extend: 53, cars: 40 };
		}
	}

	private buildTrack(): void {
		this.track = RacerTrack.build(TRACK_SEED, STAGES, STAGE_SEGS, this.settingsFor().cars, MAX_SPEED);
	}

	private resetRun(): void {
		this.driver.reset();
		this.score = 0;
		this.timeBonus = 0;
		this.timeLeft = this.settingsFor().startTime;
	}

	private newGame(): void {
		this.stopTimer();
		// 路上的車開過一場就亂掉了，重建一次
		this.buildTrack();
		this.resetRun();
		this.state = "countdown";
		this.countdown = 3.0;
		this.startTimer();
	}

	private resume(): void {
		this.state = this.pausedFrom;
		this.startTimer();
	}

	private startTimer(): void {
		this.stopTimer();
		if (this.disposed) return;
		this.lastFrame = now();
		this.timer = setInterval(() => this.onFrame(), FRAME_MS);
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
		this.render();
	}

	private update(dt: number): void {
		const d = this.driver;
		d.tick(dt, now());

		if (this.state === "countdown") {
			this.countdown -= dt;
			if (this.countdown <= 0) {
				this.state = "playing";
				d.showMessage("GO!", undefined, 0.8);
			}
			return;
		}

		this.track.updateCars(dt);
		d.drive(this.track, dt, this.state === "playing", this.state === "goal" ? 0.35 : 0.25);

		if (this.state === "playing") {
			this.score += Math.trunc(d.kmh * dt * 40);
			this.timeLeft -= dt;
			if (this.timeLeft <= 0) {
				this.timeLeft = 0;
				this.state = "timeUp";
				this.stateTimer = 0;
				d.boostTimer = 0;
			}
		}

		if (this.state === "playing" && d.reachedCheckpoint(this.track)) this.passCheckpoint();

		// 時間到 / 到終點之後滑行到停下來
		if (this.state === "timeUp" || this.state === "goal") {
			if (d.speed <= 0) this.stateTimer += dt;
			if (this.stateTimer > 1.2) {
				this.state = this.state === "goal" ? "finished" : "gameOver";
				this.updateHiScore();
				this.stopTimer();
			}
		}
	}

	private passCheckpoint(): void {
		const d = this.driver;
		if (d.stage === STAGES - 1) {
			this.timeBonus = Math.trunc(this.timeLeft * 10) * 10000;
			this.score += this.timeBonus;
			this.state = "goal";
			this.stateTimer = 0;
			d.boostTimer = 0;
			d.showMessage("GOAL!", `TIME BONUS ${this.timeBonus}`, 99);
			return;
		}

		this.timeLeft += Math.max(30, this.settingsFor().extend - d.stage * 2);
		d.nextStage();
		d.showMessage("CHECKPOINT!", "TIME EXTENDED", 2.5);
	}

	private updateHiScore(): void {
		if (this.score <= this.hiScore) return;
		this.hiScore = this.score;
		this.onHiScore?.(this.hiScore);
	}

	// #endregion

	// #region 繪圖

	render(): void {
		if (this.disposed) return;
		this.draw(this.renderer.render(this.track, this.driver, this.carColor, undefined, (r) => this.drawHud(r)));
	}

	private drawHud(r: RacerRenderer): void {
		const d = this.driver;
		if (this.state !== "idle") {
			r.text("SCORE", r.small, HudBlue, 2, 0);
			r.text(String(this.score), r.small, Colors.White, 2, 8);

			r.text("TIME", r.small, HudYellow, W / 2, 0, "center");
			const secs = Math.ceil(this.timeLeft);
			const warn = secs <= 10 && this.state === "playing";
			const tc = warn && Math.trunc(now() * 4) % 2 === 0 ? hex(0xff4030) : HudYellow;
			r.text(String(secs), r.big, tc, W / 2, 7, "center");

			r.stageBar(`STAGE ${Math.min(d.stage, STAGES - 1) + 1}`, d.stageProgress(this.track));
			r.speedo(d.kmh);
			r.boostPips(d, now());
		}

		switch (this.state) {
			case "idle":
				r.band(24, 44);
				r.textIn("SUNSET RACER", r.title, Orange, 24, 26);
				r.textIn("press dial to start", r.mid, hex(0xd0d0d0), 48, 18);
				if (this.hiScore > 0) r.text(`HI ${this.hiScore}`, r.small, HudYellow, W / 2, 2, "center");
				return;

			case "countdown":
				r.textIn(String(Math.ceil(this.countdown)), r.huge, HudYellow, 22, 32);
				r.textIn(`STAGE 1  ${THEMES[0].name}`, r.small, Colors.White, 52, 12);
				return;

			case "paused":
				r.band(30, 30);
				r.textIn("PAUSED", r.big, Colors.White, 30, 18);
				r.textIn("tap strip to resume", r.small, Grey, 46, 12);
				return;

			case "timeUp":
				r.textIn("TIME UP", r.title, Red, 28, 26);
				return;

			case "gameOver":
				r.band(24, 44);
				r.textIn("GAME OVER", r.title, Red, 24, 24);
				r.textIn(`score ${this.score}   hi ${this.hiScore}`, r.small, Colors.White, 46, 11);
				r.textIn("press dial to retry", r.small, Grey, 56, 11);
				return;

			case "finished":
				r.band(22, 48);
				r.textIn("CONGRATULATIONS!", r.big, HudYellow, 22, 18);
				r.textIn(`time bonus ${this.timeBonus}`, r.small, Colors.White, 39, 11);
				r.textIn(`score ${this.score}   hi ${this.hiScore}`, r.small, Colors.White, 49, 11);
				r.textIn("press dial to play again", r.small, Grey, 59, 11);
				return;
		}

		r.message(d, now());
	}

	// #endregion
}
