/**
 * 外掛 UUID。開發期間用 .dev，才能跟已安裝的 C# 版並排比較；
 * 正式發布時換回 com.odiestudio.minigame（manifest.json 跟資料夾名稱也要一起換），
 * 這樣 action UUID 跟 C# 版一致，老用戶的設定和最高分才會保留。
 */
export const PLUGIN_UUID = "com.odiestudio.minigame.dev";
