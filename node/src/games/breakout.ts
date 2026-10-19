import type { DialGame } from "../actions/dial-game";
import { Colors, hex } from "../gfx/color";
import { Font, Svg } from "../gfx/svg";

/**
 * 在 Stream Deck+ 觸控條（每顆旋鈕 200x100）上玩的打磚塊。
 * 旋鈕轉動 = 移動球拍，旋鈕按下 / 觸控 = 開始、發球、暫停，觸控長按 = 重新開始。
 * 這裡只有遊戲本體，跟 Stream Deck 的連線在 actions/breakout.ts。
 */

type GameState = "idle" | "ready" | "playing" | "paused" | "gameOver";

export type BallSpeed = "slow" | "normal" | "fast";

const W = 200;
const H = 100;
const HUD_H = 10; // 最上面一條留給分數 / 關卡 / 命
const BRICK_ROWS = 4;
const BRICK_COLS = 10;
const BRICK_W = 19;
const BRICK_H = 6;
const BRICK_GAP = 1;
const BRICK_TOP = HUD_H + 1;
const PADDLE_W = 32;
const PADDLE_H = 3;
const PADDLE_Y = H - 5;
const BALL_R = 2;
const START_LIVES = 3;
const FRAME_MS = 40; // 25 fps
const LEVEL_SPEEDUP = 1.15; // 每過一關球速乘上這個

const RowColors = [hex(0xff4c4c), hex(0xffa52c), hex(0xffe13a), hex(0x5ad65a)];

const HudFont: Font = { size: 8, bold: true };
const BigFont: Font = { size: 16, bold: true };
const SmallFont: Font = { size: 10 };

const rowAt = (y: number) => Math.floor((y - BRICK_TOP) / (BRICK_H + BRICK_GAP));
const colAt = (x: number) => Math.floor(x / (BRICK_W + BRICK_GAP));

export class BreakoutGame implements DialGame {
	private readonly bricks: boolean[][] = Array.from({ length: BRICK_ROWS }, () => new Array<boolean>(BRICK_COLS).fill(true));

	private state: GameState = "idle";
	private paddleX = (W - PADDLE_W) / 2;
	private ballX = 0;
	private ballY = 0;
	private ballVx = 0;
	private ballVy = 0;
	private speed = 0;
	private bricksLeft = 0;
	private score = 0;
	private lives = 0;
	private level = 1;

	private paddleStep = 8;
	private ballSpeed: BallSpeed = "normal";

	private timer: ReturnType<typeof setInterval> | undefined;
	private disposed = false;

	constructor(private readonly draw: (svg: string) => void) {
		this.speed = this.baseSpeed();
		this.resetBricks(); // Idle 畫面先把磚塊排好當預覽
	}

	configure(paddleStep: number, ballSpeed: BallSpeed): void {
		this.paddleStep = paddleStep > 0 ? paddleStep : 8;
		this.ballSpeed = ballSpeed;
		// 進行中不改速度，避免球突然變快；其他狀態直接套用
		if (this.state !== "playing") this.speed = this.baseSpeed() * Math.pow(LEVEL_SPEEDUP, this.level - 1);
	}

	dispose(): void {
		this.disposed = true;
		this.stopTimer();
	}

	// #region 輸入

	rotate(ticks: number): void {
		if (this.state === "gameOver") return;

		this.paddleX += ticks * this.paddleStep;
		this.paddleX = Math.max(0, Math.min(W - PADDLE_W, this.paddleX));

		// 球還黏在球拍上就跟著走
		if (this.state === "ready") this.ballX = this.paddleX + PADDLE_W / 2;

		// Playing 時由 timer 負責畫
		if (this.state !== "playing") this.render();
	}

	/** 旋鈕按下 / 觸控點一下 */
	press(): void {
		switch (this.state) {
			case "idle":
			case "gameOver":
				this.newGame();
				break;
			case "ready":
				this.launch();
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

	/** 觸控點一下 = 跟按旋鈕一樣，長按 = 重新開始 */
	touch(hold: boolean): void {
		if (!hold) {
			this.press();
			return;
		}
		this.newGame();
		this.render();
	}

	// #endregion

	// #region 遊戲流程

	private baseSpeed(): number {
		switch (this.ballSpeed) {
			case "slow":
				return 1.5;
			case "fast":
				return 2.6;
			default:
				return 2.0;
		}
	}

	private newGame(): void {
		this.stopTimer();
		this.score = 0;
		this.lives = START_LIVES;
		this.level = 1;
		this.speed = this.baseSpeed();
		this.paddleX = (W - PADDLE_W) / 2;
		this.resetBricks();
		this.attachBall();
		this.state = "ready";
	}

	private resetBricks(): void {
		for (const row of this.bricks) row.fill(true);
		this.bricksLeft = BRICK_ROWS * BRICK_COLS;
	}

	private attachBall(): void {
		this.ballX = this.paddleX + PADDLE_W / 2;
		this.ballY = PADDLE_Y - BALL_R - 1;
		this.ballVx = 0;
		this.ballVy = 0;
	}

	private launch(): void {
		// 往上隨機偏個角度，±35 度以內
		const angle = (Math.random() - 0.5) * 1.2;
		this.ballVx = this.speed * Math.sin(angle);
		this.ballVy = -this.speed * Math.cos(angle);
		this.state = "playing";
		this.startTimer();
	}

	private loseLife(): void {
		this.lives--;
		if (this.lives <= 0) {
			this.state = "gameOver";
		} else {
			this.attachBall();
			this.state = "ready";
		}
		this.stopTimer();
	}

	private nextLevel(): void {
		this.level++;
		this.speed *= LEVEL_SPEEDUP;
		this.resetBricks();
		this.attachBall();
		this.state = "ready";
		this.stopTimer();
	}

	private startTimer(): void {
		this.stopTimer();
		if (this.disposed) return;
		this.timer = setInterval(() => this.onFrame(), FRAME_MS);
	}

	private stopTimer(): void {
		if (this.timer !== undefined) clearInterval(this.timer);
		this.timer = undefined;
	}

	private onFrame(): void {
		if (this.state === "playing") this.step();
		this.render();
	}

	/** 一幀的物理：牆、球拍、磚塊、掉出底部 */
	private step(): void {
		let nx = this.ballX + this.ballVx;
		let ny = this.ballY + this.ballVy;

		// 左右牆
		if (nx - BALL_R < 0) {
			nx = BALL_R;
			this.ballVx = -this.ballVx;
		} else if (nx + BALL_R > W) {
			nx = W - BALL_R;
			this.ballVx = -this.ballVx;
		}

		// 上緣（HUD 下面）
		if (ny - BALL_R < HUD_H) {
			ny = HUD_H + BALL_R;
			this.ballVy = -this.ballVy;
		}

		// 球拍：只在往下掉、且這一幀剛好穿過球拍上緣時反彈
		if (
			this.ballVy > 0 &&
			ny + BALL_R >= PADDLE_Y &&
			this.ballY + BALL_R <= PADDLE_Y + PADDLE_H &&
			nx >= this.paddleX - BALL_R &&
			nx <= this.paddleX + PADDLE_W + BALL_R
		) {
			ny = PADDLE_Y - BALL_R;
			// 打到球拍哪個位置決定反彈角度，邊緣最多約 60 度
			let offset = (nx - (this.paddleX + PADDLE_W / 2)) / (PADDLE_W / 2);
			offset = Math.max(-1, Math.min(1, offset));
			// 正中接球也給一點角度，不然球會垂直上下穿同一條縫
			if (Math.abs(offset) < 0.12) offset = (this.ballVx >= 0 ? 1 : -1) * 0.12;
			const angle = offset * 1.05;
			this.ballVx = this.speed * Math.sin(angle);
			this.ballVy = -this.speed * Math.cos(angle);
		}

		// 掉出底部
		if (ny - BALL_R > H) {
			this.loseLife();
			return;
		}

		// 磚塊：找球框框碰到的第一顆
		const rowMin = rowAt(ny - BALL_R);
		const rowMax = rowAt(ny + BALL_R);
		const colMin = colAt(nx - BALL_R);
		const colMax = colAt(nx + BALL_R);
		let hit = false;
		for (let r = Math.max(0, rowMin); r <= Math.min(BRICK_ROWS - 1, rowMax) && !hit; r++) {
			for (let c = Math.max(0, colMin); c <= Math.min(BRICK_COLS - 1, colMax) && !hit; c++) {
				if (!this.bricks[r][c]) continue;

				const bx = c * (BRICK_W + BRICK_GAP);
				const by = BRICK_TOP + r * (BRICK_H + BRICK_GAP);
				if (nx + BALL_R <= bx || nx - BALL_R >= bx + BRICK_W || ny + BALL_R <= by || ny - BALL_R >= by + BRICK_H) continue;

				this.bricks[r][c] = false;
				this.bricksLeft--;
				this.score += (BRICK_ROWS - r) * 10;
				hit = true;

				// 上一幀的 x 已經在磚塊範圍內 → 從上下打到；否則是從側面
				if (this.ballX >= bx && this.ballX <= bx + BRICK_W) this.ballVy = -this.ballVy;
				else this.ballVx = -this.ballVx;
			}
		}

		this.ballX = nx;
		this.ballY = ny;

		if (this.bricksLeft <= 0) this.nextLevel();
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

		// 磚塊
		for (let r = 0; r < BRICK_ROWS; r++) {
			const color = RowColors[r % RowColors.length];
			for (let c = 0; c < BRICK_COLS; c++) {
				if (!this.bricks[r][c]) continue;
				g.fillRect(c * (BRICK_W + BRICK_GAP), BRICK_TOP + r * (BRICK_H + BRICK_GAP), BRICK_W, BRICK_H, color);
			}
		}

		// 球拍
		g.fillRect(Math.round(this.paddleX), PADDLE_Y, PADDLE_W, PADDLE_H, Colors.White);

		// 球
		if (this.state !== "idle" && this.state !== "gameOver") {
			g.fillRect(Math.round(this.ballX) - BALL_R, Math.round(this.ballY) - BALL_R, BALL_R * 2, BALL_R * 2, Colors.White);
		}

		// HUD：分數、關卡、命
		if (this.state !== "idle") {
			g.drawString(String(this.score), HudFont, Colors.White, 1, 0);
			g.drawStringIn(`L${this.level}`, HudFont, Colors.Gray, { x: 0, y: 0, w: W, h: HUD_H }, "center");
			for (let i = 0; i < this.lives; i++) g.fillRect(W - 5 - i * 6, 3, 4, 4, Colors.White);
		}

		// 狀態文字蓋在玩區中間
		const playTop = BRICK_TOP + BRICK_ROWS * (BRICK_H + BRICK_GAP);
		const playArea = { x: 0, y: playTop, w: W, h: PADDLE_Y - playTop };
		const upper = { x: 0, y: playArea.y, w: W, h: playArea.h * 0.6 };
		const lower = { x: 0, y: playArea.y + playArea.h * 0.55, w: W, h: playArea.h * 0.45 };
		switch (this.state) {
			case "idle":
				g.drawStringIn("BREAKOUT", BigFont, Colors.White, upper, "center", "center");
				g.drawStringIn("press dial to start", SmallFont, Colors.Gray, lower, "center", "center");
				break;
			case "ready":
				g.drawStringIn("press to launch", SmallFont, Colors.Gray, playArea, "center", "center");
				break;
			case "paused":
				g.drawStringIn("PAUSED", BigFont, Colors.White, playArea, "center", "center");
				break;
			case "gameOver":
				g.drawStringIn("GAME OVER", BigFont, Colors.OrangeRed, upper, "center", "center");
				g.drawStringIn(`score ${this.score}   press to retry`, SmallFont, Colors.Gray, lower, "center", "center");
				break;
		}
		return g;
	}

	// #endregion
}
