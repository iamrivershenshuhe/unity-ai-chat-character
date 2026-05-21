// =====================================================================
// SceneSetupEditor.cs
// Editor Tool：一鍵建立 AI 對話 Demo 場景
//   - Camera + Light + AICharacter（含 Animator）
//   - Canvas + 聊天 UI + 訊息氣泡 Prefabs
//   - GameManager（AICharacterManager + ChatGPTService + ChatGeminiService）
// 選單位置：Tools > Setup AI Chat Scene
// =====================================================================
#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AIAgentChat.EditorTools
{
    /// <summary>
    /// 自動建立 AI 對話 Demo 場景（含角色 / 相機 / Canvas / Prefabs / GameManager）。
    /// </summary>
    public static class SceneSetupEditor
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string AnimationsFolder = "Assets/Animations";
        private const string ScenePath = ScenesFolder + "/AIChatScene.unity";
        private const string ControllerPath = AnimationsFolder + "/AICharacter.controller";
        private const string UserBubblePrefabPath = PrefabsFolder + "/UserMessageBubble.prefab";
        private const string AIBubblePrefabPath = PrefabsFolder + "/AIMessageBubble.prefab";

        /// <summary>
        /// 修補目前場景的 EventSystem：若使用了 StandaloneInputModule（新版 Input System
        /// 下完全不會運作），改成 InputSystemUIInputModule。不會動到其他物件。
        /// </summary>
        [MenuItem("Tools/Fix EventSystem Input Module")]
        public static void FixEventSystemInputModule()
        {
            var es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es == null)
            {
                EditorUtility.DisplayDialog("Fix EventSystem", "場景中找不到 EventSystem，請先執行 Tools > Setup AI Chat Scene", "OK");
                return;
            }

#if ENABLE_INPUT_SYSTEM
            // 移除舊的 StandaloneInputModule（若存在）
            var legacy = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (legacy != null) Object.DestroyImmediate(legacy);

            var current = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (current == null)
            {
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            EditorSceneManager.MarkSceneDirty(es.gameObject.scene);
            EditorUtility.DisplayDialog("Fix EventSystem",
                "已將 EventSystem 切換為 InputSystemUIInputModule。\n" +
                "請存檔（Cmd/Ctrl+S）後按 Play 測試。", "OK");
#else
            var current = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (current == null)
            {
                es.gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
            EditorSceneManager.MarkSceneDirty(es.gameObject.scene);
            EditorUtility.DisplayDialog("Fix EventSystem", "已套用 StandaloneInputModule（舊版 Input Manager）", "OK");
#endif
        }

        [MenuItem("Tools/Setup AI Chat Scene")]
        public static void SetupScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(AnimationsFolder);

            // 確保 AnimatorController 已存在（沒有就先建立）
            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) == null)
            {
                AnimatorSetupEditor.SetupAnimator();
            }

            // 建立或載入兩個訊息氣泡 Prefab
            GameObject userBubblePrefab = EnsureBubblePrefab(UserBubblePrefabPath, isUser: true);
            GameObject aiBubblePrefab = EnsureBubblePrefab(AIBubblePrefabPath, isUser: false);

            // 新場景
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1) Camera
            var cameraGO = new GameObject("Main Camera");
            var camera = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
            cameraGO.tag = "MainCamera";
            cameraGO.transform.position = new Vector3(0, 1.5f, -2.5f);
            cameraGO.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.Skybox;

            // 2) Directional Light
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 3) Placeholder 角色（Capsule + 兩顆眼睛）
            var character = CreatePlaceholderCharacter();
            // 掛上 Animator + Controller
            var animator = character.AddComponent<Animator>();
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            animator.runtimeAnimatorController = controller;
            // 掛上 CharacterAnimatorController
            var animController = character.AddComponent<AIAgentChat.CharacterAnimatorController>();

            // 4) Canvas（Screen Space - Overlay）+ 聊天 UI
            var canvasGO = new GameObject("Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            // EventSystem — 依專案的 Active Input Handling 自動選擇正確的 InputModule
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
                // 新版 Input System：用 InputSystemUIInputModule，否則 UI 完全收不到事件
                esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            }

            // 聊天 Panel：畫面下方 1/3
            var panel = CreateUIElement("ChatPanel", canvasGO.transform);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0f, 0f);
            panelRT.anchorMax = new Vector2(1f, 1f / 3f);
            panelRT.offsetMin = new Vector2(20, 20);
            panelRT.offsetMax = new Vector2(-20, 0);
            var panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0f, 0f, 0f, 0.35f);

            // ScrollRect for 對話歷史
            var scrollGO = CreateUIElement("ChatScroll", panel.transform);
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.offsetMin = new Vector2(10, 70);
            scrollRT.offsetMax = new Vector2(-10, -10);
            scrollGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);
            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            // Viewport
            var viewportGO = CreateUIElement("Viewport", scrollGO.transform);
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewportGO.AddComponent<RectMask2D>();
            var viewportImage = viewportGO.AddComponent<Image>();
            viewportImage.color = new Color(1, 1, 1, 0.01f);

            // Content
            var contentGO = CreateUIElement("Content", viewportGO.transform);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.offsetMin = new Vector2(10, 0);
            contentRT.offsetMax = new Vector2(-10, 0);
            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRT;
            scrollRect.content = contentRT;

            // 輸入框
            var inputGO = CreateUIElement("InputField", panel.transform);
            var inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.anchorMin = new Vector2(0, 0);
            inputRT.anchorMax = new Vector2(1, 0);
            inputRT.pivot = new Vector2(0.5f, 0f);
            inputRT.anchoredPosition = new Vector2(0, 10);
            inputRT.sizeDelta = new Vector2(-140, 50);
            var inputImg = inputGO.AddComponent<Image>();
            inputImg.color = new Color(1f, 1f, 1f, 0.85f);
            var inputField = inputGO.AddComponent<TMP_InputField>();

            // Input Text 子物件
            var textAreaGO = CreateUIElement("Text Area", inputGO.transform);
            var textAreaRT = textAreaGO.GetComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(10, 6);
            textAreaRT.offsetMax = new Vector2(-10, -6);
            textAreaGO.AddComponent<RectMask2D>();

            var placeholderGO = CreateUIElement("Placeholder", textAreaGO.transform);
            var placeholderRT = placeholderGO.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.offsetMin = Vector2.zero;
            placeholderRT.offsetMax = Vector2.zero;
            var placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholderText.text = "輸入訊息...";
            placeholderText.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            placeholderText.fontSize = 24;

            var inputTextGO = CreateUIElement("Text", textAreaGO.transform);
            var inputTextRT = inputTextGO.GetComponent<RectTransform>();
            inputTextRT.anchorMin = Vector2.zero;
            inputTextRT.anchorMax = Vector2.one;
            inputTextRT.offsetMin = Vector2.zero;
            inputTextRT.offsetMax = Vector2.zero;
            var inputTextTMP = inputTextGO.AddComponent<TextMeshProUGUI>();
            inputTextTMP.text = string.Empty;
            inputTextTMP.color = Color.black;
            inputTextTMP.fontSize = 24;

            inputField.textViewport = textAreaRT;
            inputField.textComponent = inputTextTMP;
            inputField.placeholder = placeholderText;
            inputField.lineType = TMP_InputField.LineType.SingleLine;

            // 送出按鈕
            var sendBtnGO = CreateUIElement("SendButton", panel.transform);
            var sendBtnRT = sendBtnGO.GetComponent<RectTransform>();
            sendBtnRT.anchorMin = new Vector2(1, 0);
            sendBtnRT.anchorMax = new Vector2(1, 0);
            sendBtnRT.pivot = new Vector2(1f, 0f);
            sendBtnRT.anchoredPosition = new Vector2(-10, 10);
            sendBtnRT.sizeDelta = new Vector2(120, 50);
            var sendBtnImg = sendBtnGO.AddComponent<Image>();
            sendBtnImg.color = new Color(0.2f, 0.6f, 1f, 1f);
            var sendBtn = sendBtnGO.AddComponent<Button>();
            sendBtn.targetGraphic = sendBtnImg;

            var sendLabelGO = CreateUIElement("Label", sendBtnGO.transform);
            var sendLabelRT = sendLabelGO.GetComponent<RectTransform>();
            sendLabelRT.anchorMin = Vector2.zero;
            sendLabelRT.anchorMax = Vector2.one;
            sendLabelRT.offsetMin = Vector2.zero;
            sendLabelRT.offsetMax = Vector2.zero;
            var sendLabel = sendLabelGO.AddComponent<TextMeshProUGUI>();
            sendLabel.text = "送出";
            sendLabel.alignment = TextAlignmentOptions.Center;
            sendLabel.color = Color.white;
            sendLabel.fontSize = 26;

            // 思考中提示
            var typingGO = CreateUIElement("TypingIndicator", panel.transform);
            var typingRT = typingGO.GetComponent<RectTransform>();
            typingRT.anchorMin = new Vector2(0, 1);
            typingRT.anchorMax = new Vector2(1, 1);
            typingRT.pivot = new Vector2(0.5f, 1f);
            typingRT.anchoredPosition = new Vector2(0, -5);
            typingRT.sizeDelta = new Vector2(-20, 30);
            var typingTMP = typingGO.AddComponent<TextMeshProUGUI>();
            typingTMP.text = "AI 思考中…";
            typingTMP.alignment = TextAlignmentOptions.Left;
            typingTMP.color = new Color(1f, 1f, 1f, 0.85f);
            typingTMP.fontSize = 22;
            typingGO.SetActive(false);

            // 掛上 ChatUIManager 並注入 reference
            var chatUI = canvasGO.AddComponent<AIAgentChat.ChatUIManager>();
            SetPrivateField(chatUI, "inputField", inputField);
            SetPrivateField(chatUI, "sendButton", sendBtn);
            SetPrivateField(chatUI, "chatScrollRect", scrollRect);
            SetPrivateField(chatUI, "messageContainer", contentGO.transform);
            SetPrivateField(chatUI, "userMessagePrefab", userBubblePrefab);
            SetPrivateField(chatUI, "aiMessagePrefab", aiBubblePrefab);
            SetPrivateField(chatUI, "typingIndicator", typingTMP);

            // 5) GameManager
            var managerGO = new GameObject("GameManager");
            var chatgpt = managerGO.AddComponent<AIAgentChat.ChatGPTService>();
            var gemini = managerGO.AddComponent<AIAgentChat.ChatGeminiService>();
            var aiManager = managerGO.AddComponent<AIAgentChat.AICharacterManager>();
            // 預設使用 ChatGPTService
            aiManager.Configure(chatgpt, animController, chatUI);

            // 6) 存場景
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings(ScenePath);

            Debug.Log($"[SceneSetupEditor] 場景已建立並儲存：{ScenePath}");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "AI Chat Scene",
                    "場景已建立完成！\n\n" +
                    "下一步：\n" +
                    "1. 選擇 GameManager → 在 ChatGPTService 或 ChatGeminiService 上填入 API Key\n" +
                    "2. 在 AICharacterManager 的 llmService 欄位拖入想用的 LLM 元件（預設為 ChatGPTService）\n" +
                    "3. 按 Play 開始對話！",
                    "OK");
            }
        }

        // ----- Helpers -----

        private static GameObject CreatePlaceholderCharacter()
        {
            var root = new GameObject("AICharacter");
            root.transform.position = new Vector3(0, 0, 0);

            // 身體（Capsule）
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            var bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null && bodyRenderer.sharedMaterial != null)
            {
                bodyRenderer.sharedMaterial.color = new Color(0.85f, 0.7f, 0.55f);
            }

            // 兩顆眼睛
            CreateEye(body.transform, "EyeLeft", new Vector3(-0.15f, 0.55f, -0.45f));
            CreateEye(body.transform, "EyeRight", new Vector3(0.15f, 0.55f, -0.45f));
            return root;
        }

        private static void CreateEye(Transform parent, string name, Vector3 localPos)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = name;
            eye.transform.SetParent(parent, false);
            eye.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            eye.transform.localPosition = localPos;
            // 移除多餘的 Collider
            var col = eye.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var r = eye.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null) r.sharedMaterial.color = Color.black;
        }

        private static GameObject CreateUIElement(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject EnsureBubblePrefab(string path, bool isUser)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject(isUser ? "UserMessageBubble" : "AIMessageBubble", typeof(RectTransform));
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(0, 0);
            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            var rootFitter = root.AddComponent<ContentSizeFitter>();
            rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bubble = new GameObject("Bubble", typeof(RectTransform));
            bubble.transform.SetParent(root.transform, false);
            var bubbleImg = bubble.AddComponent<Image>();
            bubbleImg.color = isUser ? new Color(0.2f, 0.6f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.95f);
            var bubbleLE = bubble.AddComponent<LayoutElement>();
            bubbleLE.preferredWidth = 700;
            bubbleLE.flexibleWidth = 0;
            var bubbleVLG = bubble.AddComponent<VerticalLayoutGroup>();
            bubbleVLG.padding = new RectOffset(15, 15, 10, 10);
            bubbleVLG.childAlignment = TextAnchor.UpperLeft;
            bubbleVLG.childForceExpandWidth = true;
            bubbleVLG.childForceExpandHeight = false;
            bubbleVLG.childControlWidth = true;
            bubbleVLG.childControlHeight = true;
            var bubbleFitter = bubble.AddComponent<ContentSizeFitter>();
            bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(bubble.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = isUser ? "User message" : "AI message";
            tmp.fontSize = 24;
            tmp.color = isUser ? Color.white : Color.black;
            tmp.enableWordWrapping = true;

            var chatBubble = root.AddComponent<AIAgentChat.ChatBubble>();
            SetPrivateField(chatBubble, "messageText", tmp);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var buildScene in scenes)
            {
                if (buildScene.path == scenePath)
                {
                    buildScene.enabled = true;
                    EditorBuildSettings.scenes = scenes;
                    return;
                }
            }

            var updatedScenes = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updatedScenes, 0);
            updatedScenes[updatedScenes.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updatedScenes;
        }

        /// <summary>使用反射注入 [SerializeField] private 欄位。</summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null) return;
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field == null)
            {
                Debug.LogWarning($"[SceneSetupEditor] 找不到欄位 {fieldName} on {target.GetType().Name}");
                return;
            }
            field.SetValue(target, value);
        }
    }
}
#endif
