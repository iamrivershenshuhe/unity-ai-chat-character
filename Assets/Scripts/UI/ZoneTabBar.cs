// =====================================================================
// ZoneTabBar.cs
// 上方 4 個 tab 按鈕，點選會請求 ZoneManager 切換 zone。
// 不在 Inspector 寫死按鈕：執行時依 ZoneManager.Zones 動態建立。
// =====================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AIAgentChat.Zones;

namespace AIAgentChat.UI
{
    [DisallowMultipleComponent]
    public class ZoneTabBar : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private ZoneManager zoneManager;

        [Tooltip("Tab 按鈕的容器（建議掛 HorizontalLayoutGroup）")]
        [SerializeField] private Transform tabsContainer;

        [Tooltip("單一 tab 按鈕的 prefab；可留空 → 程式自動建一個極簡 Button + Label")]
        [SerializeField] private GameObject tabPrefab;

        [Header("外觀")]
        [SerializeField] private Color activeColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color inactiveColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color activeTextColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField] private Color inactiveTextColor = new Color(1f, 1f, 1f, 0.95f);

        [Tooltip("動態建立的 tab label 用的字體（必須含 CJK 字模）。WireUp 會自動指派；找不到時自動從 Canvas 抄一個非 LiberationSans 的字體。")]
        [SerializeField] private TMP_FontAsset cjkFont;

        private TMP_FontAsset ResolveFont()
        {
            if (cjkFont != null) return cjkFont;
            var canvas = GetComponentInParent<Canvas>(includeInactive: true);
            if (canvas != null)
            {
                foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(includeInactive: true))
                {
                    if (t.font != null && !t.font.name.Contains("LiberationSans"))
                    {
                        cjkFont = t.font;
                        return cjkFont;
                    }
                }
            }
            return null;
        }

        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> tabBackgrounds = new List<Image>();
        private readonly List<ZoneDefinition> tabZones = new List<ZoneDefinition>();

        private void Start()
        {
            if (zoneManager == null || tabsContainer == null)
            {
                Debug.LogWarning("[ZoneTabBar] zoneManager 或 tabsContainer 未指定");
                return;
            }
            BuildTabs();
            zoneManager.OnZoneActivated += HandleZoneActivated;
            zoneManager.OnZoneTransitionStarted += HandleZoneTransitionStarted;
            RefreshSelected(zoneManager.CurrentZone);
        }

        private void OnDestroy()
        {
            if (zoneManager != null)
            {
                zoneManager.OnZoneActivated -= HandleZoneActivated;
                zoneManager.OnZoneTransitionStarted -= HandleZoneTransitionStarted;
            }
        }

        private void BuildTabs()
        {
            // 清空既有
            for (int i = tabsContainer.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(tabsContainer.GetChild(i).gameObject);
            }
            tabButtons.Clear();
            tabLabels.Clear();
            tabBackgrounds.Clear();
            tabZones.Clear();

            foreach (var zone in zoneManager.Zones)
            {
                var go = tabPrefab != null
                    ? Object.Instantiate(tabPrefab, tabsContainer)
                    : CreateDefaultTab(zone);

                var btn = go.GetComponent<Button>();
                var bg = go.GetComponent<Image>();
                var label = go.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = zone.displayName;

                var captured = zone;
                if (btn != null) btn.onClick.AddListener(() => zoneManager.RequestZone(captured));

                tabButtons.Add(btn);
                tabBackgrounds.Add(bg);
                tabLabels.Add(label);
                tabZones.Add(zone);
            }
        }

        private GameObject CreateDefaultTab(ZoneDefinition zone)
        {
            var go = new GameObject($"Tab_{zone.zoneId}", typeof(RectTransform));
            go.transform.SetParent(tabsContainer, false);
            var img = go.AddComponent<Image>();
            img.color = inactiveColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = 180;
            layout.preferredHeight = 60;

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            var rt = labelGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = zone.displayName;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 22;
            tmp.color = inactiveTextColor;
            var f = ResolveFont(); if (f != null) tmp.font = f;
            return go;
        }

        private void HandleZoneActivated(ZoneDefinition zone)
        {
            RefreshSelected(zone);
            SetTabsInteractable(true);
        }

        private void HandleZoneTransitionStarted(ZoneDefinition _)
        {
            // 移動中不能再切其他 tab（避免疊指令）
            SetTabsInteractable(false);
        }

        private void SetTabsInteractable(bool value)
        {
            foreach (var b in tabButtons) if (b != null) b.interactable = value;
        }

        private void RefreshSelected(ZoneDefinition zone)
        {
            for (int i = 0; i < tabZones.Count; i++)
            {
                bool isActive = tabZones[i] == zone;
                if (tabBackgrounds[i] != null)
                {
                    tabBackgrounds[i].color = isActive ? activeColor : inactiveColor;
                }
                if (tabLabels[i] != null)
                {
                    tabLabels[i].color = isActive ? activeTextColor : inactiveTextColor;
                }
            }
        }
    }
}
