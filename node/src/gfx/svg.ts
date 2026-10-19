import { Color, toHex } from "./color";

/**
 * 字型，對應 C# 的 new Font("Arial", size, style, GraphicsUnit.Pixel)。
 */
export type Font = { readonly size: number; readonly bold?: boolean; readonly italic?: boolean; readonly family?: string };

/** 對應 GDI+ 的 StringAlignment：Near / Center / Far */
export type Align = "near" | "center" | "far";

export type Rect = { readonly x: number; readonly y: number; readonly w: number; readonly h: number };

export type LineOptions = { readonly width?: number; readonly dash?: readonly number[] };

// Arial 的字型度量（units per em = 2048），GDI+ 的 DrawString 照這個排版
const ASCENT = 1854 / 2048;
const DESCENT = 434 / 2048;
const LINE_HEIGHT = (1854 + 434 + 67) / 2048;
// GDI+ DrawString 預設在字串左右各留 1/6 em
const PAD = 1 / 6;
// 估字寬用（Arial 平均字寬約 0.6 em），只拿來判斷文字落在哪幾格
const AVG_CHAR = 0.6;
// 被裁切或旋轉的橢圓改用多邊形畫，切幾段
const ELLIPSE_STEPS = 24;
// GDI+ 的座標也會先夾在合理範圍內，避免投影出來的超大數字
const LIMIT = 20000;

const n = (v: number) => (Math.round(v * 100) / 100).toString();
const lim = (v: number) => Math.max(-LIMIT, Math.min(LIMIT, v));

function esc(s: string): string {
	return s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
}

function paint(attr: "fill" | "stroke", c: Color): string {
	return c.a >= 255 ? `${attr}="${toHex(c)}"` : `${attr}="${toHex(c)}" ${attr}-opacity="${n(c.a / 255)}"`;
}

function fontAttrs(f: Font, size: number): string {
	let s = `font-family="${f.family ?? "Arial"}" font-size="${n(size)}"`;
	if (f.bold) s += ` font-weight="bold"`;
	if (f.italic) s += ` font-style="italic"`;
	return s;
}

/** 仿射矩陣：x' = a*x + c*y + e，y' = b*x + d*y + f */
type Matrix = readonly [number, number, number, number, number, number];
const IDENTITY: Matrix = [1, 0, 0, 1, 0, 0];

function multiply(m: Matrix, k: Matrix): Matrix {
	return [
		m[0] * k[0] + m[2] * k[1],
		m[1] * k[0] + m[3] * k[1],
		m[0] * k[2] + m[2] * k[3],
		m[1] * k[2] + m[3] * k[3],
		m[0] * k[4] + m[2] * k[5] + m[4],
		m[1] * k[4] + m[3] * k[5] + m[5],
	];
}

/** 裁切範圍（畫布座標），x0 <= x <= x1、y0 <= y <= y1 */
type Box = { x0: number; y0: number; x1: number; y1: number };

/** 一個圖形元素跟它的範圍；切格輸出時用範圍挑出跟那一格重疊的元素 */
type Part = { readonly s: string; readonly box: Box };

/** Sutherland–Hodgman：多邊形裁到矩形裡 */
function clipPolygon(pts: number[], c: Box): number[] {
	const edges: [(x: number, y: number) => boolean, (ax: number, ay: number, bx: number, by: number) => [number, number]][] = [
		[(x) => x >= c.x0, (ax, ay, bx, by) => [c.x0, ay + ((by - ay) * (c.x0 - ax)) / (bx - ax)]],
		[(x) => x <= c.x1, (ax, ay, bx, by) => [c.x1, ay + ((by - ay) * (c.x1 - ax)) / (bx - ax)]],
		[(_, y) => y >= c.y0, (ax, ay, bx, by) => [ax + ((bx - ax) * (c.y0 - ay)) / (by - ay), c.y0]],
		[(_, y) => y <= c.y1, (ax, ay, bx, by) => [ax + ((bx - ax) * (c.y1 - ay)) / (by - ay), c.y1]],
	];
	let out = pts;
	for (const [inside, cross] of edges) {
		const src = out;
		out = [];
		const count = src.length / 2;
		for (let i = 0; i < count; i++) {
			const ax = src[i * 2];
			const ay = src[i * 2 + 1];
			const j = (i + count - 1) % count;
			const px = src[j * 2];
			const py = src[j * 2 + 1];
			const aIn = inside(ax, ay);
			const pIn = inside(px, py);
			if (aIn) {
				if (!pIn) out.push(...cross(px, py, ax, ay));
				out.push(ax, ay);
			} else if (pIn) {
				out.push(...cross(px, py, ax, ay));
			}
		}
		if (out.length < 6) return [];
	}
	return out;
}

function boundsOf(pts: readonly number[]): Box {
	const b = { x0: Infinity, y0: Infinity, x1: -Infinity, y1: -Infinity };
	for (let i = 0; i < pts.length; i += 2) {
		b.x0 = Math.min(b.x0, pts[i]);
		b.x1 = Math.max(b.x1, pts[i]);
		b.y0 = Math.min(b.y0, pts[i + 1]);
		b.y1 = Math.max(b.y1, pts[i + 1]);
	}
	return b;
}

const inside = (b: Box, c: Box) => b.x0 >= c.x0 && b.x1 <= c.x1 && b.y0 >= c.y0 && b.y1 <= c.y1;
const disjoint = (b: Box, c: Box) => b.x1 <= c.x0 || b.x0 >= c.x1 || b.y1 <= c.y0 || b.y0 >= c.y1;

/**
 * 用 GDI+ 的呼叫方式組一張 SVG，取代 C# 的 Bitmap + Graphics。
 * 座標跟 GDI+ 一樣是像素、左上角原點、y 朝下。
 *
 * Stream Deck 用 Qt 畫 SVG，只保證支援 SVG Tiny 1.2，沒有 clipPath、dominant-baseline、rgba() 這些。
 * 所以 GDI+ 的變換（Translate / Scale / Rotate）跟 SetClip 都在這裡自己算，輸出的全是畫布上的絕對座標：
 * 矩形跟多邊形直接裁切，被裁到或被旋轉的橢圓改成多邊形。文字只會被平移、縮放，被裁到一部分就不畫。
 */
export class Svg {
	private readonly parts: Part[] = [];
	private m: Matrix = IDENTITY;
	private clip: Box | undefined;
	private readonly stack: { m: Matrix; clip: Box | undefined }[] = [];
	private gradients = 0;

	/** smooth = 整張圖反鋸齒，對應 SmoothingMode.AntiAlias；預設跟 SmoothingMode.None 一樣 */
	constructor(
		readonly width: number,
		readonly height: number,
		private readonly smooth = false,
	) {}

	// #region 變換、裁切

	/** 對應 g.Save()：記住目前的變換跟裁切 */
	save(): void {
		this.stack.push({ m: this.m, clip: this.clip });
	}

	/** 對應 g.Restore() */
	restore(): void {
		const s = this.stack.pop();
		if (!s) return;
		this.m = s.m;
		this.clip = s.clip;
	}

	translate(x: number, y: number): void {
		this.m = multiply(this.m, [1, 0, 0, 1, x, y]);
	}

	scale(sx: number, sy = sx): void {
		this.m = multiply(this.m, [sx, 0, 0, sy, 0, 0]);
	}

	/** 角度（度），順時針，跟 GDI+ 的 RotateTransform 一樣 */
	rotate(deg: number): void {
		const r = (deg * Math.PI) / 180;
		const c = Math.cos(r);
		const s = Math.sin(r);
		this.m = multiply(this.m, [c, s, -s, c, 0, 0]);
	}

	resetTransform(): void {
		this.m = IDENTITY;
	}

	/** 對應 g.SetClip(rect)：rect 會套用目前的變換（只考慮平移縮放） */
	setClip(r: Rect): void {
		const [x0, y0] = this.pt(r.x, r.y);
		const [x1, y1] = this.pt(r.x + r.w, r.y + r.h);
		this.clip = { x0: Math.min(x0, x1), y0: Math.min(y0, y1), x1: Math.max(x0, x1), y1: Math.max(y0, y1) };
	}

	resetClip(): void {
		this.clip = undefined;
	}

	private pt(x: number, y: number): [number, number] {
		const m = this.m;
		return [lim(m[0] * x + m[2] * y + m[4]), lim(m[1] * x + m[3] * y + m[5])];
	}

	/** 沒有旋轉（矩形變換後還是矩形） */
	private get axisAligned(): boolean {
		return this.m[1] === 0 && this.m[2] === 0;
	}

	/** 目前變換的縮放倍率，拿來縮字型、線寬 */
	private get scaleFactor(): number {
		return Math.sqrt(Math.abs(this.m[0] * this.m[3] - this.m[1] * this.m[2]));
	}

	private push(s: string, box: Box): void {
		this.parts.push({ s, box });
	}

	// #endregion

	// #region 填滿

	clear(c: Color): void {
		this.parts.length = 0;
		this.parts.push({ s: `<rect x="0" y="0" width="${this.width}" height="${this.height}" ${paint("fill", c)}/>`, box: { x0: 0, y0: 0, x1: this.width, y1: this.height } });
	}

	fillRect(x: number, y: number, w: number, h: number, c: Color): void {
		if (w <= 0 || h <= 0 || c.a <= 0) return;
		if (!this.axisAligned) {
			this.fillPolygon([x, y, x + w, y, x + w, y + h, x, y + h], c);
			return;
		}
		const [ax, ay] = this.pt(x, y);
		const [bx, by] = this.pt(x + w, y + h);
		let b: Box = { x0: Math.min(ax, bx), y0: Math.min(ay, by), x1: Math.max(ax, bx), y1: Math.max(ay, by) };
		if (this.clip) {
			const k = this.clip;
			b = { x0: Math.max(b.x0, k.x0), y0: Math.max(b.y0, k.y0), x1: Math.min(b.x1, k.x1), y1: Math.min(b.y1, k.y1) };
			if (b.x1 <= b.x0 || b.y1 <= b.y0) return;
		}
		this.push(`<rect x="${n(b.x0)}" y="${n(b.y0)}" width="${n(b.x1 - b.x0)}" height="${n(b.y1 - b.y0)}" ${paint("fill", c)}/>`, b);
	}

	/** 上下漸層的矩形，對應 LinearGradientBrush(..., LinearGradientMode.Vertical)；漸層從 gy0 到 gy1 */
	fillRectGradient(x: number, y: number, w: number, h: number, top: Color, bottom: Color, gy0 = y, gy1 = y + h): void {
		if (w <= 0 || h <= 0) return;
		const [ax, ay] = this.pt(x, y);
		const [bx, by] = this.pt(x + w, y + h);
		const [, g0] = this.pt(0, gy0);
		const [, g1] = this.pt(0, gy1);
		const id = `g${this.gradients++}`;
		const stop = (o: number, c: Color) => `<stop offset="${o}" stop-color="${toHex(c)}" stop-opacity="${n(c.a / 255)}"/>`;
		const b: Box = { x0: Math.min(ax, bx), y0: Math.min(ay, by), x1: Math.max(ax, bx), y1: Math.max(ay, by) };
		this.push(
			`<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="0" y1="${n(g0)}" x2="0" y2="${n(g1)}">${stop(0, top)}${stop(1, bottom)}</linearGradient>` +
				`<rect x="${n(b.x0)}" y="${n(b.y0)}" width="${n(b.x1 - b.x0)}" height="${n(b.y1 - b.y0)}" fill="url(#${id})"/>`,
			b,
		);
	}

	fillPolygon(points: readonly number[], c: Color): void {
		if (points.length < 6 || c.a <= 0) return;
		let pts: number[] = [];
		for (let i = 0; i + 1 < points.length; i += 2) pts.push(...this.pt(points[i], points[i + 1]));
		this.polygonAbs(pts, c);
	}

	private polygonAbs(pts: number[], c: Color): void {
		let b = boundsOf(pts);
		if (this.clip && !inside(b, this.clip)) {
			if (disjoint(b, this.clip)) return;
			pts = clipPolygon(pts, this.clip);
			if (pts.length < 6) return;
			b = boundsOf(pts);
		}
		this.push(`<polygon points="${pts.map(n).join(",")}" ${paint("fill", c)}/>`, b);
	}

	/** aa = 只有這個橢圓反鋸齒，對應暫時切到 SmoothingMode.AntiAlias */
	fillEllipse(x: number, y: number, w: number, h: number, c: Color, aa = false): void {
		if (w <= 0 || h <= 0 || c.a <= 0) return;
		const cx = x + w / 2;
		const cy = y + h / 2;
		if (this.axisAligned) {
			const [ax, ay] = this.pt(x, y);
			const [bx, by] = this.pt(x + w, y + h);
			const b: Box = { x0: Math.min(ax, bx), y0: Math.min(ay, by), x1: Math.max(ax, bx), y1: Math.max(ay, by) };
			if (!this.clip || inside(b, this.clip)) {
				const rx = (b.x1 - b.x0) / 2;
				const ry = (b.y1 - b.y0) / 2;
				const sr = aa && !this.smooth ? ` shape-rendering="auto"` : "";
				this.push(`<ellipse cx="${n(b.x0 + rx)}" cy="${n(b.y0 + ry)}" rx="${n(rx)}" ry="${n(ry)}" ${paint("fill", c)}${sr}/>`, b);
				return;
			}
			if (disjoint(b, this.clip)) return;
		}
		// 被旋轉或被裁到一部分：改成多邊形
		const pts: number[] = [];
		for (let i = 0; i < ELLIPSE_STEPS; i++) {
			const t = (i / ELLIPSE_STEPS) * Math.PI * 2;
			pts.push(...this.pt(cx + (Math.cos(t) * w) / 2, cy + (Math.sin(t) * h) / 2));
		}
		this.polygonAbs(pts, c);
	}

	// #endregion

	// #region 線（只套平移縮放，不裁切；用在不會被裁到的地方）

	/** 對應 g.DrawRectangle：1px 框線，跟 GDI+ 一樣涵蓋 x..x+w 的像素 */
	drawRect(x: number, y: number, w: number, h: number, c: Color, width = 1): void {
		if (c.a <= 0) return;
		const [ax, ay] = this.pt(x, y);
		const [bx, by] = this.pt(x + w, y + h);
		const sw = width * this.scaleFactor;
		const o = this.smooth ? 0 : 0.5;
		const b: Box = { x0: Math.min(ax, bx), y0: Math.min(ay, by), x1: Math.max(ax, bx), y1: Math.max(ay, by) };
		this.push(
			`<rect x="${n(b.x0 + o)}" y="${n(b.y0 + o)}" width="${n(b.x1 - b.x0)}" height="${n(b.y1 - b.y0)}" fill="none" ${paint("stroke", c)}${sw !== 1 ? ` stroke-width="${n(sw)}"` : ""}/>`,
			{ x0: b.x0 - sw, y0: b.y0 - sw, x1: b.x1 + sw + 1, y1: b.y1 + sw + 1 },
		);
	}

	/** 跟 GDI+ 一樣，整數座標的 1px 線會剛好落在那一排像素上 */
	drawLine(x1: number, y1: number, x2: number, y2: number, c: Color, opt: LineOptions = {}): void {
		const w = (opt.width ?? 1) * this.scaleFactor;
		const o = !this.smooth && w % 2 === 1 ? 0.5 : 0;
		const [ax, ay] = this.pt(x1, y1);
		const [bx, by] = this.pt(x2, y2);
		let s = `<line x1="${n(ax + o)}" y1="${n(ay + o)}" x2="${n(bx + o)}" y2="${n(by + o)}" ${paint("stroke", c)}`;
		if (w !== 1) s += ` stroke-width="${n(w)}"`;
		if (opt.dash) s += ` stroke-dasharray="${opt.dash.map(n).join(",")}"`;
		this.push(s + "/>", { x0: Math.min(ax, bx) - w, y0: Math.min(ay, by) - w, x1: Math.max(ax, bx) + w + 1, y1: Math.max(ay, by) + w + 1 });
	}

	/** 對應 g.DrawEllipse：1px 框線 */
	drawEllipse(x: number, y: number, w: number, h: number, c: Color, width = 1): void {
		if (c.a <= 0) return;
		const [ax, ay] = this.pt(x, y);
		const [bx, by] = this.pt(x + w, y + h);
		const sw = width * this.scaleFactor;
		const o = this.smooth ? 0 : 0.5;
		const rx = Math.abs(bx - ax) / 2;
		const ry = Math.abs(by - ay) / 2;
		const cx = Math.min(ax, bx) + rx;
		const cy = Math.min(ay, by) + ry;
		this.push(
			`<ellipse cx="${n(cx + o)}" cy="${n(cy + o)}" rx="${n(rx)}" ry="${n(ry)}" fill="none" ${paint("stroke", c)}${sw !== 1 ? ` stroke-width="${n(sw)}"` : ""}/>`,
			{ x0: cx - rx - sw, y0: cy - ry - sw, x1: cx + rx + sw + 1, y1: cy + ry + sw + 1 },
		);
	}

	/** 粗折線，圓角接頭、圓頭，對應 Pen { LineJoin = Round, StartCap = EndCap = Round } 的 DrawLines */
	drawPolyline(points: readonly number[], c: Color, width: number): void {
		if (points.length < 4 || c.a <= 0) return;
		const pts: number[] = [];
		for (let i = 0; i + 1 < points.length; i += 2) pts.push(...this.pt(points[i], points[i + 1]));
		const sw = width * this.scaleFactor;
		const b = boundsOf(pts);
		this.push(
			`<polyline points="${pts.map(n).join(",")}" fill="none" ${paint("stroke", c)} stroke-width="${n(sw)}" stroke-linejoin="round" stroke-linecap="round"/>`,
			{ x0: b.x0 - sw, y0: b.y0 - sw, x1: b.x1 + sw, y1: b.y1 + sw },
		);
	}

	// #endregion

	// #region 文字

	/** 對應 g.DrawString(s, font, brush, x, y)：(x, y) 是文字框左上角 */
	drawString(s: string, font: Font, c: Color, x: number, y: number): void {
		this.text(s, font, c, x + font.size * PAD, y + font.size * ASCENT, "start");
	}

	/** 對應 g.DrawString(s, font, brush, x, y, new StringFormat { Alignment = align })：x 是左緣、中心或右緣 */
	drawStringAt(s: string, font: Font, c: Color, x: number, y: number, align: Align): void {
		this.drawStringIn(s, font, c, { x, y, w: 0, h: 0 }, align, "near");
	}

	/** 對應 g.DrawString(s, font, brush, rect, StringFormat)，單行文字在框內對齊 */
	drawStringIn(s: string, font: Font, c: Color, r: Rect, h: Align = "near", v: Align = "near"): void {
		const pad = font.size * PAD;
		const x = h === "near" ? r.x + pad : h === "far" ? r.x + r.w - pad : r.x + r.w / 2;
		const lineH = font.size * LINE_HEIGHT;
		const top = v === "near" ? r.y : v === "far" ? r.y + r.h - lineH : r.y + (r.h - lineH) / 2;
		this.text(s, font, c, x, top + font.size * ASCENT, h === "near" ? "start" : h === "far" ? "end" : "middle");
	}

	private text(s: string, font: Font, c: Color, x: number, baseline: number, anchor: "start" | "middle" | "end"): void {
		if (!s || c.a <= 0) return;
		const [px, py] = this.pt(x, baseline);
		const size = font.size * this.scaleFactor;
		const w = s.length * size * AVG_CHAR;
		const x0 = anchor === "start" ? px : anchor === "end" ? px - w : px - w / 2;
		const b: Box = { x0, y0: py - size * ASCENT, x1: x0 + w, y1: py + size * DESCENT };
		// 被裁到的文字整個不畫（目前只有遠方山坡後面的看板字會遇到）
		if (this.clip && !(b.y0 >= this.clip.y0 && b.y1 <= this.clip.y1)) return;
		const a = anchor === "start" ? "" : ` text-anchor="${anchor}"`;
		// xml:space 讓連續空白照原樣保留，跟 GDI+ 一樣
		this.push(`<text x="${n(px)}" y="${n(py)}" ${fontAttrs(font, size)}${a} ${paint("fill", c)} xml:space="preserve">${esc(s)}</text>`, b);
	}

	// #endregion

	/**
	 * 輸出 SVG。給 view 的話只輸出那一塊（例如 800px 寬的觸控條切成 4 格），
	 * 並且只放跟那一塊重疊的元素：畫面沒變的格子輸出的字串也不變，外面可以直接比字串省掉重送。
	 */
	toString(view?: Rect): string {
		const v = view ?? { x: 0, y: 0, w: this.width, h: this.height };
		const vb: Box = { x0: v.x, y0: v.y, x1: v.x + v.w, y1: v.y + v.h };
		let body = "";
		for (const p of this.parts) if (!view || !disjoint(p.box, vb)) body += p.s;
		return (
			`<svg xmlns="http://www.w3.org/2000/svg" width="${v.w}" height="${v.h}" viewBox="${v.x} ${v.y} ${v.w} ${v.h}" shape-rendering="${this.smooth ? "auto" : "crispEdges"}">` +
			body +
			"</svg>"
		);
	}
}
