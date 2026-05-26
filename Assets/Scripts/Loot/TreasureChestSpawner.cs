// =====================================================================
// TreasureChestSpawner.cs
// 每 spawnInterval 秒在中央安全區生一個寶箱（固定位置等 unity-chan 撞）。
// 場上同時只有一個寶箱；玩家撞開後 spawnInterval 秒會再生一個。
// =====================================================================
using System.Collections;
using UnityEngine;
using AIAgentChat.Movement;

namespace AIAgentChat.Loot
{
    [DisallowMultipleComponent]
    public class TreasureChestSpawner : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CharacterNavigator navigator;  // 保留欄位以維持 WireUp 相容
        [SerializeField] private ChatUIManager chatUI;
        [SerializeField] private DiscountCodePool codePool;

        [Header("Prefab")]
        [Tooltip("寶箱 prefab。留空 → 程式自動生成簡易金色 Cube。")]
        [SerializeField] private GameObject chestPrefab;

        [Tooltip("寶箱基準位置 Y（地板高度）")]
        [SerializeField] private float chestBaseY = 0f;

        [Header("生成規則")]
        [Tooltip("每隔幾秒生一個寶箱（從上一個被撞開算起；初始也等這麼久）")]
        [SerializeField] private float spawnInterval = 20f;

        [Tooltip("生成的隨機 X 範圍（中央安全區）")]
        [SerializeField] private Vector2 spawnXRange = new Vector2(-2f, 2f);

        [Tooltip("生成的隨機 Z 範圍")]
        [SerializeField] private Vector2 spawnZRange = new Vector2(-2f, 2f);

        [Tooltip("與 unity-chan 當前位置最小距離（避免一生出來就被撞）")]
        [SerializeField] private float minDistFromCharacter = 1.5f;

        private TreasureChest currentChest;
        private Coroutine spawnLoop;

        private void OnEnable()
        {
            if (spawnLoop != null) StopCoroutine(spawnLoop);
            spawnLoop = StartCoroutine(SpawnLoop());
        }

        private void OnDisable()
        {
            if (spawnLoop != null) StopCoroutine(spawnLoop);
        }

        private IEnumerator SpawnLoop()
        {
            // 初始等 spawnInterval 秒（不要一進場就有寶箱）
            yield return new WaitForSeconds(spawnInterval);

            while (true)
            {
                // 如果場上沒寶箱 → 生一個
                if (currentChest == null)
                {
                    SpawnChestAtRandomPosition();
                }

                // 等到目前這個寶箱被開（或被銷毀）
                while (currentChest != null && !currentChest.IsOpened)
                {
                    yield return null;
                }

                // 等 spawnInterval 後再生下一個
                yield return new WaitForSeconds(spawnInterval);
                currentChest = null; // 上一個已經消失了，準備生新的
            }
        }

        private void SpawnChestAtRandomPosition()
        {
            Vector3 pos = PickSpawnPosition();
            SpawnChestAt(pos);
        }

        private Vector3 PickSpawnPosition()
        {
            const int maxAttempts = 8;
            Vector3 charPos = navigator != null ? navigator.transform.position : Vector3.zero;
            for (int i = 0; i < maxAttempts; i++)
            {
                float x = Random.Range(spawnXRange.x, spawnXRange.y);
                float z = Random.Range(spawnZRange.x, spawnZRange.y);
                Vector3 p = new Vector3(x, chestBaseY, z);
                if (Vector3.Distance(new Vector3(charPos.x, chestBaseY, charPos.z), p) >= minDistFromCharacter)
                {
                    return p;
                }
            }
            // 試了幾次都太近 → 直接放對角線位置
            return new Vector3(-charPos.x * 0.5f, chestBaseY, -charPos.z * 0.5f);
        }

        private void SpawnChestAt(Vector3 pos)
        {
            GameObject go;
            if (chestPrefab != null)
            {
                go = Object.Instantiate(chestPrefab, pos, Quaternion.Euler(0, Random.Range(0, 360f), 0));
            }
            else
            {
                go = BuildFallbackChest(pos);
            }

            currentChest = go.GetComponent<TreasureChest>();
            if (currentChest == null) currentChest = go.AddComponent<TreasureChest>();
            currentChest.Configure(codePool);
            currentChest.OnOpened += HandleChestOpened;

            Debug.Log($"[TreasureChestSpawner] 寶箱生成於 {pos}（等 unity-chan 走過來撞）");
        }

        private GameObject BuildFallbackChest(Vector3 pos)
        {
            // 簡易寶箱：底箱 (大金色) + 上蓋 (子物件，便於旋轉)
            var root = new GameObject("TreasureChest");
            root.transform.position = pos;

            // root 需要 Trigger Collider，OnTriggerEnter 才能 fire
            var rootCol = root.AddComponent<BoxCollider>();
            rootCol.center = new Vector3(0, 0.35f, 0);
            rootCol.size = new Vector3(0.9f, 0.8f, 0.7f);
            rootCol.isTrigger = true;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.6f, 0.4f, 0.45f);
            body.transform.localPosition = new Vector3(0, 0.2f, 0);
            ApplyGoldMat(body, new Color(0.95f, 0.7f, 0.25f), emission: 1.2f);

            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "Lid";
            lid.transform.SetParent(root.transform, false);
            lid.transform.localScale = new Vector3(0.62f, 0.12f, 0.47f);
            // pivot wrapper — 旋轉中心在上蓋後緣
            var lidWrapper = new GameObject("LidPivot").transform;
            lidWrapper.SetParent(root.transform, false);
            lidWrapper.localPosition = new Vector3(0, 0.4f, -0.225f);
            lid.transform.SetParent(lidWrapper, true);
            lid.transform.localPosition = new Vector3(0, 0.06f, 0.225f);
            ApplyGoldMat(lid, new Color(1f, 0.78f, 0.35f), emission: 1.6f);

            // body / lid 內部的 mesh collider 全部清掉（root 的 trigger 已負責偵測）
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                if (c != rootCol) Object.Destroy(c);
            }

            // 點光突顯
            var lightGo = new GameObject("ChestGlow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 0.7f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.85f, 0.45f);
            l.intensity = 1.8f;
            l.range = 3f;
            l.shadows = LightShadows.None;

            // 配置 TreasureChest 使用 lidPivot 旋轉
            var chest = root.AddComponent<TreasureChest>();
            if (chest != null)
            {
                var soField = typeof(TreasureChest).GetField("lid",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (soField != null) soField.SetValue(chest, lidWrapper);
            }

            return root;
        }

        private static void ApplyGoldMat(GameObject go, Color baseColor, float emission)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var m = new Material(Shader.Find("Standard"));
            m.SetColor("_Color", baseColor);
            m.SetFloat("_Metallic", 0.85f);
            m.SetFloat("_Glossiness", 0.7f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", baseColor * emission);
            r.material = m;
        }

        private void HandleChestOpened(TreasureChest chest)
        {
            if (chest.DrawnCode != null && chatUI != null)
            {
                string desc = string.IsNullOrEmpty(chest.DrawnCode.description)
                    ? string.Empty
                    : $"\n{chest.DrawnCode.description}";
                chatUI.AppendAIMessage($"🎁 你獲得優惠碼：<b>{chest.DrawnCode.code}</b>{desc}");
            }
            chest.OnOpened -= HandleChestOpened;
            // currentChest 保留參考直到它真的被 Destroy，SpawnLoop 會判斷 IsOpened 並倒數下一個
        }
    }
}
