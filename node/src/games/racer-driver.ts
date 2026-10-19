import { Color, hex } from "../gfx/color";
import { carWidth, RacerSegment, RacerTrack, SEG_LEN, spriteHitRatio, spriteWidth } from "./racer-track";

// #region 賽車共用的常數：畫面投影與車子物理

export const W = 200;
export const H = 100;
/** 平路時地平線的 y */
export const HORIZON = 40;
/** 投影比例 */
export const K = W / 2;
/** 半個路寬 */
export const ROAD_W = 2000;
export const CAM_H = 1000;
/** 玩家車子在相機前方多遠，剛好是路面碰到畫面底部的地方 */
export const PLAYER_Z = (CAM_H * K) / (H - HORIZON);
/** 玩家車子在 PLAYER_Z 時畫出來的寬度（px） */
export const PLAYER_CAR_W = 50.4;

/** 每秒 60 段 */
export const MAX_SPEED = SEG_LEN * 60;
const TOP_KMH = 293;
/** 以半個路寬為單位 */
export const PLAYER_W = 0.42;
/** 方向盤打滿時每秒橫移幾個半路寬 */
const STEER_RATE = 2.6;
const CENTRIFUGAL = 0.3;
const OFFROAD_LIMIT = MAX_SPEED * 0.3;
export const CRASH_TIME = 1.6;
/** 停止轉動多久後方向盤開始回正 */
const STEER_CENTER_DELAY = 0.18;
const STEER_CENTER_RATE = 3.0;
/** 每場比賽可以加速幾次 */
export const BOOSTS = 3;
/** 加速維持幾秒 */
const BOOST_TIME = 1.5;
const BOOST_SPEED = MAX_SPEED * 1.35;

export const PLAYER_COLORS: readonly Color[] = [hex(0xd81018), hex(0x2060e0), hex(0xe8b810), hex(0x20a040)];

export function steerSensitivity(s: string | undefined): number {
	switch (s) {
		case "low":
			return 0.18;
		case "high":
			return 0.4;
		default:
			return 0.28;
	}
}

export function ordinal(place: number): string {
	switch (place) {
		case 1:
			return "1st";
		case 2:
			return "2nd";
		case 3:
			return "3rd";
		default:
			return `${place}th`;
	}
}

// #endregion

/**
 * 一台玩家車子的狀態與物理：方向盤、油門、加速、離心力、出界、撞景物、撞路上的車。
 */
export class RacerDriver {
	/** 相機的 z，車子在 position + PLAYER_Z */
	position = 0;
	/** 橫向位置，-1 ~ 1 是路面 */
	x = 0;
	speed = 0;
	/** -1 ~ 1 */
	steer = 0;
	lastSteerTime = -99;
	boosts = BOOSTS;
	boostTimer = 0;
	crashTimer = 0;
	shakeTimer = 0;
	farOffset = 0;
	nearOffset = 0;

	stage = 0;
	bgFrom = 0;
	bgTo = 0;
	/** 0 → 1，換關時背景淡入 */
	bgFade = 1;

	steerSens = 0.28;
	autoCenter = true;

	msg1?: string;
	msg2?: string;
	msgTimer = 0;

	get z(): number {
		return this.position + PLAYER_Z;
	}

	get kmh(): number {
		return (this.speed / MAX_SPEED) * TOP_KMH;
	}

	get offRoad(): boolean {
		return Math.abs(this.x) > 1.05;
	}

	get lean(): number {
		return this.steer < -0.3 ? -1 : this.steer > 0.3 ? 1 : 0;
	}

	get boosting(): boolean {
		return this.boostTimer > 0;
	}

	reset(position = 0, x = 0): void {
		this.position = position;
		this.x = x;
		this.speed = 0;
		this.steer = 0;
		this.boosts = BOOSTS;
		this.boostTimer = 0;
		this.crashTimer = 0;
		this.shakeTimer = 0;
		this.farOffset = this.nearOffset = 0;
		this.stage = 0;
		this.bgFrom = this.bgTo = 0;
		this.bgFade = 1;
		this.msgTimer = 0;
	}

	rotate(ticks: number, now: number): void {
		this.steer = Math.max(-1, Math.min(1, this.steer + ticks * this.steerSens));
		this.lastSteerTime = now;
	}

	/** 瞬間加速，翻車中不能用。用掉一次回傳 true */
	boost(): boolean {
		if (this.boosts <= 0 || this.crashTimer > 0) return false;
		this.boosts--;
		this.boostTimer = BOOST_TIME;
		this.speed = BOOST_SPEED;
		this.shakeTimer = 0.2;
		return true;
	}

	showMessage(m1: string, m2: string | undefined, seconds: number): void {
		this.msg1 = m1;
		this.msg2 = m2;
		this.msgTimer = seconds;
	}

	/** 計時器、方向盤回正，倒數時也要跑 */
	tick(dt: number, now: number): void {
		if (this.msgTimer > 0) this.msgTimer -= dt;
		if (this.shakeTimer > 0) this.shakeTimer -= dt;
		if (this.bgFade < 1) this.bgFade = Math.min(1, this.bgFade + dt / 1.5);

		if (this.autoCenter && now - this.lastSteerTime > STEER_CENTER_DELAY) {
			const step = STEER_CENTER_RATE * dt;
			this.steer = Math.abs(this.steer) <= step ? 0 : this.steer - Math.sign(this.steer) * step;
		}
	}

	/** 一幀的物理。canAccel = false 時鬆油門滑行，coastDecel 是滑行減速度（相對極速 / 秒） */
	drive(track: RacerTrack, dt: number, canAccel: boolean, coastDecel: number): void {
		const MAX = MAX_SPEED;
		const seg = track.segmentAt(this.z);
		const speedPct = this.speed / MAX;
		const startPos = this.position;

		this.position += this.speed * dt;

		if (this.crashTimer > 0) {
			// 翻車中：不能開，慢慢把車放回路邊
			this.crashTimer -= dt;
			this.speed = 0;
			const target = Math.max(-0.8, Math.min(0.8, this.x));
			this.x += (target - this.x) * Math.min(1, dt * 3);
		} else {
			// 方向盤，太慢的時候轉不太動
			if (this.speed > 0) this.x += dt * STEER_RATE * this.steer * Math.min(1, 0.25 + speedPct);
			// 彎道把車往外甩
			this.x -= dt * speedPct * speedPct * seg.curve * CENTRIFUGAL;

			if (this.boostTimer > 0) {
				// 加速中維持超過極速
				this.boostTimer -= dt;
				this.speed = Math.max(this.speed, BOOST_SPEED * 0.9);
			} else if (this.speed > MAX) {
				this.speed = Math.max(MAX, this.speed - MAX * 0.6 * dt); // 加速完慢慢降回極速
			} else if (canAccel) {
				this.speed += MAX * (0.22 - 0.12 * speedPct) * dt; // 0 到極速大約 6.5 秒
			} else {
				this.speed -= MAX * coastDecel * dt;
			}

			if (this.offRoad && this.speed > OFFROAD_LIMIT) {
				this.boostTimer = 0;
				this.speed = Math.max(OFFROAD_LIMIT, this.speed - MAX * 0.7 * dt);
			}

			if (this.offRoad) this.checkSpriteHit(seg);
			this.checkCarHit(seg);
		}

		this.x = Math.max(-2.6, Math.min(2.6, this.x));
		this.speed = Math.max(0, Math.min(BOOST_SPEED, this.speed));

		const segMoved = (this.position - startPos) / SEG_LEN;
		this.farOffset += seg.curve * segMoved * 0.12;
		this.nearOffset += seg.curve * segMoved * 0.3;
	}

	reachedCheckpoint(track: RacerTrack): boolean {
		return this.stage < track.checkpoint.length && Math.trunc(this.z / SEG_LEN) >= track.checkpoint[this.stage];
	}

	/** 過了檢查點，進下一關（背景淡入下一關的配色） */
	nextStage(): void {
		this.stage++;
		this.bgFrom = this.bgTo;
		this.bgTo = this.stage;
		this.bgFade = 0;
	}

	/** 這一關跑了幾成，0 ~ 1 */
	stageProgress(track: RacerTrack): number {
		const s = Math.min(this.stage, track.checkpoint.length - 1);
		const from = track.stageStart[s];
		const to = track.checkpoint[s];
		return Math.max(0, Math.min(1, (this.z / SEG_LEN - from) / (to - from)));
	}

	crash(): void {
		this.crashTimer = CRASH_TIME;
		this.speed = 0;
		this.boostTimer = 0;
		this.shakeTimer = 0.5;
	}

	private checkSpriteHit(seg: RacerSegment): void {
		if (!seg.sprites) return;
		for (const sp of seg.sprites) {
			const sw = spriteWidth(sp.type) / ROAD_W;
			const sx = sp.offset + (sw / 2) * (sp.offset > 0 ? 1 : -1);
			if (!overlap(this.x, PLAYER_W * 0.8, sx, sw * spriteHitRatio(sp.type))) continue;

			if (this.speed > MAX_SPEED * 0.25) {
				this.crash();
			} else {
				// 慢慢撞到就停住，退一點
				this.speed = 0;
				this.position = Math.max(0, this.position - SEG_LEN * 0.5);
				this.shakeTimer = 0.3;
			}
			return;
		}
	}

	private checkCarHit(seg: RacerSegment): void {
		if (!seg.cars) return;
		for (const car of seg.cars) {
			const cw = carWidth(car.type) / ROAD_W;
			if (this.speed <= car.speed || !overlap(this.x, PLAYER_W * 0.85, car.offset, cw * 0.85)) continue;

			if (this.speed - car.speed > MAX_SPEED * 0.45) {
				this.crash();
			} else {
				this.speed = car.speed * 0.7;
				this.boostTimer = 0;
				this.shakeTimer = 0.4;
			}
			this.position = car.z - PLAYER_Z - SEG_LEN * 0.3;
			return;
		}
	}
}

export function overlap(x1: number, w1: number, x2: number, w2: number): boolean {
	return !(x1 + w1 / 2 < x2 - w2 / 2 || x1 - w1 / 2 > x2 + w2 / 2);
}

/** 畫在別人畫面裡的對手車 */
export type RacerRival = {
	readonly z: number;
	readonly x: number;
	readonly color: Color;
	readonly lean: number;
	readonly crashTimer: number;
};
