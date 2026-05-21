// =====================================================================
// LLMResponse.cs
// 統一的 LLM 回應資料結構（情緒 + 文字）
// 安裝依賴：請確認 Packages/manifest.json 含有
//   "com.unity.nuget.newtonsoft-json": "3.2.1"
// =====================================================================
using System;
using System.Collections.Generic;

namespace AIAgentChat
{
    /// <summary>
    /// 表示一次 LLM 回應的結構化資料。包含 AI 文字內容與情緒標籤。
    /// </summary>
    [Serializable]
    public class LLMResponse
    {
        /// <summary>AI 回覆給玩家的文字內容（已從 JSON 中解析出來）。</summary>
        public string text;

        /// <summary>
        /// 從 AI 回覆中解析出的情緒標籤，例如：
        /// happy / sad / thinking / greeting / neutral / surprised / angry。
        /// 若解析失敗，預設為 "neutral"。
        /// </summary>
        public string emotion;

        /// <summary>是否在發送 / 解析過程中發生錯誤（用於 UI 顯示錯誤訊息）。</summary>
        public bool isError;

        /// <summary>當 isError 為 true 時，提供給玩家看的錯誤描述。</summary>
        public string errorMessage;

        /// <summary>建立一個成功回應。</summary>
        public static LLMResponse Success(string text, string emotion)
        {
            return new LLMResponse
            {
                text = text,
                emotion = string.IsNullOrEmpty(emotion) ? "neutral" : emotion,
                isError = false,
                errorMessage = null
            };
        }

        /// <summary>建立一個錯誤回應。</summary>
        public static LLMResponse Error(string message)
        {
            return new LLMResponse
            {
                text = string.Empty,
                emotion = "neutral",
                isError = true,
                errorMessage = message
            };
        }
    }

    /// <summary>
    /// 對話歷史單筆訊息結構，符合 OpenAI / Gemini 共用的 role + content 模型。
    /// role 可為 "system" / "user" / "assistant"。
    /// </summary>
    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;

        public ChatMessage() { }
        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }
}
