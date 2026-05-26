// =====================================================================
// ZoneSystemWireUpEditor.cs
// Editor Tool：在 AIChatScene 中安裝 ZoneManager / ZoneTabBar /
// CharacterNavigator / CameraRig / TreasureChestSpawner /
// ZonePanelSwitcher 並 wire 好 reference。
//
// 執行前提：
//   1. 已開啟 AIChatScene（含 Canvas, GameManager, AICharacter, Main Camera）
//   2. 已執行 Tools > Build Zones + Office Furniture（產生 4 個 ZoneDefinition asset）
//
// 選單位置：Tools > Wire Zone System Into Scene
// =====================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using AIAgentChat;
using AIAgentChat.Cinematics;
using AIAgentChat.Loot;
using AIAgentChat.Movement;
using AIAgentChat.UI;
using AIAgentChat.Zones;

namespace AIAgentChat.EditorTools
{
    public static class ZoneSystemWireUpEditor
    {
        private const string ZoneAssetFolder = "Assets/AIAgentChat/Zones";
        private const string LootAssetFolder = "Assets/AIAgentChat/Loot";
        private const string CJKFontPath = "Assets/Fonts/CJK SDF.asset";

        // 4 個 zone 的最新「正確」camera + anchor 位置。WireUp 會用這些覆寫 asset，
        // 避免使用者忘記重跑 Tools > Build Zones + Office Furniture。
        // 家具靠牆設計：anchor 在中央走廊，camera 在 anchor 與牆面之間，家具在 anchor 背後
        // 直線視角：camera → anchor → 家具，所有東西在一條軸上，視線淨空
        private static readonly (string id, Vector3 anchorPos, Vector3 cameraPos)[] LatestFraming =
        {
            ("chat",      new Vector3(-3.0f, 0f, -1.5f), new Vector3( 2.0f, 1.6f, -1.5f)), // 沙發靠左牆 → anchor → cam (右側看左)
            ("qa",        new Vector3( 1.5f, 0f, -3.0f), new Vector3( 1.5f, 1.6f,  2.0f)), // 展示桌靠前牆 → anchor → cam (後側看前)
            ("wish",      new Vector3(-1.5f, 0f,  3.0f), new Vector3(-1.5f, 1.6f, -2.0f)), // 投票板靠後牆 → anchor → cam (前側看後)
            ("recommend", new Vector3( 1.5f, 0f,  3.0f), new Vector3( 1.5f, 1.6f, -2.0f)), // 辦公桌靠後牆 → anchor → cam (前側看後)
        };

        [MenuItem("Tools/Wire Zone System Into Scene")]
        public static void WireUp()
        {
            // 1) 找場景上必要的物件
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var aiManager = Object.FindFirstObjectByType<AICharacterManager>();
            var animController = Object.FindFirstObjectByType<CharacterAnimatorController>();
            var chatUI = Object.FindFirstObjectByType<ChatUIManager>();
            var mainCam = Camera.main;

            if (canvas == null || aiManager == null || animController == null || chatUI == null || mainCam == null)
            {
                EditorUtility.DisplayDialog("Wire Zone System",
                    "找不到必要物件，請先執行：\n" +
                    "  Tools > Setup AI Chat Scene\n" +
                    "  Tools > Build Luxury Room\n" +
                    "  Tools > Build Zones + Office Furniture", "OK");
                return;
            }

            // 2) 載入 4 個 ZoneDefinition + DiscountCodePool；順便用最新位置覆寫 asset
            //    （避免 user 用到舊版 Build Zones 留下的錯誤 camera 位置）
            var zones = new List<ZoneDefinition>();
            foreach (var (id, anchorPos, cameraPos) in LatestFraming)
            {
                var z = AssetDatabase.LoadAssetAtPath<ZoneDefinition>($"{ZoneAssetFolder}/Zone_{id}.asset");
                if (z == null)
                {
                    EditorUtility.DisplayDialog("Wire Zone System",
                        $"找不到 Zone_{id}.asset，請先執行 Tools > Build Zones + Office Furniture", "OK");
                    return;
                }
                // 重新計算 yaw / euler 並覆寫，確保 camera 一定看到 unity-chan
                var (yaw, euler) = ZoneOfficeSetupEditor.ComputeFraming(anchorPos, cameraPos);
                z.anchorPosition = anchorPos;
                z.anchorYaw = yaw;
                z.cameraPosition = cameraPos;
                z.cameraEulerAngles = euler;
                if (z.cameraFOV < 1f) z.cameraFOV = 50f;
                EditorUtility.SetDirty(z);
                zones.Add(z);
            }
            var pool = AssetDatabase.LoadAssetAtPath<DiscountCodePool>($"{LootAssetFolder}/DiscountCodePool.asset");

            // 載入 CJK 字體；找不到就告訴 user
            var cjkFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CJKFontPath);
            if (cjkFont == null)
            {
                Debug.LogWarning($"[ZoneSystemWireUp] 找不到 {CJKFontPath} — 請先執行 Tools > Setup CJK Font，否則中文字會看不到");
            }

            // 3) CameraRig
            var rig = mainCam.GetComponent<CameraRig>() ?? mainCam.gameObject.AddComponent<CameraRig>();
            SetField(rig, "targetCamera", mainCam);

            // 4) CharacterNavigator on AICharacter
            var character = animController.gameObject;
            var navigator = character.GetComponent<CharacterNavigator>() ?? character.AddComponent<CharacterNavigator>();
            SetField(navigator, "animController", animController);

            // 5) ZoneManager on GameManager
            var manager = aiManager.gameObject.GetComponent<ZoneManager>() ?? aiManager.gameObject.AddComponent<ZoneManager>();
            SetField(manager, "zones", zones);
            SetField(manager, "navigator", navigator);
            SetField(manager, "cameraRig", rig);
            SetField(manager, "aiManager", aiManager);

            // 6) TreasureChestSpawner on GameManager
            var spawner = aiManager.gameObject.GetComponent<TreasureChestSpawner>() ?? aiManager.gameObject.AddComponent<TreasureChestSpawner>();
            SetField(spawner, "navigator", navigator);
            SetField(spawner, "chatUI", chatUI);
            SetField(spawner, "codePool", pool);

            // 7) ZoneTabBar at top of Canvas
            var tabBar = BuildOrFindTabBar(canvas);
            var tabBarComp = tabBar.GetComponent<ZoneTabBar>() ?? tabBar.AddComponent<ZoneTabBar>();
            SetField(tabBarComp, "zoneManager", manager);
            SetField(tabBarComp, "tabsContainer", tabBar.transform);
            if (cjkFont != null) SetField(tabBarComp, "cjkFont", cjkFont);

            // 8) UI panels：把現有 ChatPanel 保留，建 VotingPanel + RecommendationPanel 為兄弟節點
            var chatPanel = canvas.transform.Find("ChatPanel");
            if (chatPanel == null)
            {
                Debug.LogWarning("[ZoneSystemWireUp] 找不到 ChatPanel，請先執行 Tools > Setup AI Chat Scene");
            }
            var votingPanel = BuildOrFindVotingPanel(canvas, aiManager, cjkFont);
            var recommendPanel = BuildOrFindRecommendPanel(canvas, aiManager, cjkFont);

            // 把字體套到整個 Canvas 既有的 TMP_Text（包含 ChatPanel）
            if (cjkFont != null)
            {
                int n = 0;
                foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(includeInactive: true))
                {
                    t.font = cjkFont;
                    n++;
                }
                Debug.Log($"[ZoneSystemWireUp] 已將 CJK 字體套用到 Canvas 內 {n} 個 TMP_Text");
            }

            // 9) ZonePanelSwitcher on Canvas
            var switcher = canvas.gameObject.GetComponent<ZonePanelSwitcher>() ?? canvas.gameObject.AddComponent<ZonePanelSwitcher>();
            SetField(switcher, "zoneManager", manager);
            SetField(switcher, "chatPanel", chatPanel != null ? chatPanel.gameObject : null);
            SetField(switcher, "wishVotingPanel", votingPanel);
            SetField(switcher, "recommendationPanel", recommendPanel);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Wire Zone System",
                "Zone 系統已 wire 完成！\n\n" +
                "場景中加入：\n" +
                "  - Main Camera → CameraRig\n" +
                "  - AICharacter → CharacterNavigator\n" +
                "  - GameManager → ZoneManager + TreasureChestSpawner\n" +
                "  - Canvas → ZoneTabBar + WishVotingPanel + RecommendationPanel + ZonePanelSwitcher\n\n" +
                "請按 Cmd/Ctrl+S 存檔後 Play 測試！", "OK");
        }

        // =================================================================
        // UI builders
        // =================================================================

        private static GameObject BuildOrFindTabBar(Canvas canvas)
        {
            var existing = canvas.transform.Find("ZoneTabBar");
            if (existing != null) return existing.gameObject;

            var go = new GameObject("ZoneTabBar", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -20);
            rt.sizeDelta = new Vector2(900, 70);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.45f);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 8, 8);
            hlg.spacing = 8;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            return go;
        }

        private static GameObject BuildOrFindVotingPanel(Canvas canvas, AICharacterManager aiManager, TMP_FontAsset cjkFont)
        {
            var existing = canvas.transform.Find("WishVotingPanel");
            if (existing != null) return existing.gameObject;

            var panel = NewPanelOnRight(canvas, "WishVotingPanel", new Color(0, 0, 0, 0.5f));

            // Header
            CreateLabel(panel, "Title", "💡 商品許願榜", 28, new Vector2(20, -20), new Vector2(-20, -60), TextAlignmentOptions.MidlineLeft);

            // Wish list container (vertical)
            var list = NewChildRect(panel, "WishList");
            var listRT = list.GetComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0, 0);
            listRT.anchorMax = new Vector2(1, 1);
            listRT.offsetMin = new Vector2(20, 130);
            listRT.offsetMax = new Vector2(-20, -70);
            var vlg = list.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            // 輸入框
            var inputGO = NewChildRect(panel, "WishInput");
            var inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.anchorMin = new Vector2(0, 0);
            inputRT.anchorMax = new Vector2(1, 0);
            inputRT.pivot = new Vector2(0.5f, 0f);
            inputRT.anchoredPosition = new Vector2(0, 70);
            inputRT.sizeDelta = new Vector2(-150, 50);
            var inputImg = inputGO.AddComponent<Image>();
            inputImg.color = new Color(1, 1, 1, 0.9f);
            var input = inputGO.AddComponent<TMP_InputField>();
            BuildInputFieldGuts(inputGO, input, "我想許願…");

            // 送出按鈕
            var submitBtn = NewChildRect(panel, "SubmitWish");
            var btnRT = submitBtn.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(1, 0);
            btnRT.anchorMax = new Vector2(1, 0);
            btnRT.pivot = new Vector2(1, 0);
            btnRT.anchoredPosition = new Vector2(-20, 70);
            btnRT.sizeDelta = new Vector2(120, 50);
            var btnImg = submitBtn.AddComponent<Image>();
            btnImg.color = new Color(1f, 0.85f, 0.24f);
            var btn = submitBtn.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            CreateLabel(submitBtn, "Label", "許願", 24, new Vector2(0, 0), new Vector2(0, 0), TextAlignmentOptions.Center);

            var voting = panel.AddComponent<WishVotingPanel>();
            SetField(voting, "aiManager", aiManager);
            SetField(voting, "wishListContainer", list.transform);
            SetField(voting, "wishInput", input);
            SetField(voting, "submitWishButton", btn);
            if (cjkFont != null) SetField(voting, "cjkFont", cjkFont);

            return panel;
        }

        private static GameObject BuildOrFindRecommendPanel(Canvas canvas, AICharacterManager aiManager, TMP_FontAsset cjkFont)
        {
            var existing = canvas.transform.Find("RecommendationPanel");
            if (existing != null) return existing.gameObject;

            var panel = NewPanelOnRight(canvas, "RecommendationPanel", new Color(0, 0, 0, 0.5f));

            CreateLabel(panel, "Title", "✨ 為你推薦", 28, new Vector2(20, -20), new Vector2(-20, -60), TextAlignmentOptions.MidlineLeft);

            // 卡片橫向容器
            var cards = NewChildRect(panel, "Cards");
            var cardsRT = cards.GetComponent<RectTransform>();
            cardsRT.anchorMin = new Vector2(0, 0);
            cardsRT.anchorMax = new Vector2(1, 1);
            cardsRT.offsetMin = new Vector2(20, 30);
            cardsRT.offsetMax = new Vector2(-20, -70);
            var hlg = cards.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var rec = panel.AddComponent<RecommendationPanel>();
            SetField(rec, "aiManager", aiManager);
            SetField(rec, "cardsContainer", cards.transform);
            if (cjkFont != null) SetField(rec, "cjkFont", cjkFont);

            return panel;
        }

        private static GameObject NewPanelOnRight(Canvas canvas, string name, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1f / 3f);
            rt.offsetMin = new Vector2(20, 20);
            rt.offsetMax = new Vector2(-20, 0);
            var img = go.AddComponent<Image>();
            img.color = bg;
            go.SetActive(false); // 由 switcher 控制
            return go;
        }

        private static GameObject NewChildRect(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static GameObject CreateLabel(GameObject parent, string name, string text, float fontSize,
            Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions align)
        {
            var go = NewChildRect(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            // 改為 anchorMax 為 (1,1) 並用 offset 控制 — 但 anchor 是 top-stretch；offsetMin/Max 已含 padding
            // 簡化：給一個固定高度
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 40);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = align;
            return go;
        }

        private static void BuildInputFieldGuts(GameObject host, TMP_InputField input, string placeholder)
        {
            var textArea = NewChildRect(host, "Text Area");
            var taRT = textArea.GetComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero;
            taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(10, 6);
            taRT.offsetMax = new Vector2(-10, -6);
            textArea.AddComponent<RectMask2D>();

            var phGO = NewChildRect(textArea, "Placeholder");
            var phRT = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
            phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
            var ph = phGO.AddComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.color = new Color(0.4f, 0.4f, 0.4f);
            ph.fontSize = 22;

            var textGO = NewChildRect(textArea, "Text");
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tt = textGO.AddComponent<TextMeshProUGUI>();
            tt.text = string.Empty;
            tt.color = Color.black;
            tt.fontSize = 22;

            input.textViewport = taRT;
            input.textComponent = tt;
            input.placeholder = ph;
            input.lineType = TMP_InputField.LineType.SingleLine;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            if (target == null) return;
            var f = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null)
            {
                Debug.LogWarning($"[ZoneSystemWireUp] 找不到欄位 {fieldName} on {target.GetType().Name}");
                return;
            }
            f.SetValue(target, value);
        }
    }
}
#endif
