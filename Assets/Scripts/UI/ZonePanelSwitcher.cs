// =====================================================================
// ZonePanelSwitcher.cs
// 依據目前啟用的 ZoneDefinition.uiMode，啟用對應的 UI panel：
//   Chat / ProductQA           → 純文字對話 panel
//   WishVoting                 → 投票 panel
//   Recommendation             → 商品推薦 panel
// 同一個 zone 的 panel 之間互斥（同時間只顯示一個）。
// =====================================================================
using UnityEngine;
using AIAgentChat.Zones;

namespace AIAgentChat.UI
{
    [DisallowMultipleComponent]
    public class ZonePanelSwitcher : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private ZoneManager zoneManager;

        [Header("Panels (依 uiMode 對應)")]
        [Tooltip("Chat / ProductQA 共用：純文字對話框")]
        [SerializeField] private GameObject chatPanel;

        [SerializeField] private GameObject wishVotingPanel;

        [SerializeField] private GameObject recommendationPanel;

        private void Start()
        {
            if (zoneManager != null)
            {
                zoneManager.OnZoneActivated += HandleZoneActivated;
                HandleZoneActivated(zoneManager.CurrentZone);
            }
        }

        private void OnDestroy()
        {
            if (zoneManager != null) zoneManager.OnZoneActivated -= HandleZoneActivated;
        }

        private void HandleZoneActivated(ZoneDefinition zone)
        {
            if (zone == null) return;
            // 預設都關掉
            if (chatPanel != null) chatPanel.SetActive(false);
            if (wishVotingPanel != null) wishVotingPanel.SetActive(false);
            if (recommendationPanel != null) recommendationPanel.SetActive(false);

            switch (zone.uiMode)
            {
                case ZoneUIMode.WishVoting:
                    if (wishVotingPanel != null) wishVotingPanel.SetActive(true);
                    else if (chatPanel != null) chatPanel.SetActive(true);
                    break;
                case ZoneUIMode.Recommendation:
                    if (recommendationPanel != null) recommendationPanel.SetActive(true);
                    else if (chatPanel != null) chatPanel.SetActive(true);
                    break;
                default: // Chat, ProductQA
                    if (chatPanel != null) chatPanel.SetActive(true);
                    break;
            }
        }
    }
}
