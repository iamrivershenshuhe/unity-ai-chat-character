// =====================================================================
// CharacterAnimatorController.cs
// 角色動畫控制器：根據情緒字串觸發對應的 Animator Trigger，
// 並提供 PlayTalking / PlayIdle 切換等待動畫。
// =====================================================================
using System.Collections.Generic;
using UnityEngine;

namespace AIAgentChat
{
    /// <summary>
    /// 控制 AI 角色的動畫狀態。
    /// 依賴：場景中有一個 <see cref="Animator"/>，其 Controller 含有
    /// 對應情緒的 Trigger（Happy / Sad / Thinking / Greeting / Neutral /
    /// Surprised / Angry）以及一個 Bool "IsTalking"。
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterAnimatorController : MonoBehaviour
    {
        [Header("Animator")]
        [Tooltip("角色的 Animator 元件。若留空，會在 Awake 自動 GetComponent")]
        [SerializeField] private Animator animator;

        [Header("Animator 參數名稱（需與 Animator Controller 中的設定一致）")]
        [Tooltip("等待 AI 回覆時播放的 Bool 參數")]
        [SerializeField] private string talkingBoolName = "IsTalking";

        /// <summary>
        /// 情緒字串 → Animator Trigger 名稱對照表。
        /// 若 LLM 回傳的情緒字串不在此表中，會被視為 neutral。
        /// </summary>
        private readonly Dictionary<string, string> emotionToTrigger = new Dictionary<string, string>
        {
            { "happy",     "Happy" },
            { "sad",       "Sad" },
            { "thinking",  "Thinking" },
            { "greeting",  "Greeting" },
            { "neutral",   "Neutral" },
            { "surprised", "Surprised" },
            { "angry",     "Angry" },
        };

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }
        }

        /// <summary>
        /// 根據情緒標籤觸發對應的動畫 Trigger，並關閉「說話中」的 Bool。
        /// </summary>
        /// <param name="emotion">情緒字串（happy / sad / thinking / greeting / neutral / surprised / angry）</param>
        public void PlayEmotion(string emotion)
        {
            if (animator == null)
            {
                Debug.LogWarning("[CharacterAnimatorController] Animator 未設定，無法播放動畫");
                return;
            }

            string key = string.IsNullOrEmpty(emotion) ? "neutral" : emotion.ToLowerInvariant();
            if (!emotionToTrigger.TryGetValue(key, out string triggerName))
            {
                // 未知情緒退回 neutral
                triggerName = emotionToTrigger["neutral"];
            }

            // 切換到情緒動畫前先解除說話狀態
            SetTalkingFlag(false);
            animator.SetTrigger(triggerName);
        }

        /// <summary>切換到 Idle 待機（藉由關閉 IsTalking Bool，並觸發 Neutral）。</summary>
        public void PlayIdle()
        {
            if (animator == null) return;
            SetTalkingFlag(false);
            // Neutral 在 Animator 中是「短暫過渡到 Idle」的角色，使用 Trigger 切回 Idle
            if (HasParameter(animator, "Neutral", AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger("Neutral");
            }
        }

        /// <summary>切換到「思考 / 說話中」循環動畫（等待 AI 回覆時使用）。</summary>
        public void PlayTalking()
        {
            if (animator == null) return;
            SetTalkingFlag(true);
        }

        /// <summary>內部：安全地設定 IsTalking Bool。</summary>
        private void SetTalkingFlag(bool value)
        {
            if (HasParameter(animator, talkingBoolName, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(talkingBoolName, value);
            }
        }

        /// <summary>檢查 Animator 是否有指定的參數（名稱 + 類型）。</summary>
        private static bool HasParameter(Animator anim, string paramName, AnimatorControllerParameterType type)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return false;
            foreach (var p in anim.parameters)
            {
                if (p.name == paramName && p.type == type) return true;
            }
            return false;
        }
    }
}
