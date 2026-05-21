// =====================================================================
// ChatGPTService.cs
// 串接 OpenAI Chat Completions API（預設模型 gpt-4o-mini）。
// 安裝依賴：請確認 Packages/manifest.json 含有
//   "com.unity.nuget.newtonsoft-json": "3.2.1"
// =====================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace AIAgentChat
{
    /// <summary>
    /// OpenAI Chat Completions API 的實作。
    /// 預設模型 gpt-4o-mini，可在 Inspector 修改。
    /// </summary>
    public class ChatGPTService : LLMBase
    {
        [Header("ChatGPT 設定")]
        [Tooltip("OpenAI Chat Completions API 端點")]
        [SerializeField] private string defaultApiUrl = "https://api.openai.com/v1/chat/completions";

        [Tooltip("使用的模型名稱")]
        [SerializeField] private string model = "gpt-4o-mini";

        [Tooltip("Sampling temperature（0~2）— 越高越有創意，越低越穩定")]
        [Range(0f, 2f)]
        [SerializeField] private float temperature = 0.7f;

        private void Reset()
        {
            // Inspector 上預設值
            apiUrl = "https://api.openai.com/v1/chat/completions";
        }

        protected override IEnumerator RequestCoroutine(string message, Action<LLMResponse> onComplete)
        {
            // 若 Inspector 沒填 apiUrl，使用預設值
            string url = string.IsNullOrEmpty(apiUrl) ? defaultApiUrl : apiUrl;

            if (string.IsNullOrEmpty(apiKey))
            {
                onComplete?.Invoke(LLMResponse.Error("尚未設定 OpenAI API Key，請在 ChatGPTService Inspector 中填入"));
                yield break;
            }

            // 組合送出的 messages：system + 完整對話歷史（含本次玩家訊息）
            var messages = new List<ChatMessage>(chatHistory.Count + 1);
            messages.Add(new ChatMessage("system", systemPrompt));
            messages.AddRange(chatHistory);

            var requestBody = new ChatGPTRequest
            {
                model = model,
                temperature = temperature,
                messages = messages
            };

            string json;
            try
            {
                json = JsonConvert.SerializeObject(requestBody);
            }
            catch (Exception ex)
            {
                onComplete?.Invoke(LLMResponse.Error($"請求序列化失敗：{ex.Message}"));
                yield break;
            }

            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.timeout = requestTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string err = $"OpenAI 請求失敗：{request.error}\n{request.downloadHandler?.text}";
                    Debug.LogError($"[ChatGPTService] {err}");
                    onComplete?.Invoke(LLMResponse.Error(err));
                    yield break;
                }

                string responseText = request.downloadHandler.text;
                LLMResponse result = null;
                try
                {
                    var parsed = JsonConvert.DeserializeObject<ChatGPTResponse>(responseText);
                    if (parsed?.choices == null || parsed.choices.Count == 0)
                    {
                        result = LLMResponse.Error("OpenAI 回應沒有 choices 內容");
                    }
                    else
                    {
                        string aiText = parsed.choices[0].message?.content ?? string.Empty;
                        result = ParseJsonResponse(aiText);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ChatGPTService] 回應解析錯誤：{ex.Message}\n原始：{responseText}");
                    result = LLMResponse.Error($"OpenAI 回應解析錯誤：{ex.Message}");
                }

                onComplete?.Invoke(result);
            }
        }

        // ----- 內部請求 / 回應 DTO -----

        [Serializable]
        private class ChatGPTRequest
        {
            public string model;
            public float temperature;
            public List<ChatMessage> messages;
        }

        [Serializable]
        private class ChatGPTResponse
        {
            public List<Choice> choices;

            [Serializable]
            public class Choice
            {
                public ChatMessage message;
            }
        }
    }
}
