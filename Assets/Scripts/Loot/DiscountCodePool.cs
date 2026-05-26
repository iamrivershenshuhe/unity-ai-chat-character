// =====================================================================
// DiscountCodePool.cs
// ScriptableObject 池：在寶箱開啟時隨機抽出一組優惠碼。
// 若 codes 為空 → 程式自動生成 8 碼大寫字母+數字（不重複）。
// =====================================================================
using System.Collections.Generic;
using UnityEngine;

namespace AIAgentChat.Loot
{
    [CreateAssetMenu(fileName = "DiscountCodePool", menuName = "AIAgentChat/Discount Code Pool", order = 10)]
    public class DiscountCodePool : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public string code;
            [TextArea(1, 3)] public string description;
        }

        [Tooltip("預先設定的優惠碼。若為空會用 GenerateRandomCode 即時產生。")]
        public List<Entry> codes = new List<Entry>();

        private readonly HashSet<int> usedIndices = new HashSet<int>();

        public Entry DrawRandom()
        {
            if (codes == null || codes.Count == 0)
            {
                return new Entry
                {
                    code = GenerateRandomCode(8),
                    description = "限時優惠：本次對話可使用",
                };
            }

            // 不重複抽（用完才重置）
            if (usedIndices.Count >= codes.Count) usedIndices.Clear();
            int pick;
            int safety = 32;
            do
            {
                pick = Random.Range(0, codes.Count);
                safety--;
            } while (usedIndices.Contains(pick) && safety > 0);

            usedIndices.Add(pick);
            return codes[pick];
        }

        public static string GenerateRandomCode(int length)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 排除 I/O/0/1 易混
            var sb = new System.Text.StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append(alphabet[Random.Range(0, alphabet.Length)]);
            }
            return sb.ToString();
        }

        public void ResetUsage()
        {
            usedIndices.Clear();
        }
    }
}
