// =====================================================================
// UnityChanSetupEditor.cs
// Editor Tool：把場景中 placeholder AICharacter 換成 unity-chan 模型，
// 並重新接好 AnimatorController、CharacterAnimatorController、AICharacterManager。
// 選單位置：Tools > Use Unity-chan in Scene
// =====================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIAgentChat.EditorTools
{
    /// <summary>
    /// 在目前開啟場景中，把 placeholder 角色替換成 unity-chan。
    /// 流程：
    ///   1. 確保 AICharacter.controller 已建立（呼叫 AnimatorSetupEditor）。
    ///   2. Instantiate unitychan.prefab。
    ///   3. 沿用既有 placeholder 的 Transform，並旋轉 180° 面向相機。
    ///   4. 在 unity-chan Animator 上指定我們的 controller。
    ///   5. 停用會干擾的腳本（IdleChanger、FaceUpdate）。
    ///   6. 加上 CharacterAnimatorController。
    ///   7. 把 AICharacterManager.animController 指向新的 CharacterAnimatorController。
    /// </summary>
    public static class UnityChanSetupEditor
    {
        private const string UnityChanPrefabPath =
            "Assets/unity-chan!/Unity-chan! Model/Prefabs/unitychan.prefab";
        private const string ControllerPath = "Assets/Animations/AICharacter.controller";

        // 這些腳本會自己改 Animator 參數或 BlendShape，與我們的情緒驅動衝突
        private static readonly HashSet<string> ConflictingScriptNames = new HashSet<string>
        {
            "IdleChanger",
            "FaceUpdate",
        };

        [MenuItem("Tools/Use Unity-chan in Scene")]
        public static void UseUnityChanCharacter()
        {
            var unityChanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnityChanPrefabPath);
            if (unityChanPrefab == null)
            {
                EditorUtility.DisplayDialog("Use Unity-chan",
                    $"找不到 unity-chan prefab：\n{UnityChanPrefabPath}", "OK");
                return;
            }

            // 1) 確保 AnimatorController 存在（內含 unity-chan clips，由 AnimatorSetupEditor 處理）
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null)
            {
                AnimatorSetupEditor.SetupAnimator();
                controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            }
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Use Unity-chan",
                    $"無法載入 / 建立 AnimatorController：\n{ControllerPath}", "OK");
                return;
            }

            // 2) 找到既有的 AICharacter（不存在就以原點為位置）
            var existing = GameObject.Find("AICharacter");
            Vector3 pos = existing != null ? existing.transform.position : Vector3.zero;
            Transform parent = existing != null ? existing.transform.parent : null;

            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            // 3) Instantiate unity-chan prefab
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(unityChanPrefab);
            if (instance == null)
            {
                EditorUtility.DisplayDialog("Use Unity-chan", "Instantiate unity-chan prefab 失敗", "OK");
                return;
            }
            instance.name = "AICharacter";
            instance.transform.SetParent(parent, worldPositionStays: false);
            instance.transform.position = pos;
            // 相機從 -Z 看向 +Z；unity-chan 預設面向 +Z（背對相機），所以旋轉 180° 面向相機。
            instance.transform.rotation = Quaternion.Euler(0, 180f, 0);

            // 4) Animator：保留 unity-chan 的 Avatar，換成我們的 controller
            var animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // 避免動畫位移角色
            EditorUtility.SetDirty(animator);

            // 5) 停用衝突的腳本（IdleChanger 會擅自切換 idle、FaceUpdate 會擅自切換臉部）
            DisableConflictingScripts(instance);

            // 6) 加上我們的 CharacterAnimatorController
            var animController = instance.GetComponent<CharacterAnimatorController>();
            if (animController == null)
            {
                animController = instance.AddComponent<CharacterAnimatorController>();
            }
            // 透過反射注入 private animator 欄位（CharacterAnimatorController 在 Awake 會自動 GetComponent，
            // 但在 Editor 中提前設定，方便 Inspector 看見）
            SetPrivateField(animController, "animator", animator);
            EditorUtility.SetDirty(animController);

            // 7) 重新接線 AICharacterManager.animController
            var manager = Object.FindFirstObjectByType<AICharacterManager>();
            if (manager != null)
            {
                var ui = Object.FindFirstObjectByType<ChatUIManager>();
                // 從 manager 既有設定中讀回 llmService（不要覆蓋使用者已選的 LLM）
                var currentLLM = GetPrivateField(manager, "llmService") as LLMBase;
                if (currentLLM == null)
                {
                    currentLLM = manager.GetComponent<LLMBase>(); // fallback
                }
                manager.Configure(currentLLM, animController, ui);
                EditorUtility.SetDirty(manager);
            }

            // 8) 調整相機高度讓 unity-chan 入鏡（她身高約 1.4m）
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.transform.position = new Vector3(0, 1.2f, -2.0f);
                mainCam.transform.rotation = Quaternion.Euler(5f, 0f, 0f);
                EditorUtility.SetDirty(mainCam.transform);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog(
                "Use Unity-chan",
                "已將場景中的角色換成 unity-chan！\n\n" +
                "下一步：\n" +
                "1. 確認 GameManager 上的 API Key 仍存在。\n" +
                "2. Cmd+S 存檔。\n" +
                "3. 按 Play 測試 — 角色會以 unity-chan 的 WAIT / WIN / LOSE / 等動畫做情緒反應。",
                "OK");
        }

        // -----------------------------------------------------------------

        private static void DisableConflictingScripts(GameObject root)
        {
            int disabled = 0;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (mb == null) continue;
                if (ConflictingScriptNames.Contains(mb.GetType().Name))
                {
                    mb.enabled = false;
                    disabled++;
                }
            }
            if (disabled > 0)
            {
                Debug.Log($"[UnityChanSetupEditor] 已停用 {disabled} 個會干擾情緒動畫的 unity-chan 腳本（IdleChanger / FaceUpdate）");
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null) return;
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field == null) return;
            field.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            if (target == null) return null;
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            return field?.GetValue(target);
        }
    }
}
#endif
