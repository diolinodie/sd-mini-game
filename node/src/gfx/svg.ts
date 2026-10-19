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
// 估字寬用（Arial 平均字寬約 0.6 em），只拿來判斷文字落在哪幾格
const AVG_CHAR = 0.6;

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

/** 一個圖形元素跟它的水平範圍；切格輸出時用範圍挑出跟那一格重疊的元素 */
type Part = { readonly s: string; readonly x0: number; readonly x1: number };

/**
 * 用 GDI+ 的呼叫方式組一張 SVG，取代 C# 的 Bitmap + Graphics。
 * 座標跟 GDI+ 一樣是像素、左上角原點、y 朝下。
 * 只用 SVG Tiny 1.2 有的東西（Stream Deck 用 Qt 畫 SVG），所以不用 dominant-baseline、rgba() 這類。
 */
export class Svg {
	private readonly parts: Part[] = [];

	constructor(
		readonly width: number,
		readonly height: number,
	) {}

	private push(s: string, x0: number, x1: number): void {
		this.parts.push({ s, x0: Math.min(x0, x1), x1: Math.max(x0, x1) });
	}

	clear(c: Color): void {
		this.parts.length = 0;
		this.fillRect(0, 0, this.width, this.height, c);
	}

	fillRect(x: number, y: number, w: number, h: number, c: Color): void {
		if (w <= 0 || h <= 0 || c.a <= 0) return;
		this.push(`<rect x="${n(x)}" y="${n(y)}" width="${n(w)}" height="${n(h)}" ${paint("fill", c)}/>`, x, x + w);
	}

	/** 對應 g.DrawRectangle：1px 框線，跟 GDI+ 一樣涵蓋 x..x+w 的像素 */
	drawRect(x: number, y: number, w: number, h: number, c: Color): void {
		if (c.a <= 0) return;
		this.push(`<rect x="${n(x + 0.5)}" y="${n(y + 0.5)}" width="${n(w)}" height="${n(h)}" fill="none" ${paint("stroke", c)}/>`, x, x + w + 1);
	}

	/** 跟 GDI+ 一樣，整數座標的 1px 線會剛好落在那一排像素上 */
	drawLine(x1: number, y1: number, x2: number, y2: number, c: Color, opt: LineOptions = {}): void {
		const w = opt.width ?? 1;
		const o = w % 2 === 1 ? 0.5 : 0;
		let s = `<line x1="${n(x1 + o)}" y1="${n(y1 + o)}" x2="${n(x2 + o)}" y2="${n(y2 + o)}" ${paint("stroke", c)}`;
		if (w !== 1) s += ` stroke-width="${n(w)}"`;
		if (opt.dash) s += ` stroke-dasharray="${opt.dash.map(n).join(",")}"`;
		this.push(s + "/>", Math.min(x1, x2) - w, Math.max(x1, x2) + w);
	}

	fillPolygon(points: readonly number[], c: Color): void {
		if (points.length < 6 || c.a <= 0) return;
		let x0 = Infinity;
		let x1 = -Infinity;
		for (let i = 0; i < points.length; i += 2) {
			x0 = Math.min(x0, points[i]);
			x1 = Math.max(x1, points[i]);
		}
		this.push(`<polygon points="${points.map(n).join(",")}" ${paint("fill", c)}/>`, x0, x1);
	}

	/** smooth = 反鋸齒，對應 SmoothingMode.AntiAlias */
	fillEllipse(x: number, y: number, w: number, h: number, c: Color, smooth = false): void {
		if (w <= 0 || h <= 0 || c.a <= 0) return;
		const aa = smooth ? ` shape-rendering="auto"` : "";
		this.push(`<ellipse cx="${n(x + w / 2)}" cy="${n(y + h / 2)}" rx="${n(w / 2)}" ry="${n(h / 2)}" ${paint("fill", c)}${aa}/>`, x, x + w);
	}

	/** 對應 g.DrawEllipse：1px 框線 */
	drawEllipse(x: number, y: number, w: number, h: number, c: Color, smooth = false): void {
		if (c.a <= 0) return;
		const aa = smooth ? ` shape-rendering="auto"` : "";
		this.push(
			`<ellipse cx="${n(x + w / 2 + 0.5)}" cy="${n(y + h / 2 + 0.5)}" rx="${n(w / 2)}" ry="${n(h / 2)}" fill="none" ${paint("stroke", c)}${aa}/>`,
			x,
			x + w + 1,
		);
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
		const w = s.length * font.size * AVG_CHAR;
		const x0 = anchor === "start" ? x : anchor === "end" ? x - w : x - w / 2;
		// xml:space 讓連續空白照原樣保留，跟 GDI+ 一樣
		this.push(`<text x="${n(x)}" y="${n(baseline)}" ${fontAttrs(font)}${a} ${paint("fill", c)} xml:space="preserve">${esc(s)}</text>`, x0, x0 + w);
	}

	/**
	 * 輸出 SVG。給 view 的話只輸出那一塊（例如 800px 寬的觸控條切成 4 格），
	 * 並且只放跟那一塊重疊的元素：畫面沒變的格子輸出的字串也不變，外面可以直接比字串省掉重送。
	 */
	toString(view?: Rect): string {
		const v = view ?? { x: 0, y: 0, w: this.width, h: this.height };
		let body = "";
		for (const p of this.parts) if (!view || (p.x1 > v.x && p.x0 < v.x + v.w)) body += p.s;
		return (
			`<svg xmlns="http://www.w3.org/2000/svg" width="${v.w}" height="${v.h}" viewBox="${v.x} ${v.y} ${v.w} ${v.h}" shape-rendering="crispEdges">` +
			body +
			"</svg>"
		);
	}
}
