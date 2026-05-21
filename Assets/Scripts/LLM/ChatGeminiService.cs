// =====================================================================
// ChatGeminiService.cs
// 串接 Google Gemini API（預設模型 gemini-2.0-flash）。
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
    /// Google Gemini generateContent API 的實作。
    /// 預設模型 gemini-2.0-flash，可在 Inspector 修改。
    /// </summary>
    public class ChatGeminiService : LLMBase
    {
        [Header("Gemini 設定")]
        [Tooltip("Gemini API 端點模板 — {model} 會替換成下方的模型名稱")]
        [SerializeField] private string apiUrlTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        [Tooltip("使用的模型名稱")]
        [SerializeField] private string model = "gemini-2.0-flash";

        [Tooltip("Sampling temperature（0~2）")]
        [Range(0f, 2f)]
        [SerializeField] private float temperature = 0.7f;

        protected override IEnumerator RequestCoroutine(string message, Action<LLMResponse> onComplete)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                onComplete?.Invoke(LLMResponse.Error("尚未設定 Gemini API Key，請在 ChatGeminiService Inspector 中填入"));
                yield break;
            }

            // 組合 URL（key 透過 query string 傳遞）
            string baseUrl = string.IsNullOrEmpty(apiUrl) ? apiUrlTemplate.Replace("{model}", model) : apiUrl;
            string url = $"{baseUrl}?key={UnityWebRequest.EscapeURL(apiKey)}";

            // 把 chatHistory 轉成 Gemini 的 contents 結構（role 為 user / model）
            var contents = new List<GeminiContent>();
            foreach (var msg in chatHistory)
            {
                string role = msg.role == "assistant" ? "model" : "user";
                contents.Add(new GeminiContent
                {
                    role = role,
                    parts = new List<GeminiPart> { new GeminiPart { text = msg.content } }
                });
            }

            var requestBody = new GeminiRequest
            {
                // Gemini 用 systemInstruction 欄位傳遞 system prompt
                systemInstruction = new GeminiContent
                {
                    role = "user",
                    parts = new List<GeminiPart> { new GeminiPart { text = systemPrompt } }
                },
                contents = contents,
                generationConfig = new GeminiGenerationConfig { temperature = temperature }
            };

            string json;
            try
            {
                // 序列化時忽略 null 欄位（systemInstruction 在 v1 / v1beta 才支援）
                json = JsonConvert.SerializeObject(requestBody, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
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
                request.timeout = requestTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string err = $"Gemini 請求失敗：{request.error}\n{request.downloadHandler?.text}";
                    Debug.LogError($"[ChatGeminiService] {err}");
                    onComplete?.Invoke(LLMResponse.Error(err));
                    yield break;
                }

                string responseText = request.downloadHandler.text;
                LLMResponse result = null;
                try
                {
                    var parsed = JsonConvert.DeserializeObject<GeminiResponse>(responseText);
                    string aiText = ExtractText(parsed);
                    if (string.IsNullOrEmpty(aiText))
                    {
                        result = LLMResponse.Error("Gemini 回應沒有 candidate 內容");
                    }
                    else
                    {
                        result = ParseJsonResponse(aiText);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ChatGeminiService] 回應解析錯誤：{ex.Message}\n原始：{responseText}");
                    result = LLMResponse.Error($"Gemini 回應解析錯誤：{ex.Message}");
                }

                onComplete?.Invoke(result);
            }
        }

        /// <summary>從 Gemini 的 candidates[0].content.parts 中取出文字內容。</summary>
        private static string ExtractText(GeminiResponse parsed)
        {
            if (parsed?.candidates == null || parsed.candidates.Count == 0)
                return null;
            var content = parsed.candidates[0].content;
            if (content?.parts == null || content.parts.Count == 0)
                return null;

            var sb = new StringBuilder();
            foreach (var part in content.parts)
            {
                if (!string.IsNullOrEmpty(part.text))
                    sb.Append(part.text);
            }
            return sb.ToString();
        }

        // ----- 內部請求 / 回應 DTO -----

        [Serializable]
        private class GeminiRequest
        {
            public GeminiContent systemInstruction;
            public List<GeminiContent> contents;
            public GeminiGenerationConfig generationConfig;
        }

        [Serializable]
        private class GeminiContent
        {
            public string role;
            public List<GeminiPart> parts;
        }

        [Serializable]
        private class GeminiPart
        {
            public string text;
        }

        [Serializable]
        private class GeminiGenerationConfig
        {
            public float temperature;
        }

        [Serializable]
        private class GeminiResponse
        {
            public List<GeminiCandidate> candidates;

            [Serializable]
            public class GeminiCandidate
            {
                public GeminiContent content;
            }
        }
    }
}
