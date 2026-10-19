import { Color, Colors, darker, hex, lighter, mix, rgb, withAlpha } from "../gfx/color";
import { Align, Font, Svg } from "../gfx/svg";
import {
	BOOSTS,
	CAM_H,
	CRASH_TIME,
	H,
	HORIZON,
	K,
	MAX_SPEED,
	PLAYER_CAR_W,
	PLAYER_Z,
	RacerDriver,
	RacerRival,
	ROAD_W,
	W,
} from "./racer-driver";
import {
	BG_PERIOD,
	carHeight,
	carWidth,
	RacerCar,
	RacerSegment,
	RacerSpriteItem,
	RacerTheme,
	RacerTrack,
	SEG_LEN,
	spriteHeight,
	spriteWidth,
	THEMES,
} from "./racer-track";

const NEAR = 10;
const DRAW_DIST = 150; // 往前畫幾段
const FOG_DENSITY = 4.5;
const LANES = 3;

const TrunkColor = hex(0x8a5a2a);
const PalmDark = hex(0x1e7a2e);
const PalmLight = hex(0x3cb040);
const PineColor = hex(0x1c5a2c);
const CactusColor = hex(0x3a8a3a);
const RockColor = hex(0x8a7a6a);
const PoleColor = hex(0xa0a0a8);
const TireColor = hex(0x181818);
export const HudYellow = hex(0xffd830);
export const HudBlue = hex(0x80c8ff);
export const Grey = hex(0xc0c0c0);

const BillboardColors: readonly (readonly [Color, Color])[] = [
	[hex(0xf4f4f4), hex(0xe02020)],
	[hex(0x2050c0), hex(0xffd830)],
	[hex(0xffc820), hex(0x202020)],
];

const PALM_LEAVES = [
	[-1.0, 0.6],
	[-0.85, -0.15],
	[-0.35, -0.55],
	[0.35, -0.55],
	[0.85, -0.15],
	[1.0, 0.6],
	[-0.2, 0.75],
	[0.3, 0.7],
];

const randInt = (min: number, max: number) => min + Math.floor(Math.random() * (max - min)); // [min, max)

function wrap(v: number, period: number): number {
	v %= period;
	return v < 0 ? v + period : v;
}

/** 投影一個點：回傳畫面 x、y、半路寬、比例、相機前方距離 */
function project(wy: number, wz: number, camX: number, camY: number, camZ: number) {
	const cz = wz - camZ;
	const scale = 1.0 / Math.max(cz, NEAR);
	return { sx: W / 2 - scale * camX * K, sy: HORIZON - scale * (wy - camY) * K, sw: scale * ROAD_W * K, scale, cz };
}

/** 在某一段上的位置換算成畫面座標（車子底部中央）與比例 */
function onSegment(seg: RacerSegment, z: number, offset: number) {
	const pct = Math.max(0, Math.min(1, (z - seg.index * SEG_LEN) / SEG_LEN));
	const scale = seg.scale1 + (seg.scale2 - seg.scale1) * pct;
	return {
		cx: seg.x1 + (seg.x2 - seg.x1) * pct + scale * offset * ROAD_W * K,
		bottom: seg.screenY1 + (seg.screenY2 - seg.screenY1) * pct,
		scale,
	};
}

/**
 * 賽車畫面（200x100）：背景 → 路面（遠到近）→ 景物、車子、對手（遠到近）→ 玩家，
 * 最後交給呼叫端畫 HUD 與文字。單人版與 4P 版共用。
 */
export class RacerRenderer {
	readonly small: Font = { size: 8, bold: true };
	readonly mid: Font = { size: 10, bold: true };
	readonly big: Font = { size: 14, bold: true };
	readonly title: Font = { size: 18, bold: true, italic: true };
	readonly huge: Font = { size: 26, bold: true, italic: true };

	private readonly drawn: RacerSegment[] = new Array(DRAW_DIST);

	private g!: Svg;
	private track!: RacerTrack;
	private d!: RacerDriver;
	private rivals?: readonly RacerRival[];
	private carColor!: Color;
	private curFog = 1;
	private curFogColor: Color = Colors.Black;

	/** 畫一個玩家看出去的畫面，overlay 用來畫 HUD 與文字，回傳 SVG 字串 */
	render(track: RacerTrack, driver: RacerDriver, carColor: Color, rivals: readonly RacerRival[] | undefined, overlay?: (r: RacerRenderer) => void): string {
		this.track = track;
		this.d = driver;
		this.carColor = carColor;
		this.rivals = rivals;

		const g = (this.g = new Svg(W, H));
		g.clear(Colors.Black);

		if (driver.shakeTimer > 0) g.translate(randInt(-1, 2), randInt(-1, 2));

		this.curFog = 1;
		if (driver.bgFade < 1) this.drawBackground(THEMES[driver.bgFrom], 255);
		this.drawBackground(THEMES[driver.bgTo], driver.bgFade < 1 ? Math.trunc(driver.bgFade * 255) : 255);
		this.drawRoad();

		g.resetTransform();
		this.curFog = 1;
		overlay?.(this);
		this.rivals = undefined;
		return g.toString();
	}

	// #region 基本圖形

	private f(c: Color): Color {
		return this.curFog >= 1 ? c : mix(this.curFogColor, c, this.curFog);
	}

	rect(c: Color, x: number, y: number, w: number, h: number): void {
		if (w <= 0 || h <= 0) return;
		this.g.fillRect(x, y, w, h, this.f(c));
	}

	private ellipse(c: Color, x: number, y: number, w: number, h: number): void {
		if (w <= 0 || h <= 0) return;
		this.g.fillEllipse(x, y, w, h, this.f(c));
	}

	private poly(c: Color, ...xy: number[]): void {
		this.g.fillPolygon(xy, this.f(c));
	}

	private sizedFont(px: number): Font {
		return { size: Math.max(4, Math.min(40, Math.trunc(px))), bold: true };
	}

	/** 有陰影的文字，align 決定 x 是左、中、右 */
	text(s: string, font: Font, c: Color, x: number, y: number, align: Align = "near"): void {
		this.g.drawStringAt(s, font, Colors.Black, x + 1, y + 1, align);
		this.g.drawStringAt(s, font, c, x, y, align);
	}

	/** 置中在一列裡的文字（整個畫面寬） */
	textIn(s: string, font: Font, c: Color, y: number, h: number): void {
		this.g.drawStringIn(s, font, Colors.Black, { x: 1, y: y + 1, w: W, h }, "center", "center");
		this.g.drawStringIn(s, font, c, { x: 0, y, w: W, h }, "center", "center");
	}

	/** 半透明黑底，墊在文字後面 */
	band(y: number, h: number): void {
		this.g.fillRect(0, y, W, h, rgb(0, 0, 0, 150));
	}

	/** 車子身上的大字訊息（CHECKPOINT 等），GO! 以外會閃 */
	message(driver: RacerDriver, now: number): void {
		if (driver.msgTimer <= 0 || !driver.msg1) return;
		const blink = driver.msg1 !== "GO!" && Math.trunc(now * 5) % 2 === 1;
		if (!blink) this.textIn(driver.msg1, this.title, HudYellow, 26, 24);
		if (driver.msg2) this.textIn(driver.msg2, this.mid, Colors.White, 50, 14);
	}

	/** 右上角的關卡與進度條 */
	stageBar(label: string, progress: number): void {
		this.text(label, this.small, HudBlue, W - 2, 0, "far");
		this.rect(Colors.Black, W - 34, 11, 32, 4);
		this.rect(hex(0x505050), W - 33, 12, 30, 2);
		this.rect(HudYellow, W - 33, 12, 30 * progress, 2);
	}

	/** 左下角的速度表 */
	speedo(kmh: number): void {
		this.text(String(Math.trunc(kmh)), this.big, HudYellow, 32, H - 17, "far");
		this.text("km/h", this.small, Colors.White, 33, H - 11);
	}

	/** 右上角進度條下面，剩幾次加速，加速中會閃 */
	boostPips(driver: RacerDriver, now: number): void {
		const flash = driver.boosting && Math.trunc(now * 8) % 2 === 0;
		for (let i = 0; i < BOOSTS; i++) {
			const x = W - 9 - i * 9;
			const left = i < driver.boosts;
			this.rect(Colors.Black, x - 1, 17, 9, 6);
			this.rect(left ? (flash ? Colors.White : hex(0x30d0ff)) : hex(0x404040), x, 18, 7, 4);
		}
	}

	// #endregion

	// #region 背景

	private drawBackground(t: RacerTheme, alpha: number): void {
		const g = this.g;
		const d = this.d;
		g.fillRectGradient(0, 0, W, HORIZON + 1, withAlpha(t.skyTop, alpha), withAlpha(t.skyBottom, alpha), -1, HORIZON + 1);

		if (t.sun && t.sunColor) {
			const sx = wrap(150 - d.farOffset * 0.5, W + 60) - 30;
			const sy = HORIZON - 12;
			g.fillEllipse(sx - 15, sy - 15, 30, 30, withAlpha(t.sunColor, Math.trunc(alpha / 3)));
			g.fillEllipse(sx - 10, sy - 10, 20, 20, withAlpha(t.sunColor, alpha));
		}

		if (t.clouds) {
			const c = withAlpha(Colors.White, Math.trunc((alpha * 3) / 4));
			for (let i = 0; i < 5; i++) {
				const cx = wrap(i * 53 + 17 - d.farOffset * 0.6, W + 60) - 30;
				const cy = 5 + ((i * 7) % 14);
				g.fillEllipse(cx, cy, 22, 6, c);
				g.fillEllipse(cx + 5, cy - 3, 12, 6, c);
			}
		}

		const baseline = t.sea ? HORIZON - 5 : HORIZON;
		this.drawSilhouette(t.farHeights, d.farOffset, baseline, withAlpha(t.far, alpha));
		if (t.farCap) {
			// 山頂積雪：每一欄 1px 寬，連在一起一樣高的合成一塊
			const c = withAlpha(t.farCap, alpha);
			let runX = -1;
			let runTop = 0;
			let runH = 0;
			const flush = (x: number) => {
				if (runX >= 0) g.fillRect(runX, runTop, x - runX, runH, c);
				runX = -1;
			};
			for (let x = 0; x < W; x++) {
				const h = t.farHeights[Math.trunc(wrap(x + d.farOffset, BG_PERIOD))];
				if (h <= 11) {
					flush(x);
					continue;
				}
				const top = baseline - h;
				const hh = Math.min(3, h - 11);
				if (runX >= 0 && top === runTop && hh === runH) continue;
				flush(x);
				runX = x;
				runTop = top;
				runH = hh;
			}
			flush(W);
		}

		if (t.sea && t.seaColor) {
			g.fillRect(0, HORIZON - 5, W, 6, withAlpha(t.seaColor, alpha));
			const wave = withAlpha(lighter(t.seaColor, 0.35), alpha);
			for (let i = 0; i < 9; i++) {
				const wx = wrap(i * 29 - d.farOffset * 1.5, W + 20) - 10;
				g.fillRect(wx, HORIZON - 3 + (i % 3), 6, 1, wave);
			}
		}

		if (t.near) this.drawSilhouette(t.nearHeights, d.nearOffset, HORIZON, withAlpha(t.near, alpha));

		// 地平線以下先鋪一層遠處的地面顏色，下坡時看得到
		g.fillRect(0, HORIZON, W, H - HORIZON, withAlpha(mix(t.fog, t.grassDark, 0.35), alpha));
	}

	private drawSilhouette(heights: readonly number[], offset: number, baseline: number, c: Color): void {
		const pts: number[] = [];
		for (let x = 0; x <= W; x += 2) pts.push(x, baseline - heights[Math.trunc(wrap(x + offset, BG_PERIOD))]);
		pts.push(W, baseline + 1, 0, baseline + 1);
		this.g.fillPolygon(pts, c);
	}

	// #endregion

	// #region 路

	private drawRoad(): void {
		const track = this.track;
		const d = this.d;
		const segs = track.segments;
		const count = segs.length;
		const position = d.position;
		const baseSeg = track.segmentAt(position);
		const basePct = wrap(position, SEG_LEN) / SEG_LEN;
		const playerSeg = track.segmentAt(d.z);
		const playerPct = wrap(d.z, SEG_LEN) / SEG_LEN;
		const camY = playerSeg.y1 + (playerSeg.y2 - playerSeg.y1) * playerPct + CAM_H;

		// 1. 由近到遠投影，同時算出每段被前面山坡擋到哪裡（clip）
		let maxy = H;
		let x = 0;
		let dx = -(baseSeg.curve * basePct);
		for (let n = 0; n < DRAW_DIST; n++) {
			const idx = baseSeg.index + n;
			const seg = segs[idx % count];
			const camZ = position - (idx >= count ? track.length : 0);
			const camX = d.x * ROAD_W - x;

			seg.fog = Math.exp(-Math.pow(n / DRAW_DIST, 2) * FOG_DENSITY);
			seg.clip = maxy;
			const p1 = project(seg.y1, seg.index * SEG_LEN, camX, camY, camZ);
			const p2 = project(seg.y2, (seg.index + 1) * SEG_LEN, camX - dx, camY, camZ);
			seg.x1 = p1.sx;
			seg.screenY1 = p1.sy;
			seg.w1 = p1.sw;
			seg.scale1 = p1.scale;
			seg.camZ1 = p1.cz;
			seg.x2 = p2.sx;
			seg.screenY2 = p2.sy;
			seg.w2 = p2.sw;
			seg.scale2 = p2.scale;
			x += dx;
			dx += seg.curve;

			seg.visible = seg.camZ1 > NEAR && seg.screenY2 < seg.screenY1 && seg.screenY2 < maxy;
			if (seg.visible) maxy = Math.min(maxy, seg.screenY2);
			this.drawn[n] = seg;
		}

		// 2. 路面由遠到近畫，近的蓋掉遠的
		for (let n = DRAW_DIST - 1; n >= 0; n--) if (this.drawn[n].visible) this.drawSegment(this.drawn[n]);

		// 3. 景物、車子、對手、玩家由遠到近
		let playerDrawn = false;
		for (let n = DRAW_DIST - 1; n >= 1; n--) {
			const seg = this.drawn[n];
			if (seg.camZ1 > NEAR && seg.clip > 0) {
				const theme = THEMES[seg.stage];
				this.curFog = seg.fog;
				this.curFogColor = theme.fog;
				this.g.setClip({ x: -10, y: -100, w: W + 20, h: seg.clip + 100 });

				if (seg.sprites) for (const sp of seg.sprites) this.drawScenery(seg, sp, theme);
				if (seg.cars) for (const car of seg.cars) this.drawTraffic(seg, car);
				if (this.rivals) for (const r of this.rivals) if (track.segmentAt(r.z) === seg) this.drawRival(seg, r);
				if (seg.gate !== "none") this.drawGate(seg);

				this.g.resetClip();
				this.curFog = 1;
			}

			if (seg === playerSeg && !playerDrawn) {
				this.drawPlayer();
				playerDrawn = true;
			}
		}
		if (!playerDrawn) this.drawPlayer();
	}

	private drawSegment(seg: RacerSegment): void {
		const t = THEMES[seg.stage];
		this.curFog = seg.fog;
		this.curFogColor = t.fog;

		const x1 = seg.x1;
		const y1 = Math.round(seg.screenY1);
		const w1 = seg.w1;
		const x2 = seg.x2;
		const y2 = Math.round(seg.screenY2);
		const w2 = seg.w2;
		const r1 = w1 / 6;
		const r2 = w2 / 6;
		const l1 = w1 / 32;
		const l2 = w2 / 32;

		this.rect(seg.dark ? t.grassDark : t.grassLight, 0, y2, W, Math.min(H, y1) - y2 + 1);

		// 四捨五入後上下同一排的路段，路面是零高度的多邊形，畫了也看不到
		if (y2 < y1) {
			const rumble = seg.dark ? t.rumbleDark : t.rumbleLight;
			this.poly(rumble, x1 - w1 - r1, y1, x1 - w1, y1, x2 - w2, y2, x2 - w2 - r2, y2);
			this.poly(rumble, x1 + w1 + r1, y1, x1 + w1, y1, x2 + w2, y2, x2 + w2 + r2, y2);
			this.poly(seg.dark ? t.roadDark : t.roadLight, x1 - w1, y1, x1 + w1, y1, x2 + w2, y2, x2 - w2, y2);

			if (!seg.dark) {
				const lw1 = (w1 * 2) / LANES;
				const lw2 = (w2 * 2) / LANES;
				let lx1 = x1 - w1 + lw1;
				let lx2 = x2 - w2 + lw2;
				for (let lane = 1; lane < LANES; lane++) {
					this.poly(t.lane, lx1 - l1 / 2, y1, lx1 + l1 / 2, y1, lx2 + l2 / 2, y2, lx2 - l2 / 2, y2);
					lx1 += lw1;
					lx2 += lw2;
				}
			}
		}
		this.curFog = 1;
	}

	// #endregion

	// #region 景物

	private drawScenery(seg: RacerSegment, sp: RacerSpriteItem, theme: RacerTheme): void {
		const scale = seg.scale1;
		const w = spriteWidth(sp.type) * scale * K;
		const h = spriteHeight(sp.type) * scale * K;
		if (w < 0.8 || h < 0.8) return;

		const anchor = seg.x1 + scale * sp.offset * ROAD_W * K;
		const x = sp.offset < 0 ? anchor - w : anchor;
		const y = seg.screenY1 - h;
		if (x > W || x + w < 0) return;

		if (w < 3) {
			// 太遠了，一個色塊就好
			const c =
				sp.type === "rock"
					? RockColor
					: sp.type === "billboard"
						? BillboardColors[sp.variant][0]
						: sp.type === "lamp"
							? PoleColor
							: sp.type === "cactus"
								? CactusColor
								: theme.foliage;
			this.rect(c, x + w * 0.3, y, Math.max(1, w * 0.4), h);
			return;
		}

		switch (sp.type) {
			case "palm":
				this.drawPalm(x, y, w, h, sp.variant);
				break;
			case "tree":
				this.drawTree(x, y, w, h, theme.foliage);
				break;
			case "pine":
				this.drawPine(x, y, w, h);
				break;
			case "bush":
				this.drawBush(x, y, w, h, theme.foliage);
				break;
			case "cactus":
				this.drawCactus(x, y, w, h);
				break;
			case "rock":
				this.drawRock(x, y, w, h);
				break;
			case "billboard":
				this.drawBillboard(x, y, w, h, sp.variant);
				break;
			case "lamp":
				this.drawLamp(x, y, w, h, sp.offset < 0);
				break;
		}
	}

	private drawPalm(x: number, y: number, w: number, h: number, variant: number): void {
		const bx = x + w * 0.5;
		const by = y + h;
		const tx = x + w * (variant === 1 ? 0.62 : variant === 2 ? 0.5 : 0.4);
		const ty = y + h * 0.22;
		const tb = Math.max(1, w * 0.09);
		const tt = Math.max(1, w * 0.06);
		this.poly(TrunkColor, bx - tb / 2, by, bx + tb / 2, by, tx + tt / 2, ty, tx - tt / 2, ty);

		const t = Math.max(1, h * 0.035);
		PALM_LEAVES.forEach(([lx, ly], i) => {
			const ex = tx + lx * w * 0.5;
			const ey = ty + ly * h * 0.2;
			const mx = tx + lx * w * 0.27;
			const my = ty + ly * h * 0.11 - h * 0.05;
			this.poly(i % 2 === 0 ? PalmDark : PalmLight, tx, ty - t, mx, my - t, ex, ey, mx, my + t, tx, ty + t);
		});
		if (w > 10) this.ellipse(hex(0x6a4018), tx - w * 0.06, ty, w * 0.12, w * 0.1);
	}

	private drawTree(x: number, y: number, w: number, h: number, foliage: Color): void {
		this.rect(TrunkColor, x + w * 0.44, y + h * 0.5, w * 0.12, h * 0.5);
		this.ellipse(darker(foliage), x, y + h * 0.22, w * 0.62, h * 0.45);
		this.ellipse(darker(foliage), x + w * 0.38, y + h * 0.2, w * 0.62, h * 0.45);
		this.ellipse(foliage, x + w * 0.14, y, w * 0.72, h * 0.52);
		this.ellipse(lighter(foliage, 0.25), x + w * 0.3, y + h * 0.06, w * 0.3, h * 0.18);
	}

	private drawPine(x: number, y: number, w: number, h: number): void {
		this.rect(TrunkColor, x + w * 0.44, y + h * 0.84, w * 0.12, h * 0.16);
		this.poly(PineColor, x, y + h * 0.88, x + w, y + h * 0.88, x + w / 2, y + h * 0.4);
		this.poly(PineColor, x + w * 0.1, y + h * 0.62, x + w * 0.9, y + h * 0.62, x + w / 2, y + h * 0.15);
		this.poly(lighter(PineColor, 0.12), x + w * 0.22, y + h * 0.38, x + w * 0.78, y + h * 0.38, x + w / 2, y);
	}

	private drawBush(x: number, y: number, w: number, h: number, foliage: Color): void {
		this.ellipse(darker(foliage), x, y + h * 0.25, w, h * 0.75);
		this.ellipse(foliage, x + w * 0.18, y, w * 0.64, h * 0.7);
	}

	private drawCactus(x: number, y: number, w: number, h: number): void {
		this.rect(CactusColor, x + w * 0.38, y, w * 0.24, h);
		this.rect(CactusColor, x + w * 0.06, y + h * 0.28, w * 0.18, h * 0.32);
		this.rect(CactusColor, x + w * 0.06, y + h * 0.52, w * 0.34, h * 0.1);
		this.rect(CactusColor, x + w * 0.76, y + h * 0.18, w * 0.18, h * 0.3);
		this.rect(CactusColor, x + w * 0.6, y + h * 0.4, w * 0.34, h * 0.1);
		this.rect(lighter(CactusColor, 0.3), x + w * 0.42, y + h * 0.04, Math.max(1, w * 0.06), h * 0.9);
	}

	private drawRock(x: number, y: number, w: number, h: number): void {
		this.poly(RockColor, x, y + h, x + w * 0.08, y + h * 0.4, x + w * 0.35, y + h * 0.05, x + w * 0.7, y, x + w * 0.95, y + h * 0.45, x + w, y + h);
		this.poly(lighter(RockColor, 0.3), x + w * 0.12, y + h * 0.45, x + w * 0.36, y + h * 0.12, x + w * 0.6, y + h * 0.1, x + w * 0.4, y + h * 0.5);
	}

	private drawBillboard(x: number, y: number, w: number, h: number, variant: number): void {
		const c = BillboardColors[variant % BillboardColors.length];
		this.rect(PoleColor, x + w * 0.15, y + h * 0.6, Math.max(1, w * 0.05), h * 0.4);
		this.rect(PoleColor, x + w * 0.8, y + h * 0.6, Math.max(1, w * 0.05), h * 0.4);
		const bh = h * 0.62;
		this.rect(hex(0x303030), x, y, w, bh);
		this.rect(c[0], x + 1, y + 1, w - 2, bh - 2);
		if (bh >= 7) {
			this.rect(c[1], x + 1, y + bh * 0.72, w - 2, bh * 0.14);
			const ink = this.f(c[1]);
			// 上面大字 SUNSET，下面小字 ARCADE
			this.g.drawStringIn("SUNSET", this.sizedFont(Math.min(bh * 0.4, w * 0.18)), ink, { x, y, w, h: bh * 0.46 }, "center", "center");
			this.g.drawStringIn("ARCADE", this.sizedFont(Math.min(bh * 0.22, w * 0.14)), ink, { x, y: y + bh * 0.42, w, h: bh * 0.3 }, "center", "center");
		}
	}

	private drawLamp(x: number, y: number, w: number, h: number, armRight: boolean): void {
		const pw = Math.max(1, w * 0.1);
		const px = armRight ? x + w * 0.15 : x + w * 0.75;
		this.rect(PoleColor, px, y + h * 0.06, pw, h * 0.94);
		const ax = armRight ? px : x + w * 0.1;
		this.rect(PoleColor, ax, y + h * 0.05, w * 0.7, Math.max(1, h * 0.025));
		const hx = armRight ? x + w * 0.72 : x;
		this.rect(hex(0xfff4c0), hx, y + h * 0.06, w * 0.28, Math.max(1, h * 0.03));
	}

	// #endregion

	// #region 車子

	private drawTraffic(seg: RacerSegment, car: RacerCar): void {
		const { cx, bottom, scale } = onSegment(seg, wrap(car.z, this.track.length), car.offset);
		const w = carWidth(car.type) * scale * K;
		const h = carHeight(car.type) * scale * K;
		if (w < 0.8) return;

		const x = cx - w / 2;
		const y = bottom - h;
		if (x > W || x + w < 0) return;
		if (w < 4) {
			this.rect(car.color, x, y, w, h);
			return;
		}

		this.rect(hex(0x303030), x - w * 0.02, bottom - h * 0.06, w * 1.04, h * 0.08);
		this.rect(TireColor, x + w * 0.04, bottom - h * 0.24, w * 0.17, h * 0.24);
		this.rect(TireColor, x + w * 0.79, bottom - h * 0.24, w * 0.17, h * 0.24);

		const tail = hex(0xe82020);
		switch (car.type) {
			case "coupe":
				this.rect(car.color, x, y + h * 0.4, w, h * 0.45);
				this.poly(darker(car.color, 0.8), x + w * 0.12, y + h * 0.42, x + w * 0.88, y + h * 0.42, x + w * 0.78, y, x + w * 0.22, y);
				this.poly(hex(0x304060), x + w * 0.2, y + h * 0.37, x + w * 0.8, y + h * 0.37, x + w * 0.73, y + h * 0.08, x + w * 0.27, y + h * 0.08);
				this.rect(tail, x + w * 0.04, y + h * 0.5, w * 0.2, h * 0.12);
				this.rect(tail, x + w * 0.76, y + h * 0.5, w * 0.2, h * 0.12);
				this.rect(hex(0xf0f0e0), x + w * 0.42, y + h * 0.58, w * 0.16, h * 0.1);
				this.rect(darker(car.color, 0.6), x, y + h * 0.78, w, h * 0.08);
				break;

			case "beetle":
				this.ellipse(car.color, x, y + h * 0.3, w, h * 0.6);
				this.ellipse(car.color, x + w * 0.16, y, w * 0.68, h * 0.62);
				this.ellipse(hex(0x304060), x + w * 0.3, y + h * 0.1, w * 0.4, h * 0.22);
				this.rect(tail, x + w * 0.1, y + h * 0.52, w * 0.1, h * 0.12);
				this.rect(tail, x + w * 0.8, y + h * 0.52, w * 0.1, h * 0.12);
				this.rect(PoleColor, x + w * 0.05, y + h * 0.8, w * 0.9, h * 0.06);
				break;

			default:
				this.rect(hex(0xe8e2d4), x, y, w, h * 0.84);
				this.rect(car.color, x, y + h * 0.32, w, h * 0.1);
				this.rect(hex(0x909088), x + w * 0.495, y + h * 0.04, Math.max(1, w * 0.01), h * 0.76);
				this.rect(hex(0x404040), x, y + h * 0.8, w, h * 0.06);
				this.rect(tail, x + w * 0.02, y + h * 0.72, w * 0.1, h * 0.06);
				this.rect(tail, x + w * 0.88, y + h * 0.72, w * 0.1, h * 0.06);
				break;
		}
	}

	/** 其他玩家：跟自己同一台敞篷車，換顏色、依距離縮放 */
	private drawRival(seg: RacerSegment, r: RacerRival): void {
		const { cx, bottom, scale } = onSegment(seg, r.z, r.x);
		const s = scale * PLAYER_Z; // 在 PLAYER_Z 時 = 1
		const w = PLAYER_CAR_W * s;
		if (w < 0.8 || cx + w < 0 || cx - w > W) return;
		if (w < 6) {
			this.rect(r.color, cx - w / 2, bottom - w * 0.5, w, w * 0.5);
			return;
		}

		const g = this.g;
		g.save();
		g.translate(cx, bottom);
		g.scale(s, s);
		if (r.crashTimer > 0) {
			const p = 1 - r.crashTimer / CRASH_TIME;
			g.translate(0, -12 - Math.sin(Math.min(1, p * 1.4) * Math.PI) * 14);
			g.rotate(Math.min(1, p * 1.4) * 540);
			this.drawCarBody(0, 12, 0, false, 0, r.color);
		} else {
			this.drawCarBody(0, 0, r.lean, false, 0.5, r.color);
		}
		g.restore();
	}

	private drawGate(seg: RacerSegment): void {
		const x1 = seg.x1;
		const y1 = seg.screenY1;
		const w1 = seg.w1;
		const postW = Math.max(1, w1 * 0.06);
		const postH = w1 * 0.95;
		const lx = x1 - w1 * 1.15 - postW;
		const rx = x1 + w1 * 1.15;
		this.rect(hex(0xd8d8d8), lx, y1 - postH, postW, postH);
		this.rect(hex(0xd8d8d8), rx, y1 - postH, postW, postH);

		const bx = lx;
		const bw = rx + postW - lx;
		const bh = Math.max(1, postH * 0.24);
		const by = y1 - postH;
		let label: string;
		if (seg.gate === "checkpoint") {
			this.rect(hex(0x2040c0), bx, by, bw, bh);
			label = "CHECKPOINT";
		} else {
			// START / GOAL：黑白格子
			this.rect(Colors.White, bx, by, bw, bh);
			const cols = 20;
			const cw = bw / cols;
			if (cw >= 1) for (let i = 0; i < cols; i++) this.rect(Colors.Black, bx + i * cw, by + (i % 2 === 0 ? 0 : bh / 2), cw, bh / 2);
			label = seg.gate === "goal" ? "GOAL" : "START";
		}

		if (bh >= 6) {
			if (seg.gate !== "checkpoint") {
				const tw = bw * 0.36;
				this.rect(hex(0xd01010), bx + (bw - tw) / 2, by, tw, bh);
			}
			const ink = this.f(seg.gate === "checkpoint" ? HudYellow : Colors.White);
			this.g.drawStringIn(label, this.sizedFont(bh * 0.7), ink, { x: bx, y: by, w: bw, h: bh }, "center", "center");
		}
	}

	private drawPlayer(): void {
		const d = this.d;
		const g = this.g;
		const speedPct = d.speed / MAX_SPEED;
		const cx = W / 2;
		let by = H - 2;

		if (d.crashTimer > 0) {
			// 翻車：彈起來轉圈
			const p = 1 - d.crashTimer / CRASH_TIME;
			const hop = Math.sin(Math.min(1, p * 1.4) * Math.PI) * 14;
			g.save();
			g.translate(cx, by - 12 - hop);
			g.rotate(Math.min(1, p * 1.4) * 540);
			this.drawCarBody(0, 12, 0, false, 0, this.carColor);
			g.restore();
			return;
		}

		if (speedPct > 0.1 && randInt(0, 3) === 0) by -= 1;
		if (d.offRoad && d.speed > 0) by -= randInt(0, 2);

		this.drawCarBody(cx, by, d.lean, d.boosting, speedPct, this.carColor);

		if (d.offRoad && d.speed > 0) {
			// 開到路外揚起沙塵
			const t = THEMES[this.track.segmentAt(d.z).stage];
			const dust = withAlpha(lighter(t.grassDark, 0.2), 200);
			for (let i = 0; i < 5; i++) {
				const side = i % 2 === 0 ? -1 : 1;
				const px = cx + side * (20 + Math.random() * 10);
				const py = by - 2 - Math.random() * 8;
				const s = 2 + Math.random() * 3;
				g.fillEllipse(px, py, s, s, dust);
			}
		}
	}

	/** 敞篷跑車，從後面看，約 54x30。lean = -1 左轉 / 0 / 1 右轉 */
	private drawCarBody(cx: number, by: number, lean: number, boost: boolean, speedPct: number, body: Color): void {
		const L = lean * 2;
		const Lb = lean;
		const hi = lighter(body, 0.3);
		this.g.fillEllipse(cx - 27, by - 4, 54, 6, rgb(0, 0, 0, Math.trunc(110 * Math.min(1, this.curFog + 0.2))));

		this.rect(TireColor, cx - 25 + Lb, by - 9, 8, 9);
		this.rect(TireColor, cx + 17 + Lb, by - 9, 8, 9);
		this.rect(hex(0x404040), cx - 25 + Lb, by - 6, 8, 1);
		this.rect(hex(0x404040), cx + 17 + Lb, by - 6, 8, 1);

		// 車尾
		this.rect(body, cx - 24, by - 17, 48, 11);
		this.rect(hi, cx - 24, by - 17, 48, 1);
		// 左右各兩顆圓尾燈，中間車牌，下面黑色保險桿
		const tail = hex(0xff3020);
		const tailRim = darker(body, 0.5);
		this.ellipse(tailRim, cx - 22, by - 15, 7, 6);
		this.ellipse(tailRim, cx - 15, by - 15, 7, 6);
		this.ellipse(tailRim, cx + 8, by - 15, 7, 6);
		this.ellipse(tailRim, cx + 15, by - 15, 7, 6);
		this.ellipse(tail, cx - 21, by - 14, 5, 4);
		this.ellipse(tail, cx - 14, by - 14, 5, 4);
		this.ellipse(tail, cx + 9, by - 14, 5, 4);
		this.ellipse(tail, cx + 16, by - 14, 5, 4);
		this.rect(hex(0x202020), cx - 6, by - 14, 12, 5);
		this.rect(hex(0xf0f0e0), cx - 5, by - 13, 10, 3);
		this.rect(hex(0x202020), cx - 24, by - 8, 48, 2);
		this.rect(PoleColor, cx - 15, by - 7, 3, 2);
		this.rect(PoleColor, cx + 12, by - 7, 3, 2);
		if (boost) {
			// 加速時排氣管噴火
			for (const ex of [cx - 14.5, cx + 12.5]) {
				const fl = 4 + randInt(0, 4);
				this.ellipse(hex(0xff6010), ex - 2, by - 6, 6, fl);
				this.ellipse(hex(0xffe060), ex - 0.5, by - 6, 3, fl * 0.6);
			}
		}

		// 後車廂蓋、後照鏡
		this.rect(body, cx - 22 + L, by - 21, 44, 5);
		this.rect(hi, cx - 22 + L, by - 21, 44, 1);
		this.rect(body, cx - 27 + L, by - 23, 4, 3);
		this.rect(body, cx + 23 + L, by - 23, 4, 3);

		// 擋風玻璃框、頭枕、兩個人
		const hl = L * 1.5;
		this.rect(hex(0x505058), cx - 18 + hl, by - 27, 36, 1);
		this.rect(hex(0x505058), cx - 18 + hl, by - 27, 1, 6);
		this.rect(hex(0x505058), cx + 17 + hl, by - 27, 1, 6);
		this.rect(hex(0x302020), cx - 15 + L, by - 24, 9, 3);
		this.rect(hex(0x302020), cx + 6 + L, by - 24, 9, 3);
		this.ellipse(hex(0x3a2414), cx - 14 + hl, by - 30, 7, 7);
		const blonde = hex(0xf8d860);
		this.ellipse(blonde, cx + 6 + hl, by - 30, 7, 7);
		if (speedPct > 0.2) this.rect(blonde, cx + 12 + hl, by - 28, 2 + speedPct * 4, 2);
	}

	// #endregion
}
