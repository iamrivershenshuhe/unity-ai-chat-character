// =====================================================================
// CameraRig.cs
// 主鏡頭的 zone-aware 控制器。提供 SwitchTo(pos, eulerAngles, fov, duration)
// 以 smoothstep 平滑切換到目標機位。
// =====================================================================
using System.Collections;
using UnityEngine;

namespace AIAgentChat.Cinematics
{
    [DisallowMultipleComponent]
    public class CameraRig : MonoBehaviour
    {
        [Tooltip("受控的相機。留空會自動取 Camera.main 或同物件上的 Camera。")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("預設切換時長（秒）")]
        [SerializeField] private float defaultDuration = 1.2f;

        [Header("Follow Mode")]
        [Tooltip("跟拍時相對於 path 方向的 offset：x=右側偏移、y=高度、z=後方距離（負值=後方）")]
        [SerializeField] private Vector3 followOffset = new Vector3(0.8f, 1.8f, -2.8f);

        [Tooltip("跟拍時相機看向 target 的高度（公尺，相對 target 腳底）")]
        [SerializeField] private float followLookAtHeight = 1.15f;

        [Tooltip("跟拍時相機是否要 smooth damping 而不是瞬間貼上 target")]
        [SerializeField] private bool followSmoothing = true;

        [Tooltip("Smooth damping 時間（秒）")]
        [SerializeField] private float followSmoothTime = 0.18f;

        private Coroutine activeMove;
        private Transform followTarget;
        private Vector3 followWorldOffset; // 以 path 方向算好的 world-space offset
        private bool followActive;
        private Vector3 followVel;          // SmoothDamp 用

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
                if (targetCamera == null) targetCamera = Camera.main;
            }
        }

        /// <summary>切換相機到目標機位。duration &lt;= 0 會用 defaultDuration。會關閉 follow 模式。</summary>
        public void SwitchTo(Vector3 position, Vector3 eulerAngles, float fov, float duration = -1f)
        {
            if (targetCamera == null)
            {
                Debug.LogWarning("[CameraRig] 找不到目標相機");
                return;
            }
            StopFollow();
            float d = duration > 0f ? duration : defaultDuration;
            if (activeMove != null) StopCoroutine(activeMove);
            activeMove = StartCoroutine(MoveCoroutine(position, Quaternion.Euler(eulerAngles), fov, d));
        }

        /// <summary>
        /// 啟動跟拍模式。相機會持續跟在 target 的「path 方向後方」並看向 target chest。
        /// pathStart / pathEnd 用來決定 camera 在哪一側（後方）— 跟拍中不會旋轉。
        /// </summary>
        public void StartFollow(Transform target, Vector3 pathStart, Vector3 pathEnd)
        {
            if (target == null || targetCamera == null) return;

            // 路徑方向（忽略 y）— 相機放在路徑的反向（target 之後）
            Vector3 pathDir = pathEnd - pathStart;
            pathDir.y = 0f;
            if (pathDir.sqrMagnitude < 1e-4f)
            {
                pathDir = target.forward;
                pathDir.y = 0f;
                if (pathDir.sqrMagnitude < 1e-4f) pathDir = Vector3.forward;
            }
            pathDir.Normalize();
            Vector3 rightDir = Vector3.Cross(Vector3.up, pathDir);

            // world-space offset：x 軸 = 路徑右側，z 軸 = 路徑前向（負值即後方）
            followWorldOffset = rightDir * followOffset.x
                                + Vector3.up * followOffset.y
                                + pathDir * followOffset.z;

            // 停掉任何進行中的 lerp
            if (activeMove != null) { StopCoroutine(activeMove); activeMove = null; }

            followTarget = target;
            followActive = true;
            followVel = Vector3.zero;

            // 立即把相機 snap 到 follow 位置一次（避免第一幀還在舊位置）
            UpdateFollowPose(forceSnap: true);
        }

        public void StopFollow()
        {
            followActive = false;
            followTarget = null;
        }

        private void LateUpdate()
        {
            if (!followActive || followTarget == null || targetCamera == null) return;
            UpdateFollowPose(forceSnap: false);
        }

        private void UpdateFollowPose(bool forceSnap)
        {
            Vector3 desired = followTarget.position + followWorldOffset;
            var camT = targetCamera.transform;

            if (forceSnap || !followSmoothing)
            {
                camT.position = desired;
                followVel = Vector3.zero;
            }
            else
            {
                camT.position = Vector3.SmoothDamp(camT.position, desired, ref followVel, followSmoothTime);
            }
            // 看向 target chest
            Vector3 lookAt = followTarget.position + Vector3.up * followLookAtHeight;
            camT.LookAt(lookAt);
        }

        private IEnumerator MoveCoroutine(Vector3 toPos, Quaternion toRot, float toFov, float duration)
        {
            var t = targetCamera.transform;
            Vector3 fromPos = t.position;
            Quaternion fromRot = t.rotation;
            float fromFov = targetCamera.fieldOfView;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                // smoothstep
                u = u * u * (3f - 2f * u);
                t.position = Vector3.Lerp(fromPos, toPos, u);
                t.rotation = Quaternion.Slerp(fromRot, toRot, u);
                targetCamera.fieldOfView = Mathf.Lerp(fromFov, toFov, u);
                yield return null;
            }

            t.position = toPos;
            t.rotation = toRot;
            targetCamera.fieldOfView = toFov;
            activeMove = null;
        }
    }
}
