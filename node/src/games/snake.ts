import { Color, Colors, hex, rgb } from "../gfx/color";
import { Font, Svg } from "../gfx/svg";

/**
 * 在 Stream Deck+ 觸控條（每顆旋鈕 200x100）上玩的貪食蛇。
 * 旋鈕順時針一格 = 右轉 90 度，逆時針一格 = 左轉 90 度（相對蛇頭方向）。
 * 旋鈕按下 / 觸控 = 開始、暫停，觸控長按 = 重新開始。
 * 這裡只有遊戲本體，跟 Stream Deck 的連線在 actions/snake.ts。
 */

type GameState = "idle" | "playing" | "paused" | "gameOver";

export type SnakeSpeed = "slow" | "normal" | "fast";

const W = 200;
const H = 100;
const HUD_H = 10;
const CELL = 5;
const COLS = W / CELL; // 40
const ROWS = (H - HUD_H) / CELL; // 18
const START_LEN = 4;
const MIN_STEP_MS = 60;
const STEP_SPEEDUP_MS = 3; // 每吃一個縮短的毫秒數
const MAX_PENDING_TURNS = 3; // 轉太快時最多先記住幾次轉向

const HeadColor = hex(0xb8ff7a);
const BodyColor = hex(0x5ad65a);
const FoodColor = hex(0xff4c4c);
const LineColor = hex(0x303030);
const Dim = rgb(0, 0, 0, 0xb0);

const HudFont: Font = { size: 8, bold: true };
const BigFont: Font = { size: 16, bold: true };
const SmallFont: Font = { size: 10 };

type Point = { x: number; y: number };

const key = (p: Point) => p.y * COLS + p.x;

export class SnakeGame {
	/** 破紀錄時呼叫，讓外面存進設定 */
	onBest?: (best: number) => void;

	// 蛇身：[0] 是頭，最後一個是尾；occupied 用來 O(1) 判斷撞到自己
	private snake: Point[] = [];
	private readonly occupied = new Set<number>();
	private readonly pendingTurns: number[] = [];

	private state: GameState = "idle";
	private dx = 1;
	private dy = 0;
	private food: Point = { x: 0, y: 0 };
	private score = 0;
	private best = 0;
	private stepMs = 0;
	private won = false;

	private speed: SnakeSpeed = "normal";
	private wrap = false;

	private timer: ReturnType<typeof setInterval> | undefined;
	private disposed = false;

	constructor(private readonly draw: (svg: string) => void) {
		this.resetSnake(); // Idle 畫面先擺一條蛇當預覽
	}

	configure(speed: SnakeSpeed, wrap: boolean, best: number): void {
		this.speed = speed;
		this.wrap = wrap;
		// PI 送來的設定不含最高分，保留這邊的
		this.best = Math.max(this.best, best);
	}

	get bestScore(): number {
		return this.best;
	}

	dispose(): void {
		this.disposed = true;
		this.stopTimer();
	}

	// #region 輸入

	rotate(ticks: number): void {
		if (this.state !== "playing") return;

		// 一格轉一次；一次轉很多格就排隊，每一步只吃一個轉向，避免直接 180 度咬到自己
		const turn = ticks > 0 ? 1 : -1;
		for (let i = 0; i < Math.abs(ticks) && this.pendingTurns.length < MAX_PENDING_TURNS; i++) this.pendingTurns.push(turn);
	}

	/** 旋鈕按下 / 觸控點一下 */
	press(): void {
		switch (this.state) {
			case "idle":
			case "gameOver":
				this.newGame();
				break;
			case "playing":
				this.state = "paused";
				this.stopTimer();
				break;
			case "paused":
				this.state = "playing";
				this.startTimer();
				break;
		}
		this.render();
	}

	/** 觸控長按 */
	restart(): void {
		this.newGame();
		this.render();
	}

	// #endregion

	// #region 遊戲流程

	private baseStepMs(): number {
		switch (this.speed) {
			case "slow":
				return 200;
			case "fast":
				return 100;
			default:
				return 150;
		}
	}

	private resetSnake(): void {
		this.snake = [];
		this.occupied.clear();
		this.pendingTurns.length = 0;
		this.dx = 1;
		this.dy = 0;

		const y = Math.floor(ROWS / 2);
		const headX = Math.floor(COLS / 2);
		for (let i = 0; i < START_LEN; i++) {
			const p = { x: headX - i, y };
			this.snake.push(p);
			this.occupied.add(key(p));
		}
		this.placeFood();
	}

	private newGame(): void {
		this.stopTimer();
		this.resetSnake();
		this.score = 0;
		this.won = false;
		this.stepMs = this.baseStepMs();
		this.state = "playing";
		this.startTimer();
	}

	/** 回傳 false 代表格子滿了（贏了） */
	private placeFood(): boolean {
		const free = COLS * ROWS - this.occupied.size;
		if (free <= 0) return false;

		// 隨機挑第 n 個空格，避免蛇很長時一直亂數撞到身體
		let n = Math.floor(Math.random() * free);
		for (let y = 0; y < ROWS; y++) {
			for (let x = 0; x < COLS; x++) {
				if (this.occupied.has(y * COLS + x)) continue;
				if (n-- === 0) {
					this.food = { x, y };
					return true;
				}
			}
		}
		return false;
	}

	private startTimer(): void {
		this.stopTimer();
		if (this.disposed) return;
		this.timer = setInterval(() => this.onStep(), this.stepMs);
	}

	private stopTimer(): void {
		if (this.timer !== undefined) clearInterval(this.timer);
		this.timer = undefined;
	}

	private onStep(): void {
		if (this.state === "playing") this.step();
		this.render();
	}

	/** 前進一格：轉向、撞牆、撞自己、吃東西 */
	private step(): void {
		const turn = this.pendingTurns.shift();
		if (turn !== undefined) {
			const { dx, dy } = this;
			if (turn > 0) {
				// 螢幕座標 y 朝下：順時針 = 右轉
				this.dx = -dy;
				this.dy = dx;
			} else {
				// 逆時針 = 左轉
				this.dx = dy;
				this.dy = -dx;
			}
		}

		const head = this.snake[0];
		let nx = head.x + this.dx;
		let ny = head.y + this.dy;

		if (this.wrap) {
			nx = (nx + COLS) % COLS;
			ny = (ny + ROWS) % ROWS;
		} else if (nx < 0 || nx >= COLS || ny < 0 || ny >= ROWS) {
			this.gameOver();
			return;
		}

		const next = { x: nx, y: ny };
		const grow = nx === this.food.x && ny === this.food.y;

		// 沒吃到東西時尾巴會先移走，所以頭可以走進原本尾巴的位置
		if (!grow) {
			const tail = this.snake.pop()!;
			this.occupied.delete(key(tail));
		}

		if (this.occupied.has(key(next))) {
			this.gameOver();
			return;
		}

		this.snake.unshift(next);
		this.occupied.add(key(next));

		if (grow) {
			this.score++;
			if (!this.placeFood()) {
				this.won = true;
				this.gameOver();
				return;
			}

			const newStep = Math.max(MIN_STEP_MS, this.stepMs - STEP_SPEEDUP_MS);
			if (newStep !== this.stepMs) {
				this.stepMs = newStep;
				this.startTimer();
			}
		}
	}

	private gameOver(): void {
		this.state = "gameOver";
		this.stopTimer();

		if (this.score > this.best) {
			this.best = this.score;
			this.onBest?.(this.best);
		}
	}

	// #endregion

	// #region 繪圖

	render(): void {
		if (this.disposed) return;
		this.draw(this.drawFrame().toString());
	}

	private drawFrame(): Svg {
		const g = new Svg(W, H);
		g.clear(Colors.Black);

		// HUD 跟遊戲區的分隔線；穿牆模式用虛線提示上緣可以穿
		g.drawLine(0, HUD_H - 1, W, HUD_H - 1, LineColor, this.wrap ? { dash: [2, 2] } : {});

		// 食物、蛇（Idle 時不畫，免得壓到標題文字）
		if (this.state !== "idle") {
			cell(g, this.food, FoodColor);
			this.snake.forEach((p, i) => cell(g, p, i === 0 ? HeadColor : BodyColor));
		}

		// HUD：分數、最高分
		g.drawString(String(this.score), HudFont, Colors.White, 1, 0);
		g.drawStringIn(`BEST ${this.best}`, HudFont, Colors.Gray, { x: 0, y: 0, w: W - 1, h: HUD_H }, "far");

		// 狀態文字：先壓暗背景再寫字，蛇在底下也看得清楚
		const area = { x: 0, y: HUD_H, w: W, h: H - HUD_H };
		const upper = { x: 0, y: HUD_H + 15, w: W, h: 30 };
		const lower = { x: 0, y: HUD_H + 45, w: W, h: 20 };
		switch (this.state) {
			case "idle":
				g.fillRect(area.x, area.y, area.w, area.h, Dim);
				g.drawStringIn("SNAKE", BigFont, Colors.White, upper, "center", "center");
				g.drawStringIn("press dial to start", SmallFont, Colors.Gray, lower, "center", "center");
				break;
			case "paused":
				g.fillRect(area.x, area.y, area.w, area.h, Dim);
				g.drawStringIn("PAUSED", BigFont, Colors.White, area, "center", "center");
				break;
			case "gameOver": {
				g.fillRect(area.x, area.y, area.w, area.h, Dim);
				const c: Color = this.won ? Colors.Gold : Colors.OrangeRed;
				g.drawStringIn(this.won ? "YOU WIN" : "GAME OVER", BigFont, c, upper, "center", "center");
				g.drawStringIn(`score ${this.score}   press to retry`, SmallFont, Colors.Gray, lower, "center", "center");
				break;
			}
		}
		return g;
	}

	// #endregion
}

/** 每格 5x5，畫 4x4 留 1px 縫，看得出一節一節 */
function cell(g: Svg, p: Point, c: Color): void {
	g.fillRect(p.x * CELL, HUD_H + p.y * CELL, CELL - 1, CELL - 1, c);
}
