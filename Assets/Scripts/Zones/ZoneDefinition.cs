// =====================================================================
// ZoneDefinition.cs
// 描述一個對話 zone：位置、面向、相機機位、AI system prompt、UI 模式。
// 4 個 zone 各對應一份 asset：閒聊 / 產品Q&A / 許願投票 / 個性化推薦。
// =====================================================================
using UnityEngine;

namespace AIAgentChat.Zones
{
    public enum ZoneUIMode
    {
        Chat,            // 純文字聊天（Q1 閒聊）
        ProductQA,       // 純文字聊天 + 產品圖片可選顯示（Q2）
        WishVoting,      // 文字輸入 + 許願榜 + 按讚（Q3）
        Recommendation,  // 文字聊天 + AI 主動丟出商品卡片（Q4）
    }

    [CreateAssetMenu(fileName = "Zone", menuName = "AIAgentChat/Zone Definition", order = 0)]
    public class ZoneDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string zoneId;
        public string displayName;

        [Header("Anchor in Scene")]
        [Tooltip("角色站定點（世界座標）")]
        public Vector3 anchorPosition;

        [Tooltip("角色面向的 Yaw（度，世界 Y 軸）")]
        public float anchorYaw;

        [Header("Camera")]
        public Vector3 cameraPosition;
        public Vector3 cameraEulerAngles;
        [Range(20f, 90f)] public float cameraFOV = 48f;

        [Header("AI 對話設定")]
        public ZoneUIMode uiMode = ZoneUIMode.Chat;

        [TextArea(5, 15)]
        [Tooltip("此 zone 啟用時套用到 LLM 的 system prompt。必須要求 LLM 以 {emotion, message} JSON 格式回覆。")]
        public string systemPrompt;

        [Header("UI Hint")]
        [Tooltip("Tab 按鈕顯示的圖示（可選）")]
        public Sprite tabIcon;

        [Tooltip("Tab 按鈕的主色")]
        public Color tabAccentColor = new Color(0.27f, 0.8f, 0.77f);
    }
}
