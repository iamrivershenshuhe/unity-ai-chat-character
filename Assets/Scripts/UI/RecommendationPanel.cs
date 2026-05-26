// =====================================================================
// RecommendationPanel.cs
// 「AI 個性化推薦區」UI：
//   - 顯示 3 個商品卡（標題、tag、描述、按鈕「我想知道更多」）
//   - 點按鈕會把「請告訴我 {product} 的詳情」送進 AI 對話流
//   - 商品列表為 mock data。
// =====================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIAgentChat.UI
{
    [DisallowMultipleComponent]
    public class RecommendationPanel : MonoBehaviour
    {
        [System.Serializable]
        public class Product
        {
            public string name;
            public string tag;
            [TextArea(1, 3)] public string blurb;
            public Color accent = new Color(0.31f, 0.8f, 0.77f);
        }

        [Header("Refs")]
        [SerializeField] private AIAgentChat.AICharacterManager aiManager;
        [SerializeField] private Transform cardsContainer;
        [SerializeField] private GameObject cardPrefab; // 留空 → 程式自建

        [Tooltip("動態建立的 card label 用的字體（必須含 CJK）。WireUp 會自動指派；找不到時 Awake 會從 Canvas 內既有 TMP_Text 抄一個非 LiberationSans 的字體當 fallback。")]
        [SerializeField] private TMP_FontAsset cjkFont;

        /// <summary>cjkFont 為 null 時自動從 Canvas 內找非 LiberationSans 的字體當 fallback。</summary>
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

        [Header("Mock Data")]
        [SerializeField]
        private List<Product> products = new List<Product>
        {
            new Product { name = "極簡羊毛圍巾", tag = "本週熱銷",  blurb = "美麗諾羊毛，輕薄保暖",           accent = new Color(0.31f, 0.80f, 0.77f) },
            new Product { name = "黃銅機械桌燈", tag = "編輯精選",  blurb = "暖色 2700K，三段亮度",          accent = new Color(1.00f, 0.42f, 0.42f) },
            new Product { name = "再生皮革錢包", tag = "永續精選",  blurb = "回收皮革製成，5 卡 1 鈔層",     accent = new Color(1.00f, 0.85f, 0.24f) },
        };

        private void OnEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (cardsContainer == null) return;
            for (int i = cardsContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(cardsContainer.GetChild(i).gameObject);
            }
            foreach (var p in products) BuildCard(p);
        }

        private void BuildCard(Product p)
        {
            GameObject card;
            if (cardPrefab != null)
            {
                card = Instantiate(cardPrefab, cardsContainer);
                var label = card.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = $"{p.name}\n[{p.tag}]\n{p.blurb}";
                var btn = card.GetComponentInChildren<Button>();
                if (btn != null) btn.onClick.AddListener(() => AskAbout(p));
            }
            else
            {
                BuildDefaultCard(p);
            }
        }

        private void BuildDefaultCard(Product p)
        {
            var card = new GameObject($"Card_{p.name}", typeof(RectTransform));
            card.transform.SetParent(cardsContainer, false);
            var bg = card.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.92f);
            var le = card.AddComponent<LayoutElement>();
            le.preferredWidth = 220;
            le.preferredHeight = 260;
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 14, 14);
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Tag bar
            var tag = new GameObject("Tag", typeof(RectTransform));
            tag.transform.SetParent(card.transform, false);
            var tagImg = tag.AddComponent<Image>();
            tagImg.color = p.accent;
            var tagLE = tag.AddComponent<LayoutElement>();
            tagLE.preferredHeight = 28;
            var tagLabelGo = new GameObject("Label", typeof(RectTransform));
            tagLabelGo.transform.SetParent(tag.transform, false);
            var brt = tagLabelGo.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var tagLabel = tagLabelGo.AddComponent<TextMeshProUGUI>();
            tagLabel.text = p.tag;
            tagLabel.alignment = TextAlignmentOptions.Center;
            tagLabel.fontSize = 18;
            tagLabel.color = Color.white;
            var fT = ResolveFont(); if (fT != null) tagLabel.font = fT;

            // Title
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(card.transform, false);
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.text = p.name;
            title.fontSize = 22;
            title.color = new Color(0.12f, 0.12f, 0.12f);
            title.fontStyle = FontStyles.Bold;
            var fTitle = ResolveFont(); if (fTitle != null) title.font = fTitle;

            // Blurb
            var blurbGo = new GameObject("Blurb", typeof(RectTransform));
            blurbGo.transform.SetParent(card.transform, false);
            var blurb = blurbGo.AddComponent<TextMeshProUGUI>();
            blurb.text = p.blurb;
            blurb.fontSize = 17;
            blurb.color = new Color(0.3f, 0.3f, 0.3f);
            blurb.textWrappingMode = TextWrappingModes.Normal;
            var fB = ResolveFont(); if (fB != null) blurb.font = fB;
            var blurbLE = blurbGo.AddComponent<LayoutElement>();
            blurbLE.flexibleHeight = 1;

            // Button
            var btnGo = new GameObject("MoreButton", typeof(RectTransform));
            btnGo.transform.SetParent(card.transform, false);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = p.accent;
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var btnLE = btnGo.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 42;

            var btnLabelGo = new GameObject("Label", typeof(RectTransform));
            btnLabelGo.transform.SetParent(btnGo.transform, false);
            var lrt = btnLabelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var btnLabel = btnLabelGo.AddComponent<TextMeshProUGUI>();
            btnLabel.text = "想知道更多";
            btnLabel.fontSize = 18;
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.color = Color.white;
            var fBL = ResolveFont(); if (fBL != null) btnLabel.font = fBL;

            btn.onClick.AddListener(() => AskAbout(p));
        }

        private void AskAbout(Product p)
        {
            aiManager?.SubmitExternalUserMessage($"我想知道「{p.name}」({p.tag}) 的詳情，請給我亮點與適合的對象。");
        }
    }
}
