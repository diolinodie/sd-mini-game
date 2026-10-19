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
const LINE_HEIGHT = (1854 + 434 + 67) / 2048;
// GDI+ DrawString 預設在字串左右各留 1/6 em
const PAD = 1 / 6;

const n = (v: number) => (Math.round(v * 100) / 100).toString();

function esc(s: string): string {
	return s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
}

function paint(attr: "fill" | "stroke", c: Color): string {
	return c.a >= 255 ? `${attr}="${toHex(c)}"` : `${attr}="${toHex(c)}" ${attr}-opacity="${n(c.a / 255)}"`;
}

function fontAttrs(f: Font): string {
	let s = `font-family="${f.family ?? "Arial"}" font-size="${n(f.size)}"`;
	if (f.bold) s += ` font-weight="bold"`;
	if (f.italic) s += ` font-style="italic"`;
	return s;
}

/**
 * 用 GDI+ 的呼叫方式組一張 SVG，取代 C# 的 Bitmap + Graphics。
 * 座標跟 GDI+ 一樣是像素、左上角原點、y 朝下。
 * 只用 SVG Tiny 1.2 有的東西（Stream Deck 用 Qt 畫 SVG），所以不用 dominant-baseline、rgba() 這類。
 */
export class Svg {
	private readonly parts: string[] = [];

	constructor(
		readonly width: number,
		readonly height: number,
	) {}

	clear(c: Color): void {
		this.parts.length = 0;
		this.fillRect(0, 0, this.width, this.height, c);
	}

	fillRect(x: number, y: number, w: number, h: number, c: Color): void {
		if (w <= 0 || h <= 0 || c.a <= 0) return;
		this.parts.push(`<rect x="${n(x)}" y="${n(y)}" width="${n(w)}" height="${n(h)}" ${paint("fill", c)}/>`);
	}

	/** 跟 GDI+ 一樣，整數座標的 1px 線會剛好落在那一排像素上 */
	drawLine(x1: number, y1: number, x2: number, y2: number, c: Color, opt: LineOptions = {}): void {
		const w = opt.width ?? 1;
		const o = w % 2 === 1 ? 0.5 : 0;
		let s = `<line x1="${n(x1 + o)}" y1="${n(y1 + o)}" x2="${n(x2 + o)}" y2="${n(y2 + o)}" ${paint("stroke", c)}`;
		if (w !== 1) s += ` stroke-width="${n(w)}"`;
		if (opt.dash) s += ` stroke-dasharray="${opt.dash.map(n).join(",")}"`;
		this.parts.push(s + "/>");
	}

	fillPolygon(points: readonly number[], c: Color): void {
		if (points.length < 6 || c.a <= 0) return;
		this.parts.push(`<polygon points="${points.map(n).join(",")}" ${paint("fill", c)}/>`);
	}

	fillEllipse(x: number, y: number, w: number, h: number, c: Color): void {
		if (w <= 0 || h <= 0 || c.a <= 0) return;
		this.parts.push(`<ellipse cx="${n(x + w / 2)}" cy="${n(y + h / 2)}" rx="${n(w / 2)}" ry="${n(h / 2)}" ${paint("fill", c)}/>`);
	}

	/** 對應 g.DrawString(s, font, brush, x, y)：(x, y) 是文字框左上角 */
	drawString(s: string, font: Font, c: Color, x: number, y: number): void {
		this.text(s, font, c, x + font.size * PAD, y + font.size * ASCENT, "start");
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
		const a = anchor === "start" ? "" : ` text-anchor="${anchor}"`;
		// xml:space 讓連續空白照原樣保留，跟 GDI+ 一樣
		this.parts.push(`<text x="${n(x)}" y="${n(baseline)}" ${fontAttrs(font)}${a} ${paint("fill", c)} xml:space="preserve">${esc(s)}</text>`);
	}

	toString(): string {
		return (
			`<svg xmlns="http://www.w3.org/2000/svg" width="${this.width}" height="${this.height}" viewBox="0 0 ${this.width} ${this.height}" shape-rendering="crispEdges">` +
			this.parts.join("") +
			"</svg>"
		);
	}
}
