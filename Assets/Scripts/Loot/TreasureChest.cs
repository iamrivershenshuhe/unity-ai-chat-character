// =====================================================================
// TreasureChest.cs
// 由 spawner 放在地上的金色寶箱。unity-chan 走進 trigger 後自動開箱，
// 顯示優惠碼於聊天視窗。寶箱本身會輕微 idle 漂浮+旋轉提示玩家走過來。
//
// 注意：collider 應設為 isTrigger=true；unity-chan 需有 Collider + Rigidbody
// （CharacterNavigator.Awake 會確保這兩個都存在）才會觸發 OnTriggerEnter。
// =====================================================================
using System;
using System.Collections;
using UnityEngine;
using AIAgentChat.Movement;

namespace AIAgentChat.Loot
{
    public class TreasureChest : MonoBehaviour
    {
        [Header("動畫")]
        [Tooltip("開箱時上蓋會抬起的角度（沿 X 軸）")]
        [SerializeField] private float openAngle = -75f;

        [Tooltip("開箱動畫秒數")]
        [SerializeField] private float openDuration = 0.5f;

        [Tooltip("開箱後寶箱多久淡出消失（秒）。0 = 不消失。")]
        [SerializeField] private float fadeOutAfter = 4f;

        [Header("Idle 漂浮提示")]
        [Tooltip("idle 時上下漂浮幅度（公尺）")]
        [SerializeField] private float bobAmplitude = 0.08f;

        [Tooltip("idle 漂浮頻率（Hz）")]
        [SerializeField] private float bobFrequency = 1.2f;

        [Tooltip("idle 旋轉速度（度/秒）")]
        [SerializeField] private float spinSpeed = 30f;

        [Header("撞擊")]
        [Tooltip("撞擊後是否要短暫的 squash 效果（讓玩家感受到撞到了）")]
        [SerializeField] private bool playImpactSquash = true;

        [Header("可視化")]
        [Tooltip("會被旋轉的上蓋 Transform；留空則旋轉自己。")]
        [SerializeField] private Transform lid;

        [Tooltip("開箱時 spawn 的特效 prefab（可選）")]
        [SerializeField] private GameObject openVFX;

        public event Action<TreasureChest> OnOpened;

        public DiscountCodePool.Entry DrawnCode { get; private set; }
        public bool IsOpened { get; private set; }

        private Vector3 idleBasePos;
        private float idleTime;

        public void Configure(DiscountCodePool pool)
        {
            DrawnCode = pool != null ? pool.DrawRandom() : null;
        }

        private void Start()
        {
            idleBasePos = transform.position;
        }

        private void Update()
        {
            if (IsOpened) return;
            // Idle bob + spin — 讓寶箱明顯一點，提示 unity-chan 走過來
            idleTime += Time.deltaTime;
            float y = Mathf.Sin(idleTime * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            transform.position = idleBasePos + new Vector3(0, y, 0);
            transform.Rotate(0, spinSpeed * Time.deltaTime, 0, Space.World);
        }

        /// <summary>unity-chan 走進 trigger → 自動開箱。</summary>
        private void OnTriggerEnter(Collider other)
        {
            if (IsOpened) return;
            // 只認 unity-chan（透過 CharacterNavigator 識別，不依賴 tag 設定）
            if (other.GetComponent<CharacterNavigator>() == null
                && other.GetComponentInParent<CharacterNavigator>() == null) return;
            TryOpen();
        }

        public void TryOpen()
        {
            if (IsOpened) return;
            IsOpened = true;
            StartCoroutine(OpenCoroutine());
        }

        private IEnumerator OpenCoroutine()
        {
            // 0) 把 idle 漂浮的偏移歸位，避免動畫期間還在浮動
            transform.position = idleBasePos;

            // 1) 撞擊 squash — 短暫壓扁再彈回
            if (playImpactSquash)
            {
                Vector3 original = transform.localScale;
                float t = 0f;
                const float squashDur = 0.18f;
                while (t < squashDur)
                {
                    t += Time.deltaTime;
                    float u = t / squashDur;
                    // 0→1→0 形狀的彈跳
                    float bounce = 4f * u * (1f - u);
                    transform.localScale = new Vector3(
                        original.x * (1f + 0.25f * bounce),
                        original.y * (1f - 0.3f * bounce),
                        original.z * (1f + 0.25f * bounce));
                    yield return null;
                }
                transform.localScale = original;
            }

            // 2) 開蓋
            Transform target = lid != null ? lid : transform;
            Quaternion from = target.localRotation;
            Quaternion to = from * Quaternion.Euler(openAngle, 0, 0);
            float t2 = 0f;
            while (t2 < openDuration)
            {
                t2 += Time.deltaTime;
                float u = Mathf.Clamp01(t2 / openDuration);
                u = 1f - Mathf.Pow(1f - u, 3f); // ease-out cubic
                target.localRotation = Quaternion.Slerp(from, to, u);
                yield return null;
            }
            target.localRotation = to;

            if (openVFX != null)
            {
                Instantiate(openVFX, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            }

            OnOpened?.Invoke(this);

            if (fadeOutAfter > 0f)
            {
                yield return new WaitForSeconds(fadeOutAfter);
                Destroy(gameObject);
            }
        }
    }
}
