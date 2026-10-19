import { Color, Colors, hex, rgb } from "../gfx/color";
import { Font, Svg } from "../gfx/svg";

/**
 * 手足球機（桌上足球）的比賽狀態。同一台 Stream Deck+ 上的 4 個 Foosball action 共用一個實例，
 * 整條觸控條 800x100 是球場，每個 action 只顯示自己那一格 200x100。
 *
 * 桿子順序跟真實足球機一樣（左到右）：
 *   紅 守門員、紅 後衛、藍 前鋒、紅 中場、藍 中場、紅 前鋒、藍 後衛、藍 守門員
 * 旋鈕控制：
 *   旋鈕 1（紅）：紅 守門員 + 紅 後衛
 *   旋鈕 2（藍）：藍 前鋒 + 藍 中場
 *   旋鈕 3（紅）：紅 中場 + 紅 前鋒
 *   旋鈕 4（藍）：藍 後衛 + 藍 守門員
 * 紅隊守左邊球門往右攻，藍隊守右邊球門往左攻。
 * 球員正反兩面都會擋球（自己人也是），按住旋鈕可以抬腳讓球過去；
 * 腳前的球往前踢，背後的球往後撥（比較弱）。
 */

export const SLOTS = 4;
export const SLOT_W = 200;
const W = SLOT_W * SLOTS; // 800
const H = 100;
const FIELD_TOP = 10; // 上面 10px 是記分列
const FIELD_H = H - FIELD_TOP; // 90
const GOAL_H = 30;
const GOAL_TOP = FIELD_TOP + (FIELD_H - GOAL_H) / 2;
const GOAL_BOTTOM = GOAL_TOP + GOAL_H;

const PW = 6; // 球員寬
const PH = 10; // 球員高
const BALL_R = 3;

const FRAME_MS = 40; // 25 fps
const KICK_FRAMES = 6; // 踢球動畫總幀數
const KICK_ACTIVE = 3; // 前幾幀碰到球才算踢到
const KICK_REACH = 16; // 桿子左右多遠內的球踢得到
const KICK_SPEED = 11;
const BACK_KICK_SPEED = 5; // 背後的球往後撥的速度
const BOUNCE_JITTER = 0.4; // 碰撞時加一點上下亂數，免得球永遠在同一條線上來回彈
const MAX_VX = 12;
const MAX_VY = 5;
const MIN_VX = 0.8; // 球不會完全停下來，避免卡在兩支桿子中間
const FRICTION = 0.992;
const GOAL_PAUSE_FRAMES = 40; // 進球後停 1.6 秒

const TEAM_RED = 0;
const TEAM_BLUE = 1;

const RedColor = hex(0xff4c4c);
const BlueColor = hex(0x4c8bff);
const FieldColor = hex(0x1f6b32);
const LineColor = rgb(255, 255, 255, 110);
const RodColor = rgb(170, 170, 170, 150);
const RedFoot = hex(0xffa0a0);
const BlueFoot = hex(0xa0c4ff);
const HeadColor = rgb(0, 0, 0, 120);
const Dim = rgb(0, 0, 0, 180);
const KickReach = [6, 12, 16, 12, 8, 3]; // 踢球每一幀腳伸出去的長度

const HudFont: Font = { size: 8, bold: true };
const BigFont: Font = { size: 18, bold: true };
const SmallFont: Font = { size: 10 };

type GameState = "idle" | "playing" | "goal" | "paused" | "gameOver";

class Rod {
	offset = 0;
	kick = 0; // >0 表示踢球動畫進行中，倒數
	kickHit = false; // 這一腳已經踢到球
	held = false; // 旋鈕還按著
	readonly spacing: number;
	readonly maxOffset: number;

	constructor(
		readonly team: number,
		readonly column: number,
		readonly x: number,
		readonly players: number,
	) {
		this.spacing = FIELD_H / players;
		// 守門員只顧球門前；其他桿子剛好讓相鄰球員的範圍接起來、最外側的球員碰到邊線
		this.maxOffset = players === 1 ? GOAL_H / 2 + 5 : this.spacing / 2 - PH / 2;
	}

	/** 踢完還按著旋鈕 = 桿子轉起來、球員抬腳，球可以從底下過去 */
	get lifted(): boolean {
		return this.held && this.kick === 0;
	}

	get dir(): number {
		return this.team === TEAM_RED ? 1 : -1;
	}

	playerY(i: number): number {
		const baseY = this.players === 1 ? FIELD_H / 2 : this.spacing * (i + 0.5);
		return FIELD_TOP + baseY + this.offset;
	}
}

const teamOfColumn = (column: number) => (column % 2 === 0 ? TEAM_RED : TEAM_BLUE);
const randSign = () => (Math.random() < 0.5 ? -1 : 1);

export type Sink = (svg: string) => void;

export class FoosballGame {
	// #region 共用實例（每台裝置一個）

	private static readonly games = new Map<string, FoosballGame>();
	private refCount = 0;

	static acquire(deviceId: string): FoosballGame {
		let game = FoosballGame.games.get(deviceId);
		if (!game) {
			game = new FoosballGame(deviceId);
			FoosballGame.games.set(deviceId, game);
		}
		game.refCount++;
		return game;
	}

	release(): void {
		if (--this.refCount > 0) return;
		FoosballGame.games.delete(this.deviceId);
		this.disposed = true;
		this.stopTimer();
	}

	// #endregion

	private readonly rods = [
		new Rod(TEAM_RED, 0, 25, 1), // 紅 守門員
		new Rod(TEAM_RED, 0, 125, 2), // 紅 後衛
		new Rod(TEAM_BLUE, 1, 250, 3), // 藍 前鋒
		new Rod(TEAM_RED, 2, 350, 2), // 紅 中場
		new Rod(TEAM_BLUE, 1, 450, 2), // 藍 中場
		new Rod(TEAM_RED, 2, 550, 3), // 紅 前鋒
		new Rod(TEAM_BLUE, 3, 675, 2), // 藍 後衛
		new Rod(TEAM_BLUE, 3, 775, 1), // 藍 守門員
	];
	private readonly slots: (Sink | undefined)[] = new Array(SLOTS);
	private readonly lastSent: (string | undefined)[] = new Array(SLOTS);
	private readonly score = [0, 0];

	private state: GameState = "idle";
	private bx = 0;
	private by = 0;
	private vx = 0;
	private vy = 0;
	private goalPause = 0;
	private lastScorer = 0;
	private goalsToWin = 5;

	private timer: ReturnType<typeof setInterval> | undefined;
	private disposed = false;

	private constructor(readonly deviceId: string) {}

	// #region 給 action 呼叫的介面

	attach(column: number, sink: Sink): void {
		this.slots[column] = sink;
		// 其他格的提示文字（n/4）也要更新
		this.lastSent.fill(undefined);
		this.renderAndSend();
	}

	detach(column: number, sink: Sink): void {
		if (this.slots[column] !== sink) return;
		this.slots[column] = undefined;
		this.lastSent.fill(undefined);
		// 換頁時收不到 DialUp，不清掉的話桿子會一直抬著
		for (const rod of this.rods) if (rod.column === column) rod.held = false;
		this.renderAndSend();
	}

	setGoalsToWin(n: number): void {
		this.goalsToWin = Math.max(1, n);
		this.renderIfIdle();
	}

	/** 旋鈕轉動：這一格的桿子上下移動（順時針往下） */
	rotate(column: number, ticks: number, step: number): void {
		if (this.state === "gameOver") return;
		for (const rod of this.rods) {
			if (rod.column !== column) continue;
			rod.offset = Math.max(-rod.maxOffset, Math.min(rod.maxOffset, rod.offset + ticks * step));
		}
		this.renderIfIdle();
	}

	/** 旋鈕按下：比賽中是踢球（按住不放就維持抬腳），其他狀態是開始 / 繼續 */
	press(column: number): void {
		switch (this.state) {
			case "idle":
			case "gameOver":
				this.startMatch();
				break;
			case "paused":
				this.state = "playing";
				this.startTimer();
				break;
			case "playing":
				for (const rod of this.rods) {
					if (rod.column !== column) continue;
					rod.held = true;
					if (rod.kick > 0) continue;
					rod.kick = KICK_FRAMES;
					rod.kickHit = false;
				}
				break;
		}
		this.renderIfIdle();
	}

	/** 旋鈕放開：抬起的球員放下來 */
	dialReleased(column: number): void {
		for (const rod of this.rods) if (rod.column === column) rod.held = false;
		this.renderIfIdle();
	}

	/** 觸控條點一下：暫停 / 繼續 */
	togglePause(): void {
		if (this.state === "playing") {
			this.state = "paused";
			this.stopTimer();
		} else if (this.state === "paused") {
			this.state = "playing";
			this.startTimer();
		} else if (this.state === "idle" || this.state === "gameOver") {
			this.startMatch();
		}
		this.renderIfIdle();
	}

	/** 觸控條長按：回到開始畫面 */
	reset(): void {
		this.stopTimer();
		this.state = "idle";
		this.score[0] = this.score[1] = 0;
		this.resetRods();
		this.renderIfIdle();
	}

	/** 給放在 1~4 以外位置的 action 用的提示畫面 */
	static renderMessage(line1: string, line2: string): string {
		const g = new Svg(SLOT_W, H);
		g.clear(Colors.Black);
		g.drawStringIn(line1, { size: 14, bold: true }, Colors.White, { x: 0, y: 20, w: SLOT_W, h: 30 }, "center", "center");
		g.drawStringIn(line2, SmallFont, Colors.Gray, { x: 0, y: 50, w: SLOT_W, h: 30 }, "center", "center");
		return g.toString();
	}

	// #endregion

	// #region 比賽流程

	private resetRods(): void {
		for (const rod of this.rods) {
			rod.offset = 0;
			rod.kick = 0;
			rod.held = false;
		}
	}

	private startMatch(): void {
		this.score[0] = this.score[1] = 0;
		this.resetRods();
		this.serve();
		this.startTimer();
	}

	/** 從中線發球，方向隨機、慢慢滾向其中一隊的中場 */
	private serve(): void {
		this.bx = W / 2;
		this.by = FIELD_TOP + FIELD_H / 2;
		this.vx = randSign() * 1.8;
		this.vy = (Math.random() - 0.5) * 1.6;
		this.state = "playing";
	}

	private scoreGoal(team: number): void {
		this.score[team]++;
		this.lastScorer = team;
		this.state = "goal";
		this.goalPause = GOAL_PAUSE_FRAMES;
	}

	private startTimer(): void {
		this.stopTimer();
		if (this.disposed) return;
		this.timer = setInterval(() => this.onFrame(), FRAME_MS);
		this.onFrame();
	}

	private stopTimer(): void {
		if (this.timer !== undefined) clearInterval(this.timer);
		this.timer = undefined;
	}

	private onFrame(): void {
		if (this.state === "playing" || this.state === "goal") this.step();
		this.renderAndSend();
	}

	/** 一幀：踢球動畫、進球暫停、球的移動與碰撞 */
	private step(): void {
		for (const rod of this.rods) {
			if (rod.kick <= 0) continue;
			if (this.state === "playing" && !rod.kickHit && rod.kick > KICK_FRAMES - KICK_ACTIVE) this.tryKick(rod);
			rod.kick--;
		}

		if (this.state === "goal") {
			if (--this.goalPause <= 0) {
				if (this.score[this.lastScorer] >= this.goalsToWin) {
					this.state = "gameOver";
					this.stopTimer();
				} else {
					this.serve();
				}
			}
			return;
		}

		// 分幾小步走，快的球才不會直接穿過 6px 寬的球員
		const speed = Math.max(Math.abs(this.vx), Math.abs(this.vy));
		const n = Math.max(1, Math.ceil(speed / 2));
		for (let i = 0; i < n; i++) {
			const px = this.bx;
			this.bx += this.vx / n;
			this.by += this.vy / n;
			if (this.walls()) return;
			this.players(px);
		}

		this.vx *= FRICTION;
		this.vy *= FRICTION;
		if (Math.abs(this.vx) < MIN_VX) this.vx = (this.vx === 0 ? randSign() : Math.sign(this.vx)) * MIN_VX;
		this.vx = Math.max(-MAX_VX, Math.min(MAX_VX, this.vx));
		this.vy = Math.max(-MAX_VY, Math.min(MAX_VY, this.vy));
	}

	private tryKick(rod: Rod): void {
		// ahead > 0 是腳前，< 0 是背後；貼著身體的球算腳前
		const ahead = (this.bx - rod.x) * rod.dir;
		if (Math.abs(ahead) > KICK_REACH) return;
		const forward = ahead >= -(PW / 2 + BALL_R);

		for (let i = 0; i < rod.players; i++) {
			const d = this.by - rod.playerY(i);
			if (Math.abs(d) > PH / 2 + BALL_R + 1) continue;

			// 踢到球員上緣球往上偏、下緣往下偏，加一點亂數
			const spread = d / (PH / 2 + BALL_R);
			if (forward) {
				this.vx = rod.dir * KICK_SPEED;
				this.vy = spread * 3.2 + (Math.random() - 0.5) * 0.6;
				this.bx = rod.x + rod.dir * (PW / 2 + BALL_R + 1);
			} else {
				// 背後的球：桿子往後轉把球撥回去，力道比較弱
				this.vx = -rod.dir * BACK_KICK_SPEED;
				this.vy = spread * 2.0 + (Math.random() - 0.5) * 0.6;
				this.bx = rod.x - rod.dir * (PW / 2 + BALL_R + 1);
			}
			rod.kickHit = true;
			return;
		}
	}

	/** 回傳 true 代表進球 */
	private walls(): boolean {
		if (this.by - BALL_R < FIELD_TOP) {
			this.by = FIELD_TOP + BALL_R;
			this.vy = Math.abs(this.vy);
		} else if (this.by + BALL_R > H) {
			this.by = H - BALL_R;
			this.vy = -Math.abs(this.vy);
		}

		const inMouth = this.by >= GOAL_TOP && this.by <= GOAL_BOTTOM;

		if (this.bx - BALL_R < 0) {
			if (inMouth) {
				if (this.bx < 0) {
					this.scoreGoal(TEAM_BLUE);
					return true;
				}
			} else {
				this.bx = BALL_R;
				this.vx = Math.abs(this.vx) * 0.9;
				this.vy += (Math.random() - 0.5) * BOUNCE_JITTER;
			}
		} else if (this.bx + BALL_R > W) {
			if (inMouth) {
				if (this.bx > W) {
					this.scoreGoal(TEAM_RED);
					return true;
				}
			} else {
				this.bx = W - BALL_R;
				this.vx = -Math.abs(this.vx) * 0.9;
				this.vy += (Math.random() - 0.5) * BOUNCE_JITTER;
			}
		}
		return false;
	}

	/** 球員碰撞。正面背面都會擋，自己人也一樣；只有抬腳中的桿子讓球過去 */
	private players(px: number): void {
		for (const rod of this.rods) {
			if (rod.lifted) continue;

			const left = rod.x - PW / 2 - BALL_R;
			const right = rod.x + PW / 2 + BALL_R;
			if (this.bx < left || this.bx > right) continue;

			for (let i = 0; i < rod.players; i++) {
				const cy = rod.playerY(i);
				const top = cy - PH / 2 - BALL_R;
				const bottom = cy + PH / 2 + BALL_R;
				if (this.by < top || this.by > bottom) continue;

				if (px < left || px > right) {
					// 從左或右撞到：往來的方向彈回去
					const side = px < left ? -1 : 1;
					this.vx = side * Math.max(Math.abs(this.vx) * 0.7, MIN_VX);
					this.bx = side > 0 ? right + 0.1 : left - 0.1;
					this.vy += (Math.random() - 0.5) * BOUNCE_JITTER;
				} else {
					// 上下撞到，或是球員移過來壓到球
					const ny = this.by < cy ? top - 0.1 : bottom + 0.1;
					if (ny - BALL_R >= FIELD_TOP && ny + BALL_R <= H) {
						this.by = ny;
						this.vy = this.by < cy ? -Math.abs(this.vy) : Math.abs(this.vy);
					} else {
						// 球被壓在邊線上、上下沒空間：從比較近的那一側擠出去
						const side = this.bx >= rod.x ? 1 : -1;
						this.bx = side > 0 ? right + 0.1 : left - 0.1;
						this.vx = side * Math.max(Math.abs(this.vx), MIN_VX);
					}
				}
				return;
			}
		}
	}

	// #endregion

	// #region 繪圖

	private renderIfIdle(): void {
		// 計時器在跑的時候由它負責畫
		if (this.timer === undefined) this.renderAndSend();
	}

	renderAndSend(): void {
		if (this.disposed) return;
		const g = this.draw();
		for (let col = 0; col < SLOTS; col++) {
			const sink = this.slots[col];
			if (!sink) continue;
			const svg = g.toString({ x: col * SLOT_W, y: 0, w: SLOT_W, h: H });
			// 沒變的格子就不送，球只在一兩格裡跑，可以省掉大半流量
			if (svg === this.lastSent[col]) continue;
			this.lastSent[col] = svg;
			sink(svg);
		}
	}

	private attachedCount(): number {
		return this.slots.filter((s) => s).length;
	}

	private draw(): Svg {
		const g = new Svg(W, H);
		g.clear(Colors.Black);

		// 球場
		g.fillRect(0, FIELD_TOP, W, FIELD_H, FieldColor);
		const midY = FIELD_TOP + FIELD_H / 2;
		g.drawLine(W / 2, FIELD_TOP, W / 2, H, LineColor);
		g.drawEllipse(W / 2 - 14, midY - 14, 28, 28, LineColor);
		g.drawRect(0, GOAL_TOP - 8, 22, GOAL_H + 16, LineColor);
		g.drawRect(W - 23, GOAL_TOP - 8, 22, GOAL_H + 16, LineColor);
		g.fillRect(0, GOAL_TOP, 3, GOAL_H, Colors.White);
		g.fillRect(W - 3, GOAL_TOP, 3, GOAL_H, Colors.White);

		// 桿子與球員
		for (const rod of this.rods) {
			g.drawLine(rod.x, FIELD_TOP, rod.x, H, RodColor);
			const body = rod.team === TEAM_RED ? RedColor : BlueColor;
			const foot = rod.team === TEAM_RED ? RedFoot : BlueFoot;

			for (let i = 0; i < rod.players; i++) {
				const cy = Math.round(rod.playerY(i));
				if (rod.lifted) {
					// 抬起來：從上面看球員躺平、腳朝前，顏色淡一點表示離地
					const len = 14;
					const lx = rod.dir > 0 ? rod.x - PW / 2 : rod.x + PW / 2 - len;
					g.fillRect(lx, cy - 3, len, 6, foot);
					const hx = rod.dir > 0 ? rod.x - PW / 2 : rod.x + PW / 2 - 4;
					g.fillRect(hx, cy - 2, 4, 4, HeadColor);
					continue;
				}
				if (rod.kick > 0) {
					const reach = KickReach[KICK_FRAMES - rod.kick];
					const fx = rod.dir > 0 ? rod.x + PW / 2 : rod.x - PW / 2 - reach;
					g.fillRect(fx, cy - 2, reach, 4, foot);
				}
				g.fillRect(rod.x - PW / 2, cy - PH / 2, PW, PH, body);
				g.fillRect(rod.x - 2, cy - 2, 4, 4, HeadColor);
			}
		}

		// 球
		if (this.state === "playing" || this.state === "paused") {
			g.fillEllipse(this.bx - BALL_R, this.by - BALL_R, BALL_R * 2, BALL_R * 2, Colors.White, true);
		}

		// 記分列
		g.drawString(`RED ${this.score[TEAM_RED]}`, HudFont, RedColor, 2, 0);
		g.drawStringIn(`${this.score[TEAM_BLUE]} BLUE`, HudFont, BlueColor, { x: 0, y: 0, w: W - 2, h: FIELD_TOP }, "far");
		g.drawStringIn(`FIRST TO ${this.goalsToWin}`, HudFont, Colors.Gray, { x: 0, y: 0, w: W, h: FIELD_TOP }, "center");
		// 標示每格下面那顆旋鈕是哪一隊的（第 1、4 格旁邊已經有比分，不用標）
		for (let col = 1; col < SLOTS - 1; col++) {
			g.fillRect(col * SLOT_W + SLOT_W / 2 - 15, 3, 30, 2, teamOfColumn(col) === TEAM_RED ? RedColor : BlueColor);
		}

		// 中間的訊息框橫跨第 2、3 格
		const box = { x: W / 2 - 170, y: 22, w: 340, h: 66 };
		const line1 = { x: box.x, y: box.y + 2, w: box.w, h: 26 };
		const line2 = { x: box.x, y: box.y + 28, w: box.w, h: 16 };
		const line3 = { x: box.x, y: box.y + 46, w: box.w, h: 16 };
		const winner: Color = this.lastScorer === TEAM_RED ? RedColor : BlueColor;
		switch (this.state) {
			case "idle": {
				g.fillRect(box.x, box.y, box.w, box.h, Dim);
				g.drawStringIn("FOOSBALL", BigFont, Colors.White, line1, "center", "center");
				const n = this.attachedCount();
				const msg = n < SLOTS ? `put Foosball on all 4 dials (${n}/4)` : "press any dial to start";
				g.drawStringIn(msg, SmallFont, n < SLOTS ? Colors.Orange : Colors.LightGray, line2, "center", "center");
				g.drawStringIn("RED: dial 1 + 3     BLUE: dial 2 + 4", SmallFont, Colors.Gray, line3, "center", "center");
				break;
			}
			case "paused":
				g.fillRect(box.x, box.y, box.w, box.h, Dim);
				g.drawStringIn("PAUSED", BigFont, Colors.White, line1, "center", "center");
				g.drawStringIn("tap the strip to resume", SmallFont, Colors.Gray, line2, "center", "center");
				break;
			case "goal":
				g.fillRect(box.x, box.y, box.w, box.h, Dim);
				g.drawStringIn("GOAL!", BigFont, winner, line1, "center", "center");
				g.drawStringIn(`${this.score[TEAM_RED]} : ${this.score[TEAM_BLUE]}`, SmallFont, Colors.White, line2, "center", "center");
				break;
			case "gameOver":
				g.fillRect(box.x, box.y, box.w, box.h, Dim);
				g.drawStringIn(this.lastScorer === TEAM_RED ? "RED WINS" : "BLUE WINS", BigFont, winner, line1, "center", "center");
				g.drawStringIn(`${this.score[TEAM_RED]} : ${this.score[TEAM_BLUE]}`, SmallFont, Colors.White, line2, "center", "center");
				g.drawStringIn("press any dial for a rematch", SmallFont, Colors.Gray, line3, "center", "center");
				break;
		}
		return g;
	}

	// #endregion
}
