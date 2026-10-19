/**
 * 顏色，對應 C# 的 System.Drawing.Color。a 跟 GDI+ 一樣是 0-255。
 */
export type Color = { readonly r: number; readonly g: number; readonly b: number; readonly a: number };

const clamp = (v: number) => Math.max(0, Math.min(255, Math.round(v)));

export function rgb(r: number, g: number, b: number, a = 255): Color {
	return { r: clamp(r), g: clamp(g), b: clamp(b), a: clamp(a) };
}

/** 0xRRGGBB 寫法，跟 C# 的 Color.FromArgb(0xRR, 0xGG, 0xBB) 對照方便 */
export function hex(v: number, a = 255): Color {
	return rgb((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff, a);
}

export function withAlpha(c: Color, a: number): Color {
	return rgb(c.r, c.g, c.b, a);
}

export function mix(a: Color, b: Color, t: number): Color {
	t = Math.max(0, Math.min(1, t));
	return rgb(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
}

export function darker(c: Color, f = 0.7): Color {
	return rgb(c.r * f, c.g * f, c.b * f, c.a);
}

export function lighter(c: Color, f = 0.4): Color {
	return mix(c, Colors.White, f);
}

/** "#rrggbb"，透明度另外用 fill-opacity / stroke-opacity 帶，Qt 的 SVG 不一定吃 rgba() */
export function toHex(c: Color): string {
	return "#" + ((1 << 24) | (c.r << 16) | (c.g << 8) | c.b).toString(16).slice(1);
}

/** C# 用到的 Brushes.* / Color.* 具名色 */
export const Colors = {
	Black: hex(0x000000),
	White: hex(0xffffff),
	Gray: hex(0x808080),
	Gold: hex(0xffd700),
	OrangeRed: hex(0xff4500),
	Orange: hex(0xffa500),
	LightGray: hex(0xd3d3d3),
	Transparent: hex(0x000000, 0),
} as const;
