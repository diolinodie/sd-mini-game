import streamDeck, { type FeedbackPayload } from "@elgato/streamdeck";

const logger = streamDeck.logger.createScope("feedback");

type Feedbackable = { setFeedback(feedback: FeedbackPayload): Promise<void> };

/**
 * 把遊戲畫好的 SVG 送到 layout 的 canvas（pixmap），對應 C# 的
 * Connection.SetFeedbackAsync(new Dictionary { ["canvas"] = Tools.ImageToBase64(bmp) })。
 */
export function canvasFeedback(dial: Feedbackable): (svg: string) => void {
	return (svg) => {
		const value = "data:image/svg+xml;base64," + Buffer.from(svg).toString("base64");
		dial.setFeedback({ canvas: value }).catch((e) => logger.error(`setFeedback failed: ${e}`));
	};
}
