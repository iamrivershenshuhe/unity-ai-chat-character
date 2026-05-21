// =====================================================================
// FontSetupEditor.cs
// Editor Tool：一鍵建立含 CJK 字符的 TMP Font Asset，並套用到場景與氣泡 Prefab。
//
// 選單：
//   - Tools > Setup CJK Font            ：偵測 Assets/Fonts/ 內最新的字型，
//                                         若 TMP Asset 不存在或來源字型已變更
//                                         則自動重建。
//   - Tools > Rebuild CJK Font (Force)  ：不管目前狀態，強制刪除 CJK SDF.asset
//                                         並從最新的來源字型重建。
//
// 原理：TextMeshPro 預設 LiberationSans 不含中文字模，所以中文顯示為空白 /
// 方塊。本工具會：
//   1. 從 Assets/Fonts/ 挑「最近修改」的 .ttf/.otf/.ttc 當來源（這樣你新丟進去
//      的字型會被優先使用）。若資料夾為空，從 macOS 系統字型複製一份過去。
//   2. 用該字型建立 Dynamic TMP_FontAsset（Atlas 動態擴張）。
//   3. 把 Font Asset 套用到目前場景所有 TMP_Text，以及氣泡 Prefab。
// =====================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIAgentChat.EditorTools
{
    /// <summary>建立並套用支援 CJK 的 TMP Font Asset。</summary>
    public static class FontSetupEditor
    {
        private const string FontsFolder = "Assets/Fonts";
        private const string TmpFontAssetPath = FontsFolder + "/CJK SDF.asset";

        // 候選系統字型路徑（依優先順序），都是含 CJK 字符的字型
        private static readonly string[] SystemFontCandidates =
        {
            "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
            "/Library/Fonts/Arial Unicode.ttf",
            "/System/Library/Fonts/STHeiti Medium.ttc",
            "/System/Library/Fonts/STHeiti Light.ttc",
            "/System/Library/Fonts/Supplemental/Songti.ttc",
            "/System/Library/Fonts/PingFang.ttc",
        };

        [MenuItem("Tools/Setup CJK Font")]
        public static void SetupCJKFont() => Run(forceRebuild: false);

        [MenuItem("Tools/Rebuild CJK Font (Force)")]
        public static void RebuildCJKFont() => Run(forceRebuild: true);

        // -----------------------------------------------------------------

        private static void Run(bool forceRebuild)
        {
            EnsureFolder(FontsFolder);

            string fontAssetPath = PickOrImportSourceFont();
            if (string.IsNullOrEmpty(fontAssetPath))
            {
                EditorUtility.DisplayDialog(
                    "Setup CJK Font",
                    "找不到 CJK 字型。\n\n" +
                    "請把含中文字字模的 .ttf / .otf / .ttc 放到 Assets/Fonts/，再執行此選單。",
                    "OK");
                return;
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(fontAssetPath);
            if (sourceFont == null)
            {
                EditorUtility.DisplayDialog("Setup CJK Font",
                    $"無法以 Font asset 載入 {fontAssetPath}，請確認檔案有被 Unity 匯入", "OK");
                return;
            }

            var tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontAssetPath);

            // 判斷是否需要重建：
            //   - 強制重建
            //   - 不存在
            //   - 來源字型不同（「我換了字型但沒生效」的常見情況）
            //   - atlas texture / material 不見（之前版本的 bug 留下的損壞資產）
            bool atlasMissing = false;
            if (tmpFont != null)
            {
                bool noAtlas = tmpFont.atlasTextures == null
                               || tmpFont.atlasTextures.Length == 0
                               || tmpFont.atlasTextures[0] == null;
                bool noMaterial = tmpFont.material == null;
                atlasMissing = noAtlas || noMaterial;
            }

            bool needRebuild = forceRebuild
                               || tmpFont == null
                               || tmpFont.sourceFontFile != sourceFont
                               || atlasMissing;

            if (needRebuild)
            {
                if (tmpFont != null)
                {
                    AssetDatabase.DeleteAsset(TmpFontAssetPath);
                }

                tmpFont = TMP_FontAsset.CreateFontAsset(sourceFont);
                if (tmpFont == null)
                {
                    EditorUtility.DisplayDialog("Setup CJK Font",
                        $"TMP_FontAsset.CreateFontAsset 對 {fontAssetPath} 回傳 null。\n" +
                        "可能原因：該 .ttc 中的字面（face）無 outline，或 Unity 無法解析該字型。\n" +
                        "請試試另一份 .ttf（例如把 STHeiti 換成 Arial Unicode 或 Noto Sans CJK）。",
                        "OK");
                    return;
                }

                tmpFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                AssetDatabase.CreateAsset(tmpFont, TmpFontAssetPath);

                // 重要：atlas texture / material 在記憶體中，必須以 sub-asset 寫入
                // 同一個 .asset 檔，否則重新載入專案後它們會變成 missing reference，
                // TMP 就無法繪製任何字，連帶讓 InputField 也吃不到事件。
                EmbedFontSubAssets(tmpFont);

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(TmpFontAssetPath, ImportAssetOptions.ForceUpdate);

                Debug.Log($"[FontSetupEditor] 重建 TMP Font Asset：{TmpFontAssetPath} ← 來源 {fontAssetPath}");
            }
            else
            {
                // 既有資產且來源相同：只確認是 Dynamic 模式
                tmpFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                EditorUtility.SetDirty(tmpFont);
                Debug.Log($"[FontSetupEditor] 沿用既有 TMP Font Asset（來源未變）：{TmpFontAssetPath}");
            }

            // 套用到場景 + Prefab
            int sceneCount = ApplyFontToActiveScene(tmpFont);
            int prefabCount = 0;
            prefabCount += ApplyFontToPrefab("Assets/Prefabs/UserMessageBubble.prefab", tmpFont);
            prefabCount += ApplyFontToPrefab("Assets/Prefabs/AIMessageBubble.prefab", tmpFont);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Setup CJK Font",
                (needRebuild ? "已重建 TMP Font Asset。\n\n" : "TMP Font Asset 已存在（來源字型未變更）。\n\n") +
                $"來源字型：{fontAssetPath}\n" +
                $"TMP Asset：{TmpFontAssetPath}\n" +
                $"場景中更新的 TMP_Text 數量：{sceneCount}\n" +
                $"Prefab 中更新的 TMP_Text 數量：{prefabCount}\n\n" +
                "請存檔（Cmd+S）後按 Play 測試。",
                "OK");
        }

        /// <summary>
        /// 從 Assets/Fonts/ 挑「最近修改」的字型檔當來源；資料夾為空就從系統字型補一份。
        /// </summary>
        private static string PickOrImportSourceFont()
        {
            var candidates = new List<(string path, DateTime mtime)>();
            foreach (var ext in new[] { "*.ttf", "*.otf", "*.ttc" })
            {
                foreach (var f in Directory.GetFiles(FontsFolder, ext, SearchOption.TopDirectoryOnly))
                {
                    candidates.Add((f.Replace("\\", "/"), File.GetLastWriteTimeUtc(f)));
                }
            }
            if (candidates.Count > 0)
            {
                // 取最近修改的那份 — 使用者剛丟進來的會自動被挑中
                candidates.Sort((a, b) => b.mtime.CompareTo(a.mtime));
                string chosen = candidates[0].path;
                if (candidates.Count > 1)
                {
                    Debug.Log($"[FontSetupEditor] Assets/Fonts/ 內有 {candidates.Count} 份字型，挑最近修改的：{chosen}");
                }
                return chosen;
            }

            // 沒有 → 從系統字型抓一份
            foreach (var src in SystemFontCandidates)
            {
                if (File.Exists(src))
                {
                    string dst = FontsFolder + "/" + Path.GetFileName(src);
                    File.Copy(src, dst, overwrite: true);
                    AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
                    Debug.Log($"[FontSetupEditor] 從系統複製字型：{src} → {dst}");
                    return dst;
                }
            }
            return null;
        }

        /// <summary>
        /// 把 TMP_FontAsset 的 atlas texture 與 material 以 sub-asset 形式
        /// 嵌入到主 .asset 檔，否則 Unity 重新載入後它們會變成 missing reference。
        /// </summary>
        private static void EmbedFontSubAssets(TMP_FontAsset asset)
        {
            if (asset == null) return;

            // Atlas texture(s)
            if (asset.atlasTextures != null)
            {
                for (int i = 0; i < asset.atlasTextures.Length; i++)
                {
                    var tex = asset.atlasTextures[i];
                    if (tex == null) continue;
                    if (!AssetDatabase.Contains(tex))
                    {
                        tex.name = i == 0 ? "Atlas" : $"Atlas_{i}";
                        AssetDatabase.AddObjectToAsset(tex, asset);
                    }
                }
            }

            // Material
            if (asset.material != null && !AssetDatabase.Contains(asset.material))
            {
                asset.material.name = "Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
        }

        private static int ApplyFontToActiveScene(TMP_FontAsset tmpFont)
        {
            int count = 0;
            var texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in texts)
            {
                t.font = tmpFont;
                EditorUtility.SetDirty(t);
                count++;
            }
            if (count > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            return count;
        }

        private static int ApplyFontToPrefab(string prefabPath, TMP_FontAsset tmpFont)
        {
            if (string.IsNullOrEmpty(prefabPath)) return 0;
            if (!File.Exists(prefabPath))
            {
                Debug.LogWarning($"[FontSetupEditor] Prefab 不存在，略過：{prefabPath}");
                return 0;
            }

            var instance = PrefabUtility.LoadPrefabContents(prefabPath);
            if (instance == null) return 0;

            int count = 0;
            try
            {
                foreach (var t in instance.GetComponentsInChildren<TMP_Text>(includeInactive: true))
                {
                    t.font = tmpFont;
                    count++;
                }
                if (count > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }
            return count;
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
    }
}
#endif
