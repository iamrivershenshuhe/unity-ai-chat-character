// =====================================================================
// AnimatorSetupEditor.cs
// Editor Tool：一鍵建立 AICharacter.controller 與 placeholder .anim 檔案。
// 選單位置：Tools > Setup AI Character Animator
// =====================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AIAgentChat.EditorTools
{
    /// <summary>
    /// 自動建立 AI 角色用的 AnimatorController：包含所有情緒 State、
    /// Talking Bool、Any State 觸發轉換，以及 placeholder Animation Clips。
    /// </summary>
    public static class AnimatorSetupEditor
    {
        private const string AnimationsFolder = "Assets/Animations";
        private const string ControllerPath = AnimationsFolder + "/AICharacter.controller";

        // unity-chan 動畫資料夾。若存在，會優先使用內部的 FBX 動畫 clip 取代 placeholder。
        private const string UnityChanAnimFolder = "Assets/unity-chan!/Unity-chan! Model/Art/Animations";

        private static readonly string[] EmotionStates =
        {
            "Happy", "Sad", "Thinking", "Greeting", "Neutral", "Surprised", "Angry",
        };

        // 將每個 AnimatorState 對應到 unity-chan 動畫 FBX 的「檔名（不含副檔名）」。
        // 載入時用 AssetDatabase.LoadAllAssetsAtPath 從 FBX 取出 AnimationClip。
        private static readonly Dictionary<string, string> UnityChanClipMap = new Dictionary<string, string>
        {
            { "Idle",      "unitychan_WAIT00" },
            { "Talking",   "unitychan_WAIT01" }, // 等待 AI 回覆時用，循環待機
            { "Walk",      "unitychan_WALK00_F" }, // zone 切換時的位移動畫
            { "Happy",     "unitychan_WIN00" },
            { "Sad",       "unitychan_LOSE00" },
            { "Thinking",  "unitychan_REFLESH00" },
            { "Greeting",  "unitychan_HANDUP00_R" },
            { "Neutral",   "unitychan_WAIT03" },
            { "Surprised", "unitychan_UMATOBI00" },
            { "Angry",     "unitychan_DAMAGED01" },
        };

        [MenuItem("Tools/Setup AI Character Animator")]
        public static void SetupAnimator()
        {
            EnsureFolder(AnimationsFolder);

            // 1) 建立 AnimatorController（若已存在就直接覆蓋使用）
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            // 2) 加入 Parameters
            EnsureParameter(controller, "IsTalking", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "IsWalking", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "Idle", AnimatorControllerParameterType.Trigger);
            foreach (var emotion in EmotionStates)
            {
                EnsureParameter(controller, emotion, AnimatorControllerParameterType.Trigger);
            }

            // 3) 取得每個 State 對應的 AnimationClip。
            //    優先順序：unity-chan FBX 中的 clip > placeholder
            //    （若使用者沒有 unity-chan 資產，會退回 placeholder 模式，行為與舊版一致）
            bool unityChanAvailable = AssetDatabase.IsValidFolder(UnityChanAnimFolder);
            var clips = new Dictionary<string, AnimationClip>();
            clips["Idle"] = ResolveClip("Idle", loop: true, unityChanAvailable);
            clips["Talking"] = ResolveClip("Talking", loop: true, unityChanAvailable);
            clips["Walk"] = ResolveClip("Walk", loop: true, unityChanAvailable);
            foreach (var emotion in EmotionStates)
            {
                // 情緒動畫播完會自動回 Idle，因此不需要 loop
                clips[emotion] = ResolveClip(emotion, loop: false, unityChanAvailable);
            }

            // 4) 設定 root state machine：建立各 State，並設 Idle 為預設
            var rootSm = controller.layers[0].stateMachine;
            RemoveExistingStates(rootSm);

            var idleState = rootSm.AddState("Idle");
            idleState.motion = clips["Idle"];
            rootSm.defaultState = idleState;

            var talkingState = rootSm.AddState("Talking");
            talkingState.motion = clips["Talking"];

            var walkState = rootSm.AddState("Walk");
            walkState.motion = clips["Walk"];

            // 各情緒 State
            var emotionStateMap = new Dictionary<string, AnimatorState>();
            foreach (var emotion in EmotionStates)
            {
                var s = rootSm.AddState(emotion);
                s.motion = clips[emotion];
                emotionStateMap[emotion] = s;
            }

            // 5) Transitions
            // Any State → Talking（透過 Bool IsTalking == true）
            var toTalking = rootSm.AddAnyStateTransition(talkingState);
            toTalking.hasExitTime = false;
            toTalking.duration = 0.1f;
            toTalking.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            toTalking.canTransitionToSelf = false;

            // Talking → Idle（IsTalking == false）
            var talkingToIdle = talkingState.AddTransition(idleState);
            talkingToIdle.hasExitTime = false;
            talkingToIdle.duration = 0.1f;
            talkingToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");

            // Any State → Walk（IsWalking == true）
            var toWalk = rootSm.AddAnyStateTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.1f;
            toWalk.canTransitionToSelf = false;
            toWalk.AddCondition(AnimatorConditionMode.If, 0, "IsWalking");

            // Walk → Idle（IsWalking == false）
            var walkToIdle = walkState.AddTransition(idleState);
            walkToIdle.hasExitTime = false;
            walkToIdle.duration = 0.15f;
            walkToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsWalking");

            // Any State → Emotion（透過 Trigger）
            foreach (var emotion in EmotionStates)
            {
                var t = rootSm.AddAnyStateTransition(emotionStateMap[emotion]);
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.If, 0, emotion);

                // Emotion 播完後自動回 Idle
                var back = emotionStateMap[emotion].AddTransition(idleState);
                back.hasExitTime = true;
                back.exitTime = 0.95f;
                back.duration = 0.1f;
            }

            // Any State → Idle（透過 Trigger，用於 PlayIdle 強制切回 — 透過 Neutral trigger 已可達成，
            // 此處不另外加 Idle trigger 以避免 transition 衝突）

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AnimatorSetupEditor] 已建立 / 更新 AnimatorController：{ControllerPath}");
            EditorUtility.DisplayDialog(
                "AI Character Animator",
                "AnimatorController 已建立完成！\n" +
                "位置：" + ControllerPath + "\n\n" +
                "下一步：把它指定到場景中 AICharacter 的 Animator 元件，\n" +
                "或執行 Tools > Setup AI Chat Scene 自動完成。",
                "OK");
        }

        // ----- 內部工具 -----

        private static void RemoveExistingStates(AnimatorStateMachine sm)
        {
            // 移除既有 child states（保留 entry / exit / any）
            var snapshot = new List<ChildAnimatorState>(sm.states);
            foreach (var child in snapshot)
            {
                sm.RemoveState(child.state);
            }
            // 同時清掉 Any State 既有的 transitions
            var anyTrans = new List<AnimatorStateTransition>(sm.anyStateTransitions);
            foreach (var t in anyTrans)
            {
                sm.RemoveAnyStateTransition(t);
            }
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == name)
                {
                    if (p.type != type)
                    {
                        Debug.LogWarning($"[AnimatorSetupEditor] 參數 {name} 已存在但類型不同（{p.type}），將不修改");
                    }
                    return;
                }
            }
            controller.AddParameter(name, type);
        }

        /// <summary>
        /// 解析某個 state name 對應的 AnimationClip：
        ///   1. 若有 unity-chan 對應的 FBX，從 FBX 載入該 clip（不在硬碟上新建檔案）。
        ///   2. 否則退回到 Assets/Animations/&lt;State&gt;.anim 的 placeholder clip。
        /// </summary>
        private static AnimationClip ResolveClip(string stateName, bool loop, bool unityChanAvailable)
        {
            if (unityChanAvailable && UnityChanClipMap.TryGetValue(stateName, out string fbxBaseName))
            {
                string fbxPath = $"{UnityChanAnimFolder}/{fbxBaseName}.fbx";
                var clip = LoadFirstClipFromFbx(fbxPath);
                if (clip != null)
                {
                    Debug.Log($"[AnimatorSetupEditor] {stateName} ← unity-chan clip {clip.name} (from {fbxPath})");
                    return clip;
                }
                Debug.LogWarning($"[AnimatorSetupEditor] 找不到 unity-chan FBX {fbxPath}，退回 placeholder");
            }
            return EnsurePlaceholderClip(stateName, loop);
        }

        /// <summary>從 .fbx 取出第一個非預覽 AnimationClip。FBX 不存在則回傳 null。</summary>
        private static AnimationClip LoadFirstClipFromFbx(string fbxPath)
        {
            if (!File.Exists(fbxPath)) return null;
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var a in assets)
            {
                if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }
            return null;
        }

        private static AnimationClip EnsurePlaceholderClip(string clipName, bool loop = false)
        {
            string path = $"{AnimationsFolder}/{clipName}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = clipName };
                // Placeholder：放一個極短的虛擬曲線，避免 Unity 警告 "AnimationClip is empty"
                var curve = AnimationCurve.Linear(0f, 0f, 1f, 0f);
                clip.SetCurve("", typeof(GameObject), "m_IsActive", curve);
                AssetDatabase.CreateAsset(clip, path);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
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
