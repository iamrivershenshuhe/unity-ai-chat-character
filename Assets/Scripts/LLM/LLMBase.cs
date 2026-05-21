// =====================================================================
// LLMBase.cs
// 所有 LLM 服務的抽象基底類別，負責管理對話歷史、System Prompt、
// 以及對外的統一介面 SendMessage。
// 安裝依賴：請確認 Packages/manifest.json 含有
//   "com.unity.nuget.newtonsoft-json": "3.2.1"
// =====================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AIAgentChat
{
    /// <summary>
    /// LLM 服務的抽象基底類別。
    /// 子類別（ChatGPTService、ChatGeminiService）僅需實作
    /// <see cref="RequestCoroutine"/>，其餘對話歷史管理由本類別處理。
    /// </summary>
    public abstract class LLMBase : MonoBehaviour
    {
        [Header("API 設定")]
        [Tooltip("API 端點 URL（子類別會給預設值，可以在 Inspector 中覆蓋）")]
        [SerializeField] protected string apiUrl;

        [Tooltip("在此填入你的 API Key")]
        [SerializeField] protected string apiKey;

        [Header("角色設定")]
        [Tooltip("System Prompt — 定義角色人設，並要求 LLM 以 JSON 格式回覆")]
        [TextArea(5, 15)]
        [SerializeField]
        protected string systemPrompt =
            "你是一個友善的 AI 助教角色。你的每一次回覆都必須嚴格使用以下 JSON 格式，不要輸出任何其他內容：\n" +
            "{\"emotion\": \"<從 happy/sad/thinking/greeting/neutral/surprised/angry 中選一個最符合的>\", \"message\": \"<你的回覆內容>\"}\n" +
            "回答語言：繁體中文";

        [Header("對話歷史")]
        [Tooltip("保留對話訊息數量上限（不含 system prompt），達到上限時丟棄最舊的 user/assistant 訊息")]
        [SerializeField] protected int maxHistoryCount = 20;

        [Tooltip("HTTP request timeout（秒）")]
        [SerializeField] protected int requestTimeoutSeconds = 30;

        /// <summary>對話歷史（不包含 system prompt，systemPrompt 在子類別組裝請求時額外加入）。</summary>
        protected readonly List<ChatMessage> chatHistory = new List<ChatMessage>();

        /// <summary>對外暴露唯讀的對話歷史。</summary>
        public IReadOnlyList<ChatMessage> ChatHistory => chatHistory;

        /// <summary>System Prompt 內容（唯讀）。</summary>
        public string SystemPrompt => systemPrompt;

        /// <summary>
        /// 玩家送出一則訊息給 LLM。
        /// 此方法會啟動 Coroutine 呼叫子類別實作的 <see cref="RequestCoroutine"/>。
        /// </summary>
        /// <param name="userMessage">玩家輸入的文字</param>
        /// <param name="onComplete">完成時的 callback，會收到 LLMResponse（可能是錯誤回應）</param>
        public virtual void SendMessage(string userMessage, Action<LLMResponse> onComplete)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                onComplete?.Invoke(LLMResponse.Error("訊息為空"));
                return;
            }

            // 將玩家訊息加入歷史，之後子類別會把整段歷史送給 API
            chatHistory.Add(new ChatMessage("user", userMessage));
            TrimHistory();

            StartCoroutine(RequestCoroutine(userMessage, response =>
            {
                // 若成功則把 AI 回覆也記錄到對話歷史中
                if (response != null && !response.isError)
                {
                    chatHistory.Add(new ChatMessage("assistant", response.text));
                    TrimHistory();
                }
                onComplete?.Invoke(response);
            }));
        }

        /// <summary>
        /// 由子類別實作：實際對 LLM 發送 HTTP 請求並解析回應。
        /// </summary>
        /// <param name="message">最新一則玩家訊息（已經加入 chatHistory，可直接讀 chatHistory 組裝請求）</param>
        /// <param name="onComplete">解析完成後傳回 LLMResponse</param>
        protected abstract IEnumerator RequestCoroutine(string message, Action<LLMResponse> onComplete);

        /// <summary>清空對話歷史（不影響 systemPrompt）。</summary>
        public virtual void ClearHistory()
        {
            chatHistory.Clear();
        }

        /// <summary>修剪對話歷史，僅保留最近 maxHistoryCount 筆訊息。</summary>
        protected void TrimHistory()
        {
            int over = chatHistory.Count - maxHistoryCount;
            if (over > 0)
            {
                chatHistory.RemoveRange(0, over);
            }
        }

        /// <summary>
        /// 嘗試從 LLM 回傳的原始字串中解析出 {emotion, message} JSON。
        /// 若解析失敗，emotion 預設 neutral、message 直接使用原始字串。
        /// </summary>
        /// <param name="rawText">LLM 回傳的原始文字</param>
        protected LLMResponse ParseJsonResponse(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return LLMResponse.Success(string.Empty, "neutral");
            }

            // 嘗試擷取第一個 '{' 到最後一個 '}' 之間的內容，避免模型在 JSON 前後夾雜其他文字
            string jsonCandidate = rawText.Trim();
            int firstBrace = jsonCandidate.IndexOf('{');
            int lastBrace = jsonCandidate.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                jsonCandidate = jsonCandidate.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            try
            {
                // 使用 Newtonsoft.Json 解析（比 JsonUtility 容錯性更好）
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<EmotionMessagePayload>(jsonCandidate);
                if (parsed != null && !string.IsNullOrEmpty(parsed.message))
                {
                    string emotion = string.IsNullOrEmpty(parsed.emotion) ? "neutral" : parsed.emotion.ToLowerInvariant();
                    return LLMResponse.Success(parsed.message, emotion);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LLMBase] JSON 解析失敗，回傳原始文字: {ex.Message}");
            }

            // 解析失敗 → 回傳 neutral + 原始文字
            return LLMResponse.Success(rawText, "neutral");
        }

        /// <summary>內部 DTO：對應 system prompt 中要求 LLM 輸出的 JSON 結構。</summary>
        [Serializable]
        private class EmotionMessagePayload
        {
            public string emotion;
            public string message;
        }
    }
}
