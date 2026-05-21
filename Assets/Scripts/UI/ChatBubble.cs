// =====================================================================
// ChatBubble.cs
// 掛在訊息氣泡 Prefab 上，提供設定文字內容與打字機效果。
// =====================================================================
using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace AIAgentChat
{
    /// <summary>
    /// 單一聊天氣泡：負責顯示一段文字。
    /// AI 訊息可使用 <see cref="TypewriterEffect"/> 達到逐字顯示。
    /// </summary>
    [DisallowMultipleComponent]
    public class ChatBubble : MonoBehaviour
    {
        [Tooltip("氣泡內顯示文字用的 TextMeshProUGUI 元件")]
        [SerializeField] private TextMeshProUGUI messageText;

        /// <summary>立即設定訊息文字（不帶打字機效果）。</summary>
        /// <param name="text">要顯示的完整文字</param>
        public void SetMessage(string text)
        {
            if (messageText != null)
            {
                messageText.text = text ?? string.Empty;
            }
        }

        /// <summary>
        /// 以打字機效果顯示文字，每個字之間延遲 <paramref name="delay"/> 秒。
        /// 使用方式：StartCoroutine(bubble.TypewriterEffect(...))
        /// </summary>
        /// <param name="text">完整文字</param>
        /// <param name="delay">每個字之間的延遲</param>
        /// <param name="onComplete">打字完成時呼叫的 callback（可為 null）</param>
        public IEnumerator TypewriterEffect(string text, float delay = 0.03f, Action onComplete = null)
        {
            if (messageText == null)
            {
                onComplete?.Invoke();
                yield break;
            }
            if (string.IsNullOrEmpty(text))
            {
                messageText.text = string.Empty;
                onComplete?.Invoke();
                yield break;
            }

            messageText.text = string.Empty;
            // 一個字一個字加上去；遇到負值或 0 的 delay 則直接全部顯示
            if (delay <= 0f)
            {
                messageText.text = text;
            }
            else
            {
                var buffer = new System.Text.StringBuilder(text.Length);
                foreach (char c in text)
                {
                    buffer.Append(c);
                    messageText.text = buffer.ToString();
                    yield return new WaitForSeconds(delay);
                }
            }

            onComplete?.Invoke();
        }

        /// <summary>取得內部 TMP 文字元件（供外部需要時調整字型樣式等）。</summary>
        public TextMeshProUGUI MessageText => messageText;
    }
}
