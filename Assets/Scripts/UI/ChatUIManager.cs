// =====================================================================
// ChatUIManager.cs
// 聊天視窗 UI 管理：負責輸入、顯示玩家 / AI 訊息氣泡、滾動、思考中提示。
// =====================================================================
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIAgentChat
{
    /// <summary>
    /// 聊天視窗 UI 的核心 Manager（不使用 IMGUI / OnGUI）。
    /// 對外暴露 <see cref="OnUserMessageSubmitted"/> 事件給核心控制器訂閱。
    /// </summary>
    [DisallowMultipleComponent]
    public class ChatUIManager : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("玩家輸入文字的 TMP_InputField")]
        [SerializeField] private TMP_InputField inputField;

        [Tooltip("送出訊息的按鈕")]
        [SerializeField] private Button sendButton;

        [Tooltip("聊天歷史的 ScrollRect — 用於自動捲到最新訊息")]
        [SerializeField] private ScrollRect chatScrollRect;

        [Tooltip("訊息氣泡的父容器（通常是 ScrollRect 的 content）")]
        [SerializeField] private Transform messageContainer;

        [Header("Prefabs")]
        [Tooltip("玩家訊息氣泡 Prefab（含 ChatBubble 元件）")]
        [SerializeField] private GameObject userMessagePrefab;

        [Tooltip("AI 訊息氣泡 Prefab（含 ChatBubble 元件）")]
        [SerializeField] private GameObject aiMessagePrefab;

        [Header("Indicators")]
        [Tooltip("「AI 思考中…」的提示文字（可為 null）")]
        [SerializeField] private TextMeshProUGUI typingIndicator;

        [Tooltip("送出後是否清空輸入框")]
        [SerializeField] private bool clearInputOnSend = true;

        [Header("Typewriter")]
        [Tooltip("AI 訊息打字機效果每個字的延遲（秒），設為 0 則即時顯示")]
        [SerializeField] private float typewriterDelay = 0.03f;

        /// <summary>玩家送出一則訊息時觸發的事件，參數為玩家輸入的字串。</summary>
        public event Action<string> OnUserMessageSubmitted;

        private void Awake()
        {
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(HandleSendButtonClicked);
            }
            if (inputField != null)
            {
                // 按 Enter 送出
                inputField.onSubmit.AddListener(HandleInputFieldSubmit);
            }
            SetTypingIndicatorVisible(false);
        }

        private void OnDestroy()
        {
            if (sendButton != null)
            {
                sendButton.onClick.RemoveListener(HandleSendButtonClicked);
            }
            if (inputField != null)
            {
                inputField.onSubmit.RemoveListener(HandleInputFieldSubmit);
            }
        }

        private void HandleSendButtonClicked()
        {
            SubmitCurrentInput();
        }

        private void HandleInputFieldSubmit(string value)
        {
            // TMP_InputField 在 SingleLine 模式下，按 Enter 會觸發 onSubmit
            SubmitCurrentInput();
        }

        /// <summary>從輸入框讀出文字並對外送出事件。</summary>
        private void SubmitCurrentInput()
        {
            if (inputField == null) return;
            string text = inputField.text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // 顯示玩家氣泡
            AppendUserMessage(text);

            // 清空輸入框並重新 focus
            if (clearInputOnSend)
            {
                inputField.text = string.Empty;
                inputField.ActivateInputField();
            }

            OnUserMessageSubmitted?.Invoke(text);
        }

        /// <summary>在聊天視窗中新增一則玩家訊息氣泡。</summary>
        public ChatBubble AppendUserMessage(string text)
        {
            return AppendBubble(userMessagePrefab, text, useTypewriter: false, null);
        }

        /// <summary>
        /// 在聊天視窗中新增一則 AI 訊息氣泡，並以打字機效果顯示。
        /// </summary>
        /// <param name="text">完整文字</param>
        /// <param name="onComplete">打字完成時呼叫</param>
        public ChatBubble AppendAIMessage(string text, Action onComplete = null)
        {
            return AppendBubble(aiMessagePrefab, text, useTypewriter: true, onComplete);
        }

        /// <summary>顯示錯誤訊息（外觀沿用 AI 氣泡，但會在前方加上「⚠」標示）。</summary>
        public void AppendErrorMessage(string error)
        {
            AppendBubble(aiMessagePrefab, $"⚠ 錯誤：{error}", useTypewriter: false, null);
        }

        /// <summary>切換「思考中…」提示文字的顯示狀態。</summary>
        public void SetTypingIndicatorVisible(bool visible)
        {
            if (typingIndicator != null)
            {
                typingIndicator.gameObject.SetActive(visible);
            }
        }

        /// <summary>內部統一的氣泡產生流程。</summary>
        private ChatBubble AppendBubble(GameObject prefab, string text, bool useTypewriter, Action onComplete)
        {
            if (prefab == null || messageContainer == null)
            {
                Debug.LogWarning("[ChatUIManager] 氣泡 Prefab 或 messageContainer 未設定");
                onComplete?.Invoke();
                return null;
            }

            GameObject go = Instantiate(prefab, messageContainer);
            var bubble = go.GetComponent<ChatBubble>();
            if (bubble == null)
            {
                Debug.LogError("[ChatUIManager] Prefab 缺少 ChatBubble 元件");
                onComplete?.Invoke();
                return null;
            }

            if (useTypewriter && typewriterDelay > 0f)
            {
                bubble.SetMessage(string.Empty);
                StartCoroutine(TypeAndScroll(bubble, text, onComplete));
            }
            else
            {
                bubble.SetMessage(text);
                ScheduleScrollToBottom();
                onComplete?.Invoke();
            }
            return bubble;
        }

        private IEnumerator TypeAndScroll(ChatBubble bubble, string text, Action onComplete)
        {
            // 因為 Layout 在打字過程中持續更新，每幾個字 scroll 一次即可
            yield return bubble.TypewriterEffect(text, typewriterDelay, () =>
            {
                ScheduleScrollToBottom();
                onComplete?.Invoke();
            });
        }

        /// <summary>等待一個 frame 讓 Layout 重新計算後再捲到底。</summary>
        private void ScheduleScrollToBottom()
        {
            StartCoroutine(ScrollToBottomNextFrame());
        }

        private IEnumerator ScrollToBottomNextFrame()
        {
            yield return null; // 等 Layout 更新
            if (chatScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                chatScrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
}
