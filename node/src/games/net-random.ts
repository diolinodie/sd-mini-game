/**
 * 跟 .NET 的 new System.Random(seed) 一模一樣的亂數（Knuth subtractive generator），
 * 同一個種子會產生跟 C# 版完全相同的賽道、景物和路上的車。
 * 不需要重現時用 Math.random() 就好。
 */
const MBIG = 0x7fffffff;
const MSEED = 161803398;

export class NetRandom {
	private readonly seedArray = new Array<number>(56).fill(0);
	private inext = 0;
	private inextp = 21;

	constructor(seed: number) {
		const subtraction = seed === -0x80000000 ? MBIG : Math.abs(seed);
		let mj = MSEED - subtraction;
		this.seedArray[55] = mj;
		let mk = 1;
		for (let i = 1; i < 55; i++) {
			const ii = (21 * i) % 55;
			this.seedArray[ii] = mk;
			mk = mj - mk;
			if (mk < 0) mk += MBIG;
			mj = this.seedArray[ii];
		}
		for (let k = 1; k < 5; k++) {
			for (let i = 1; i < 56; i++) {
				this.seedArray[i] -= this.seedArray[1 + ((i + 30) % 55)];
				if (this.seedArray[i] < 0) this.seedArray[i] += MBIG;
			}
		}
	}

	private internalSample(): number {
		if (++this.inext >= 56) this.inext = 1;
		if (++this.inextp >= 56) this.inextp = 1;
		let ret = this.seedArray[this.inext] - this.seedArray[this.inextp];
		if (ret === MBIG) ret--;
		if (ret < 0) ret += MBIG;
		this.seedArray[this.inext] = ret;
		return ret;
	}

	/** 對應 NextDouble()，0 <= x < 1 */
	nextDouble(): number {
		return this.internalSample() * (1.0 / MBIG);
	}

	/** 對應 Next(max)：0 <= x < max；Next(min, max)：min <= x < max（範圍都不超過 int） */
	next(a: number, b?: number): number {
		if (b === undefined) return Math.trunc(this.nextDouble() * a);
		return Math.trunc(this.nextDouble() * (b - a)) + a;
	}
}
