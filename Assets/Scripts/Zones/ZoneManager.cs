// =====================================================================
// ZoneManager.cs
// 4 個對話 zone 的總協調者：tab UI 觸發 → 角色走 → 相機切 → AI prompt 換 → UI 切。
// 不直接持有 UI panel；以事件 OnZoneActivated 通知 UI 自行切換。
// =====================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AIAgentChat.Cinematics;
using AIAgentChat.Movement;

namespace AIAgentChat.Zones
{
    [DisallowMultipleComponent]
    public class ZoneManager : MonoBehaviour
    {
        [Header("Zones")]
        [Tooltip("4 個 zone 定義 asset，依顯示順序填入")]
        [SerializeField] private List<ZoneDefinition> zones = new List<ZoneDefinition>();

        [Tooltip("啟動時預設啟用的 zone（清單中的 index）")]
        [SerializeField] private int startupZoneIndex = 0;

        [Header("Refs")]
        [SerializeField] private CharacterNavigator navigator;
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private AICharacterManager aiManager;

        /// <summary>觸發於：角色開始往新 zone 移動的瞬間。</summary>
        public event Action<ZoneDefinition> OnZoneTransitionStarted;

        /// <summary>觸發於：角色已抵達目標 zone 且 AI prompt 已套用。</summary>
        public event Action<ZoneDefinition> OnZoneActivated;

        public ZoneDefinition CurrentZone { get; private set; }
        public bool IsTransitioning { get; private set; }
        public IReadOnlyList<ZoneDefinition> Zones => zones;

        private void Start()
        {
            if (zones.Count == 0)
            {
                Debug.LogWarning("[ZoneManager] zones 未指派，ZoneManager 不會運作");
                return;
            }

            // 把角色與相機瞬移到起始 zone（避免一開場就走長路）
            int idx = Mathf.Clamp(startupZoneIndex, 0, zones.Count - 1);
            var initial = zones[idx];
            if (navigator != null)
            {
                navigator.Teleport(initial.anchorPosition, initial.anchorYaw);
            }
            if (cameraRig != null)
            {
                cameraRig.SwitchTo(initial.cameraPosition, initial.cameraEulerAngles, initial.cameraFOV, 0.001f);
            }
            CurrentZone = initial;
            aiManager?.ApplyZone(initial);
            OnZoneActivated?.Invoke(initial);
        }

        /// <summary>切換到指定 zone（依 index）。</summary>
        public void RequestZone(int index)
        {
            if (index < 0 || index >= zones.Count) return;
            RequestZone(zones[index]);
        }

        /// <summary>切換到指定 zone。</summary>
        public void RequestZone(ZoneDefinition target)
        {
            if (target == null) return;
            if (IsTransitioning) return;
            if (target == CurrentZone) return;
            StartCoroutine(TransitionCoroutine(target));
        }

        private IEnumerator TransitionCoroutine(ZoneDefinition target)
        {
            IsTransitioning = true;
            OnZoneTransitionStarted?.Invoke(target);

            bool arrived = false;
            void Handler() { arrived = true; }

            if (navigator != null)
            {
                // 1) 相機進入跟拍模式 — 路徑方向 = 起點 → 終點
                Vector3 pathStart = navigator.transform.position;
                Vector3 pathEnd = target.anchorPosition;
                cameraRig?.StartFollow(navigator.transform, pathStart, pathEnd);

                // 2) 走過去
                navigator.OnPathArrived += Handler;
                navigator.MoveTo(target.anchorPosition, target.anchorYaw);

                float waited = 0f;
                const float maxWait = 25f;
                while (!arrived && waited < maxWait)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
                navigator.OnPathArrived -= Handler;

                if (!arrived)
                {
                    Debug.LogWarning($"[ZoneManager] Navigator 超時未到達 {target.displayName}，強制 teleport。");
                    navigator.StopImmediately();
                    navigator.Teleport(target.anchorPosition, target.anchorYaw);
                }
            }

            // 3) 抵達後 — 關閉跟拍、lerp 到 zone 固定機位
            cameraRig?.SwitchTo(target.cameraPosition, target.cameraEulerAngles, target.cameraFOV);

            CurrentZone = target;
            aiManager?.ApplyZone(target);
            OnZoneActivated?.Invoke(target);
            IsTransitioning = false;
        }
    }
}
