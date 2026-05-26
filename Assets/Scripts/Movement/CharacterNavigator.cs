// =====================================================================
// CharacterNavigator.cs
// 控制 unity-chan 從目前位置走到目標 anchor。
//   - 不使用 NavMesh：直接走直線（房間沒有大型障礙物）
//   - 先轉向 → 再走 → 抵達後校正到目標 yaw
//   - 過程中觸發 OnPathStarted / OnPathProgress / OnPathArrived 事件
// =====================================================================
using System;
using System.Collections;
using UnityEngine;

namespace AIAgentChat.Movement
{
    [DisallowMultipleComponent]
    public class CharacterNavigator : MonoBehaviour
    {
        [Header("移動")]
        [Tooltip("移動速度（公尺/秒）")]
        [SerializeField] private float moveSpeed = 1.6f;

        [Tooltip("轉向速度（度/秒）")]
        [SerializeField] private float turnSpeed = 360f;

        [Tooltip("抵達目標的容忍距離")]
        [SerializeField] private float arrivalThreshold = 0.05f;

        [Header("動畫綁定")]
        [Tooltip("角色的 CharacterAnimatorController（用於播 Walk / Idle）。留空會 GetComponent。")]
        [SerializeField] private AIAgentChat.CharacterAnimatorController animController;

        /// <summary>移動開始時觸發（startPos, endPos）。</summary>
        public event Action<Vector3, Vector3> OnPathStarted;

        /// <summary>每幀回報目前進度（0..1）。</summary>
        public event Action<float> OnPathProgress;

        /// <summary>抵達目標時觸發。</summary>
        public event Action OnPathArrived;

        private Coroutine activeMove;
        private bool isMoving;
        public bool IsMoving => isMoving;

        private void Awake()
        {
            if (animController == null)
            {
                animController = GetComponent<AIAgentChat.CharacterAnimatorController>();
            }

            // 強制關 root motion — Walk clip 含 root motion 會把人推離我們設的 transform.position
            var anim = GetComponent<Animator>();
            if (anim != null) anim.applyRootMotion = false;

            // Rigidbody 若非 kinematic，物理會在 FixedUpdate 把 transform.position 改回去
            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            // 確保有 Collider，否則寶箱的 trigger 偵測不到她
            if (GetComponent<Collider>() == null)
            {
                var cap = gameObject.AddComponent<CapsuleCollider>();
                cap.height = 1.4f;
                cap.radius = 0.3f;
                cap.center = new Vector3(0, 0.7f, 0);
            }

            // unity-chan 自帶腳本會搶走 Animator/位移控制，全部停用
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb == this || mb == animController) continue;
                string n = mb.GetType().Name;
                if (n == "UnityChanControlScriptWithRgidBody" || n == "IdleChanger")
                {
                    mb.enabled = false;
                }
            }
        }

        /// <summary>請求角色走到 targetPosition，到達後旋轉到 targetYaw。</summary>
        public void MoveTo(Vector3 targetPosition, float targetYaw)
        {
            if (activeMove != null) StopCoroutine(activeMove);
            activeMove = StartCoroutine(MoveCoroutine(targetPosition, targetYaw));
        }

        /// <summary>瞬移到目標（不播動畫，例如場景剛載入時）。</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private IEnumerator MoveCoroutine(Vector3 targetPos, float targetYaw)
        {
            isMoving = true;
            Vector3 startPos = transform.position;
            Debug.Log($"[CharacterNavigator] MoveTo: from {startPos} → {targetPos} (yaw {targetYaw}°)");
            // 包 try/catch — 訂閱者（如 TreasureChestSpawner）若噴例外不能打斷走路 coroutine
            try { OnPathStarted?.Invoke(startPos, targetPos); }
            catch (System.Exception e) { Debug.LogException(e); }

            // 1) 先轉向 — 朝目標方向旋轉直到對齊（不到 5 度）
            Vector3 flatDir = targetPos - transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 1e-4f)
            {
                Quaternion targetRot = Quaternion.LookRotation(flatDir);
                while (Quaternion.Angle(transform.rotation, targetRot) > 5f)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                    yield return null;
                }
            }

            // 2) 播 Walk
            animController?.PlayWalk(true);

            // 3) 直線走向目標
            float totalDist = Vector3.Distance(startPos, targetPos);
            while (true)
            {
                Vector3 cur = transform.position;
                Vector3 to = new Vector3(targetPos.x, cur.y, targetPos.z);
                float distLeft = Vector3.Distance(cur, to);
                if (distLeft <= arrivalThreshold) break;

                transform.position = Vector3.MoveTowards(cur, to, moveSpeed * Time.deltaTime);

                // 同時保持朝向目標（避免被外力推偏）
                Vector3 dir = to - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f)
                {
                    Quaternion want = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
                }

                float progress = totalDist > 0f ? 1f - (distLeft / totalDist) : 1f;
                OnPathProgress?.Invoke(Mathf.Clamp01(progress));
                yield return null;
            }

            // 4) 對齊到目標 yaw
            Quaternion finalRot = Quaternion.Euler(0f, targetYaw, 0f);
            while (Quaternion.Angle(transform.rotation, finalRot) > 1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, finalRot, turnSpeed * Time.deltaTime);
                yield return null;
            }

            // 5) 收尾
            animController?.PlayWalk(false);
            transform.position = new Vector3(targetPos.x, transform.position.y, targetPos.z);
            transform.rotation = finalRot;
            isMoving = false;
            OnPathArrived?.Invoke();
            activeMove = null;
        }

        public void StopImmediately()
        {
            if (activeMove != null) StopCoroutine(activeMove);
            activeMove = null;
            isMoving = false;
            animController?.PlayWalk(false);
        }
    }
}
