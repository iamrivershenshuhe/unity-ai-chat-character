// =====================================================================
// WishVotingPanel.cs
// 「商品許願投票區」UI：
//   - 上方顯示目前許願榜 (Top N)
//   - 每筆有「我也要 +1」按鈕（按下會把訊息送進 AICharacterManager）
//   - 下方有輸入框讓玩家許願（送出後加入榜單最低位 + 通知 AI）
// 目前 wish 列表為 mock data（in-memory），未串接後端。
// =====================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIAgentChat.UI
{
    [DisallowMultipleComponent]
    public class WishVotingPanel : MonoBehaviour
    {
        [System.Serializable]
        public class Wish
        {
            public string title;
            public int votes;
        }

        [Header("Refs")]
        [SerializeField] private AIAgentChat.AICharacterManager aiManager;
        [SerializeField] private Transform wishListContainer;
        [SerializeField] private GameObject wishRowPrefab; // 留空 → 程式自建
        [SerializeField] private TMP_InputField wishInput;
        [SerializeField] private Button submitWishButton;

        [Tooltip("動態建立的 row label 用的字體（必須含 CJK）。WireUp 會自動指派；找不到時 Awake 會從 Canvas 內既有 TMP_Text 抄一個非 LiberationSans 的字體當 fallback。")]
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

        [Header("資料")]
        [SerializeField]
        private List<Wish> wishes = new List<Wish>
        {
            new Wish { title = "限定版聯名 T-shirt", votes = 18 },
            new Wish { title = "可客製化的金屬書籤", votes = 12 },
            new Wish { title = "迷你機械鍵盤", votes = 9 },
        };

        [SerializeField] private int topN = 3;

        private void OnEnable()
        {
            if (submitWishButton != null) submitWishButton.onClick.AddListener(HandleSubmit);
            Refresh();
        }

        private void OnDisable()
        {
            if (submitWishButton != null) submitWishButton.onClick.RemoveListener(HandleSubmit);
        }

        private void HandleSubmit()
        {
            if (wishInput == null) return;
            string t = wishInput.text?.Trim();
            if (string.IsNullOrEmpty(t)) return;

            wishes.Add(new Wish { title = t, votes = 1 });
            wishInput.text = string.Empty;
            wishInput.ActivateInputField();
            Refresh();

            aiManager?.SubmitExternalUserMessage($"我想許願：{t}");
        }

        private void Refresh()
        {
            if (wishListContainer == null) return;
            // 清空舊 row
            for (int i = wishListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(wishListContainer.GetChild(i).gameObject);
            }
            // 排序取 topN
            wishes.Sort((a, b) => b.votes.CompareTo(a.votes));
            int count = Mathf.Min(topN, wishes.Count);
            for (int i = 0; i < count; i++)
            {
                BuildRow(wishes[i], i + 1);
            }
        }

        private void BuildRow(Wish wish, int rank)
        {
            GameObject row;
            if (wishRowPrefab != null)
            {
                row = Instantiate(wishRowPrefab, wishListContainer);
                var label = row.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = $"#{rank}  {wish.title}   ❤ {wish.votes}";
                var btn = row.GetComponentInChildren<Button>();
                if (btn != null) btn.onClick.AddListener(() => VoteUp(wish));
            }
            else
            {
                row = BuildDefaultRow(wish, rank);
            }
        }

        private GameObject BuildDefaultRow(Wish wish, int rank)
        {
            var row = new GameObject($"Wish_{rank}", typeof(RectTransform));
            row.transform.SetParent(wishListContainer, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 6, 6);
            hlg.spacing = 10;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 50;

            var bg = row.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.12f);

            // Label
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(row.transform, false);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = $"#{rank}  {wish.title}   ❤ {wish.votes}";
            label.fontSize = 22;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var f1 = ResolveFont(); if (f1 != null) label.font = f1;
            var labelLE = labelGo.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1;

            // 按鈕
            var btnGo = new GameObject("VoteButton", typeof(RectTransform));
            btnGo.transform.SetParent(row.transform, false);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(1f, 0.42f, 0.42f, 0.95f);
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var btnLE = btnGo.AddComponent<LayoutElement>();
            btnLE.preferredWidth = 110;

            var btnLabelGo = new GameObject("Label", typeof(RectTransform));
            btnLabelGo.transform.SetParent(btnGo.transform, false);
            var brt = btnLabelGo.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            var btnLabel = btnLabelGo.AddComponent<TextMeshProUGUI>();
            btnLabel.text = "我也要 +1";
            btnLabel.fontSize = 20;
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.color = Color.white;
            var f2 = ResolveFont(); if (f2 != null) btnLabel.font = f2;

            btn.onClick.AddListener(() => VoteUp(wish));
            return row;
        }

        private void VoteUp(Wish wish)
        {
            wish.votes += 1;
            Refresh();
            aiManager?.SubmitExternalUserMessage($"我也想要「{wish.title}」+1！這個東西為什麼這麼受歡迎？");
        }
    }
}
