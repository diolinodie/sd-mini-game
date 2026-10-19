import { Color, hex } from "../gfx/color";
import { NetRandom } from "./net-random";

export type RacerSprite = "palm" | "tree" | "pine" | "bush" | "cactus" | "rock" | "billboard" | "lamp";

export type RacerGate = "none" | "start" | "checkpoint" | "goal";

export type RacerCarType = "coupe" | "beetle" | "truck";

export type RacerSpriteItem = {
	readonly type: RacerSprite;
	/** 以半個路寬為單位，負數在左邊；左邊的物件右緣對齊這個位置，右邊的左緣對齊 */
	readonly offset: number;
	readonly variant: number;
};

export type RacerCar = {
	readonly type: RacerCarType;
	/** 車子中心，以半個路寬為單位 */
	readonly offset: number;
	z: number;
	speed: number;
	readonly color: Color;
	segment: RacerSegment;
};

/**
 * 路的一小段（長 SEG_LEN）。y1 → y2 是前後兩端的高度，curve 是這段的彎度。
 * x1 / screenY1 / w1 / scale1 … 是每一幀投影出來的結果，畫圖時用。
 */
export class RacerSegment {
	gate: RacerGate = "none";
	sprites?: RacerSpriteItem[];
	cars?: RacerCar[];

	x1 = 0;
	screenY1 = 0;
	w1 = 0;
	scale1 = 0;
	camZ1 = 0;
	x2 = 0;
	screenY2 = 0;
	w2 = 0;
	scale2 = 0;
	clip = 0;
	fog = 0;
	visible = false;

	constructor(
		readonly index: number,
		readonly y1: number,
		readonly y2: number,
		readonly curve: number,
		readonly dark: boolean,
		readonly stage: number,
	) {}
}

/** 每一關的配色、背景、路邊景物 */
export type RacerTheme = {
	readonly name: string;
	readonly skyTop: Color;
	readonly skyBottom: Color;
	readonly sea?: boolean;
	readonly seaColor?: Color;
	readonly sun?: boolean;
	readonly sunColor?: Color;
	readonly clouds?: boolean;
	readonly far: Color;
	readonly farCap?: Color;
	readonly near?: Color;
	readonly grassLight: Color;
	readonly grassDark: Color;
	readonly rumbleLight: Color;
	readonly rumbleDark: Color;
	readonly roadLight: Color;
	readonly roadDark: Color;
	readonly lane: Color;
	readonly fog: Color;
	readonly foliage: Color;
	readonly scenery: readonly RacerSprite[];
	/** 沿路兩排整齊的景物（海灘的椰子樹） */
	readonly rows?: RacerSprite;
	/** 遠山輪廓，週期 BG_PERIOD px */
	farHeights: number[];
	/** 近丘輪廓 */
	nearHeights: number[];
};

export const BG_PERIOD = 256;

export const THEMES: readonly RacerTheme[] = createThemes();

function createThemes(): RacerTheme[] {
	const t: RacerTheme[] = [
		{
			name: "PALM BEACH",
			skyTop: hex(0x2e7be0),
			skyBottom: hex(0xb8e2ff),
			sea: true,
			seaColor: hex(0x1f63c6),
			clouds: true,
			far: hex(0x4f8f6a),
			grassLight: hex(0xf0dca6),
			grassDark: hex(0xe6cf94),
			rumbleLight: hex(0xf4f4f4),
			rumbleDark: hex(0xe02424),
			roadLight: hex(0x9a9a9e),
			roadDark: hex(0x929296),
			lane: hex(0xf4f4f4),
			fog: hex(0xc8e6ff),
			foliage: hex(0x2e9a3a),
			scenery: ["palm", "bush", "palm", "billboard"],
			rows: "palm",
			farHeights: [],
			nearHeights: [],
		},
		{
			name: "SUNSET COAST",
			skyTop: hex(0x3a1f6b),
			skyBottom: hex(0xff9a4a),
			sun: true,
			sunColor: hex(0xffd24a),
			far: hex(0x6a2e5e),
			near: hex(0x2c3d2c),
			grassLight: hex(0x4c8c3a),
			grassDark: hex(0x448034),
			rumbleLight: hex(0xf4f4f4),
			rumbleDark: hex(0xe02424),
			roadLight: hex(0x7e7880),
			roadDark: hex(0x767078),
			lane: hex(0xf4f4f4),
			fog: hex(0xffb070),
			foliage: hex(0x2e7a34),
			scenery: ["palm", "lamp", "bush", "billboard"],
			rows: "lamp",
			farHeights: [],
			nearHeights: [],
		},
		{
			name: "DESERT",
			skyTop: hex(0x4a9ce8),
			skyBottom: hex(0xffe2a8),
			far: hex(0xc06a3a),
			near: hex(0xd89050),
			grassLight: hex(0xe8b068),
			grassDark: hex(0xdea45c),
			rumbleLight: hex(0xf4f4f4),
			rumbleDark: hex(0xe02424),
			roadLight: hex(0x8c847c),
			roadDark: hex(0x847c74),
			lane: hex(0xf4f4f4),
			fog: hex(0xffe4b8),
			foliage: hex(0x6a8a3a),
			scenery: ["cactus", "rock", "cactus", "billboard"],
			farHeights: [],
			nearHeights: [],
		},
		{
			name: "ALPINE PASS",
			skyTop: hex(0x3d7dd8),
			skyBottom: hex(0xd4ecff),
			clouds: true,
			far: hex(0x6c7c9c),
			farCap: hex(0xf4f8ff),
			near: hex(0x2e6a3a),
			grassLight: hex(0x3e9a46),
			grassDark: hex(0x368c3e),
			rumbleLight: hex(0xf4f4f4),
			rumbleDark: hex(0xe02424),
			roadLight: hex(0x888890),
			roadDark: hex(0x808088),
			lane: hex(0xf4f4f4),
			fog: hex(0xddf0ff),
			foliage: hex(0x2e8a3a),
			scenery: ["pine", "pine", "rock", "bush"],
			farHeights: [],
			nearHeights: [],
		},
		{
			name: "AUTUMN ROAD",
			skyTop: hex(0x2a1e5a),
			skyBottom: hex(0xf07c6a),
			sun: true,
			sunColor: hex(0xff8050),
			far: hex(0x502e60),
			near: hex(0x7a3a2a),
			grassLight: hex(0x9c7a34),
			grassDark: hex(0x90702e),
			rumbleLight: hex(0xf4f4f4),
			rumbleDark: hex(0xe02424),
			roadLight: hex(0x74707c),
			roadDark: hex(0x6c6874),
			lane: hex(0xf4f4f4),
			fog: hex(0xe88a78),
			foliage: hex(0xe07a28),
			scenery: ["tree", "tree", "lamp", "billboard"],
			farHeights: [],
			nearHeights: [],
		},
	];

	for (let i = 0; i < t.length; i++) {
		const rng = new NetRandom(7919 * (i + 1));
		if (t[i].sea) t[i].farHeights = silhouette(rng, 3, 7, -3); // 海上的小島：大部分是 0
		else if (i === 2) t[i].farHeights = mesas(rng);
		else t[i].farHeights = silhouette(rng, 9, 8, 0);
		t[i].nearHeights = silhouette(rng, 3, 3, 0);
	}
	return t;
}

/** C# 的 Math.Round 是銀行家捨入（.5 取偶數），這裡跟著做，輪廓才會一模一樣 */
function roundEven(v: number): number {
	const r = Math.round(v);
	return Math.abs(v % 1) === 0.5 && r % 2 !== 0 ? r - 1 : r;
}

/** 幾個整數頻率的 sin 疊起來，週期剛好是 BG_PERIOD，左右接得起來 */
function silhouette(rng: NetRandom, baseH: number, amp: number, floor: number): number[] {
	const h = new Array<number>(BG_PERIOD);
	const freq = [1, 2, 3, 5, 8];
	const phase: number[] = [];
	const a: number[] = [];
	for (let k = 0; k < freq.length; k++) {
		phase[k] = rng.nextDouble() * Math.PI * 2;
		a[k] = (amp / (k + 1)) * (0.6 + rng.nextDouble() * 0.8);
	}
	for (let x = 0; x < BG_PERIOD; x++) {
		let v = baseH;
		for (let k = 0; k < freq.length; k++) v += a[k] * Math.sin((2 * Math.PI * freq[k] * x) / BG_PERIOD + phase[k]);
		h[x] = Math.max(0, roundEven(Math.max(floor, v)));
	}
	return h;
}

/** 沙漠的平頂台地 */
function mesas(rng: NetRandom): number[] {
	const h = new Array<number>(BG_PERIOD).fill(2);
	let pos = 0;
	while (pos < BG_PERIOD - 20) {
		pos += rng.next(8, 30);
		const width = rng.next(14, 40);
		const height = rng.next(8, 18);
		for (let x = 0; x < width && pos + x < BG_PERIOD; x++) {
			const edge = Math.min(x, width - 1 - x);
			const v = edge < 3 ? Math.trunc((height * (edge + 1)) / 4) : height;
			h[pos + x] = Math.max(h[pos + x], v);
		}
		pos += width;
	}
	return h;
}

export const SEG_LEN = 200;
const RUMBLE_LEN = 3;
/** 起跑門在第幾段（玩家車子在第 8 段左右） */
export const START_GATE = 30;
/** 路上車子的速度，相對極速 */
const CAR_SPEED_MIN = 0.32;
const CAR_SPEED_MAX = 0.58;

const CarColors = [hex(0xf0f0f0), hex(0x3060d0), hex(0xf0c820), hex(0x30a050), hex(0x303038), hex(0xe07020), hex(0x8040b0), hex(0x60c0e0)];

export function spriteWidth(s: RacerSprite): number {
	switch (s) {
		case "palm":
			return 1100;
		case "tree":
			return 1300;
		case "pine":
			return 900;
		case "bush":
			return 900;
		case "cactus":
			return 500;
		case "rock":
			return 1200;
		case "billboard":
			return 1800;
		case "lamp":
			return 500;
	}
}

export function spriteHeight(s: RacerSprite): number {
	switch (s) {
		case "palm":
			return 2400;
		case "tree":
			return 1900;
		case "pine":
			return 2000;
		case "bush":
			return 500;
		case "cactus":
			return 1100;
		case "rock":
			return 800;
		case "billboard":
			return 1300;
		case "lamp":
			return 2000;
	}
}

/** 撞得到的寬度佔圖寬的比例（椰子樹只有樹幹撞得到） */
export function spriteHitRatio(s: RacerSprite): number {
	switch (s) {
		case "palm":
			return 0.25;
		case "tree":
			return 0.3;
		case "pine":
			return 0.35;
		case "lamp":
			return 0.3;
		case "bush":
			return 0.7;
		default:
			return 0.85;
	}
}

export function carWidth(t: RacerCarType): number {
	return t === "beetle" ? 720 : t === "truck" ? 900 : 820;
}

export function carHeight(t: RacerCarType): number {
	return t === "beetle" ? 520 : t === "truck" ? 1050 : 440;
}

const MAP_TURN = 0.0016; // 每段的彎度換算成俯視圖上轉幾弧度

export type MapPoint = { readonly x: number; readonly y: number };

const easeIn = (a: number, b: number, p: number) => a + (b - a) * p * p;
const easeInOut = (a: number, b: number, p: number) => a + (b - a) * (-Math.cos(p * Math.PI) / 2 + 0.5);

/**
 * 一整場比賽的賽道：好幾關接在一起，每關結尾有檢查點，最後一關結尾是終點。
 * 做法跟一般的 pseudo-3D 賽車一樣：路是一串等長的小段，每段有彎度跟高度。
 */
export class RacerTrack {
	readonly segments: RacerSegment[] = [];
	readonly cars: RacerCar[] = [];
	/** 每一關第一段的 index */
	stageStart: number[] = [];
	/** 每一關的檢查點（最後一個是終點）在哪一段 */
	checkpoint: number[] = [];

	private readonly rng: NetRandom;
	private lastY = 0;
	private curStage = 0;
	private mapPoints?: MapPoint[];
	private mapBounds?: { x: number; y: number; w: number; h: number };

	private constructor(seed: number) {
		this.rng = new NetRandom(seed);
	}

	get length(): number {
		return this.segments.length * SEG_LEN;
	}

	get goalIndex(): number {
		return this.checkpoint[this.checkpoint.length - 1];
	}

	segmentAt(z: number): RacerSegment {
		const n = this.segments.length;
		let i = Math.floor(z / SEG_LEN) % n;
		if (i < 0) i += n;
		return this.segments[i];
	}

	static build(seed: number, stages: number, stageSegs: number, carsPerStage: number, maxSpeed: number): RacerTrack {
		const t = new RacerTrack(seed);

		for (let s = 0; s < stages; s++) {
			t.curStage = s;
			t.stageStart[s] = t.segments.length;
			if (s === 0) t.addStraight(50);

			const difficulty = 0.8 + 0.1 * s;
			while (t.segments.length - t.stageStart[s] < stageSegs) t.addRandomSection(difficulty);

			// 檢查點前把路拉平、拉直
			t.addRoad(25, 25, 25, 0, -t.lastY / SEG_LEN);
			t.addStraight(20);

			t.checkpoint[s] = t.segments.length;
			if (s < stages - 1) {
				// 過了檢查點就是下一關的配色
				t.curStage = s + 1;
				t.addStraight(30);
			} else {
				// 終點後面留一大段直路，衝線後滑行用，也避免畫到賽道開頭
				t.addStraight(400);
			}
		}

		t.segments[START_GATE].gate = "start";
		for (let s = 0; s < stages; s++) t.segments[t.checkpoint[s]].gate = s === stages - 1 ? "goal" : "checkpoint";

		t.placeScenery();
		t.spawnCars(stages, carsPerStage, maxSpeed);
		return t;
	}

	// #region 俯視地圖

	/** 每一段在俯視地圖上的位置（任意單位），彎度累加出來的 */
	getMapPoints(): MapPoint[] {
		if (!this.mapPoints) this.buildMap();
		return this.mapPoints!;
	}

	/** 起點到終點的範圍（不含終點後滑行用的直路） */
	getMapBounds(): { x: number; y: number; w: number; h: number } {
		if (!this.mapPoints) this.buildMap();
		return this.mapBounds!;
	}

	private buildMap(): void {
		const pts: MapPoint[] = [];
		let heading = -Math.PI / 2;
		let x = 0;
		let y = 0;
		let minX = 0;
		let minY = 0;
		let maxX = 0;
		let maxY = 0;
		const goal = this.goalIndex;
		for (let i = 0; i < this.segments.length; i++) {
			// C# 存成 float
			const p = { x: Math.fround(x), y: Math.fround(y) };
			pts.push(p);
			if (i <= goal) {
				minX = Math.min(minX, p.x);
				maxX = Math.max(maxX, p.x);
				minY = Math.min(minY, p.y);
				maxY = Math.max(maxY, p.y);
			}
			heading += this.segments[i].curve * MAP_TURN;
			x += Math.cos(heading);
			y += Math.sin(heading);
		}
		this.mapBounds = { x: minX, y: minY, w: Math.max(1, maxX - minX), h: Math.max(1, maxY - minY) };
		this.mapPoints = pts;
	}

	// #endregion

	// #region 路

	private addSegment(curve: number, y: number): void {
		const n = this.segments.length;
		this.segments.push(new RacerSegment(n, this.lastY, y, curve, Math.trunc(n / RUMBLE_LEN) % 2 === 1, this.curStage));
		this.lastY = y;
	}

	private addRoad(enter: number, hold: number, leave: number, curve: number, hill: number): void {
		const startY = this.lastY;
		const endY = startY + hill * SEG_LEN;
		const total = enter + hold + leave;
		for (let n = 0; n < enter; n++) this.addSegment(easeIn(0, curve, n / enter), easeInOut(startY, endY, n / total));
		for (let n = 0; n < hold; n++) this.addSegment(curve, easeInOut(startY, endY, (enter + n) / total));
		for (let n = 0; n < leave; n++) this.addSegment(easeInOut(curve, 0, n / leave), easeInOut(startY, endY, (enter + hold + n) / total));
	}

	private addStraight(n: number): void {
		const q = Math.trunc(n / 4);
		const h = Math.trunc(n / 2);
		this.addRoad(q, h, n - q - h, 0, 0);
	}

	private addRandomSection(difficulty: number): void {
		const rng = this.rng;
		const len = rng.next(25, 70);
		const curves = [2, 3.5, 5];
		const curve = curves[rng.next(curves.length)] * difficulty * (rng.next(2) === 0 ? -1 : 1);

		let hill = rng.next(3) === 0 ? 0 : rng.next(-25, 26);
		// 一直往上或往下太久就拉回來
		const level = this.lastY / SEG_LEN;
		if (Math.abs(level) > 50) hill = -level * (0.4 + rng.nextDouble() * 0.4);

		const r = rng.nextDouble();
		const third = Math.trunc(len / 3);
		const half = Math.trunc(len / 2);
		if (r < 0.18) {
			this.addRoad(third, len, third, 0, hill);
		} else if (r < 0.65) {
			this.addRoad(half, len, half, curve, hill);
		} else if (r < 0.85) {
			// S 彎
			this.addRoad(third, half, third, curve, hill / 2);
			this.addRoad(third, half, third, -curve, -hill / 2);
		} else {
			// 長長的緩彎
			this.addRoad(40, 90, 40, curve * 0.45, hill);
		}
	}

	// #endregion

	// #region 景物、車子

	private nearGate(i: number): boolean {
		for (const c of this.checkpoint) if (Math.abs(i - c) < 4) return true;
		return Math.abs(i - START_GATE) < 4;
	}

	private addSprite(i: number, type: RacerSprite, offset: number): void {
		const seg = this.segments[i];
		(seg.sprites ??= []).push({ type, offset, variant: this.rng.next(3) });
	}

	private placeScenery(): void {
		const rng = this.rng;
		for (let i = 20; i < this.segments.length - 200; i++) {
			if (this.nearGate(i)) continue;
			const theme = THEMES[this.segments[i].stage];

			// 整齊的行道樹 / 路燈，一段有一段沒有
			if (theme.rows && Math.trunc(i / 160) % 2 === 0 && i % 8 === 0) {
				this.addSprite(i, theme.rows, -1.2);
				this.addSprite(i, theme.rows, 1.2);
				continue;
			}

			if (i % 220 === 110) {
				this.addSprite(i, "billboard", rng.next(2) === 0 ? -1.15 : 1.15);
				continue;
			}

			if (rng.nextDouble() < 0.14) {
				const type = theme.scenery[rng.next(theme.scenery.length)];
				const side = rng.next(2) === 0 ? -1 : 1;
				this.addSprite(i, type, side * (1.2 + rng.nextDouble() * 2.4));
			}
		}
	}

	private spawnCars(stages: number, carsPerStage: number, maxSpeed: number): void {
		const rng = this.rng;
		const lanes = [-0.66, 0, 0.66];
		for (let s = 0; s < stages; s++) {
			const from = this.stageStart[s] + (s === 0 ? 60 : 10);
			const to = this.checkpoint[s] - 10;
			for (let k = 0; k < carsPerStage; k++) {
				const i = rng.next(from, to);
				const r = rng.nextDouble();
				const type: RacerCarType = r < 0.55 ? "coupe" : r < 0.8 ? "beetle" : "truck";
				const offset = lanes[rng.next(lanes.length)] + (rng.nextDouble() - 0.5) * 0.1;
				const z = (i + rng.nextDouble()) * SEG_LEN;
				let speed = maxSpeed * (CAR_SPEED_MIN + rng.nextDouble() * (CAR_SPEED_MAX - CAR_SPEED_MIN));
				const color = CarColors[rng.next(CarColors.length)];
				if (type === "truck") speed *= 0.85;
				const segment = this.segments[i];
				const car: RacerCar = { type, offset, z, speed, color, segment };
				(segment.cars ??= []).push(car);
				this.cars.push(car);
			}
		}
	}

	/** 路上的車往前開，換段時搬到新的段落 */
	updateCars(dt: number): void {
		for (const car of this.cars) {
			car.z += car.speed * dt;
			if (car.z >= this.length) car.z -= this.length;
			const seg = this.segmentAt(car.z);
			if (seg === car.segment) continue;
			const list = car.segment.cars!;
			list.splice(list.indexOf(car), 1);
			(seg.cars ??= []).push(car);
			car.segment = seg;
		}
	}

	// #endregion
}
