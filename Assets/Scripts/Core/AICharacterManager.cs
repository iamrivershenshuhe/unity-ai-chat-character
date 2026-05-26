// =====================================================================
// AICharacterManager.cs
// 核心控制器：串接 ChatUIManager（玩家輸入）、LLMBase（AI 回覆）、
// CharacterAnimatorController（動畫播放）三個系統。
// =====================================================================
using UnityEngine;

namespace AIAgentChat
{
    /// <summary>
    /// AI 角色對話的總控制器。
    /// 收到玩家輸入後：
    /// 1. 通知角色進入「說話中」動畫；
    /// 2. 呼叫 LLM；
    /// 3. 解析 emotion + message；
    /// 4. 播放對應情緒動畫並顯示 AI 回覆（含打字機效果）；
    /// 5. 打字機結束後切回 Idle。
    /// </summary>
    [DisallowMultipleComponent]
    public class AICharacterManager : MonoBehaviour
    {
        [Header("Services")]
        [Tooltip("實際使用的 LLM 服務（拖入 ChatGPTService 或 ChatGeminiService 元件）")]
        [SerializeField] private LLMBase llmService;

        [Tooltip("角色動畫控制器")]
        [SerializeField] private CharacterAnimatorController animController;

        [Tooltip("聊天視窗 UI")]
        [SerializeField] private ChatUIManager chatUI;

        [Header("Behaviour")]
        [Tooltip("在輸入欄送出後是否暫時 disable 輸入框，避免重複送出")]
        [SerializeField] private bool blockInputWhileWaiting = true;

        private bool isWaitingForResponse;

        private void OnEnable()
        {
            if (chatUI != null)
            {
                chatUI.OnUserMessageSubmitted += HandleUserMessageSubmitted;
            }
        }

        private void OnDisable()
        {
            if (chatUI != null)
            {
                chatUI.OnUserMessageSubmitted -= HandleUserMessageSubmitted;
            }
        }

        private void Start()
        {
            // 進場先回到 Idle
            if (animController != null)
            {
                animController.PlayIdle();
            }
        }

        /// <summary>當玩家從 ChatUIManager 送出訊息時被呼叫。</summary>
        private void HandleUserMessageSubmitted(string userMessage)
        {
            if (isWaitingForResponse)
            {
                Debug.Log("[AICharacterManager] 上一則回覆尚未完成，忽略新輸入");
                return;
            }
            if (llmService == null)
            {
                chatUI?.AppendErrorMessage("尚未指定 LLM 服務");
                return;
            }

            isWaitingForResponse = true;

            // 1) 角色進入說話 / 思考動畫；2) UI 顯示「思考中…」提示
            animController?.PlayTalking();
            chatUI?.SetTypingIndicatorVisible(true);

            llmService.SendMessage(userMessage, OnLLMResponse);
        }

        /// <summary>LLM 回應完成後的處理。</summary>
        private void OnLLMResponse(LLMResponse response)
        {
            chatUI?.SetTypingIndicatorVisible(false);

            if (response == null || response.isError)
            {
                // 出錯：顯示錯誤、角色回到 Idle
                string errMsg = response?.errorMessage ?? "未知錯誤";
                chatUI?.AppendErrorMessage(errMsg);
                animController?.PlayIdle();
                isWaitingForResponse = false;
                return;
            }

            // 播放對應情緒動畫
            animController?.PlayEmotion(response.emotion);

            // 顯示 AI 訊息（帶打字機效果），打字結束後回到 Idle
            chatUI?.AppendAIMessage(response.text, onComplete: () =>
            {
                animController?.PlayIdle();
                isWaitingForResponse = false;
            });

            // 若 UI 找不到（例如未指定氣泡 prefab），上面 callback 不會觸發
            // 此處做個防呆：若 chatUI 為 null 也要釋放鎖
            if (chatUI == null)
            {
                animController?.PlayIdle();
                isWaitingForResponse = false;
            }
        }

        /// <summary>對外公開的設定 API（讓 Editor 腳本可在建立場景時注入 reference）。</summary>
        public void Configure(LLMBase llm, CharacterAnimatorController anim, ChatUIManager ui)
        {
            llmService = llm;
            animController = anim;
            chatUI = ui;
        }

        /// <summary>
        /// 切換到指定 zone：套用 zone 的 system prompt 並清空 LLM 對話歷史。
        /// 由 ZoneManager 在角色抵達 zone 之後呼叫。
        /// </summary>
        public void ApplyZone(AIAgentChat.Zones.ZoneDefinition zone)
        {
            if (zone == null || llmService == null) return;
            if (!string.IsNullOrWhiteSpace(zone.systemPrompt))
            {
                llmService.SetSystemPrompt(zone.systemPrompt, clearHistory: true);
            }
        }

        /// <summary>把外部來源（如投票按鈕、商品卡）的輸入當作玩家訊息送進對話流程。</summary>
        public void SubmitExternalUserMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            chatUI?.AppendUserMessage(message);
            HandleUserMessageSubmitted(message);
        }
    }
}
