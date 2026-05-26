// =====================================================================
// ZoneOfficeSetupEditor.cs
// Editor Tool：在 LuxuryRoom 內建立 4 個象限的辦公室家具，並產生對應的
// ZoneDefinition assets + DiscountCodePool asset。
//
// 4 個 zone：
//   Q1（前左, -X -Z） 一般閒聊區     → 矮沙發 + 茶几 + 木地毯
//   Q2（前右, +X -Z） 產品 AI 問答區 → 木展示台 + 2 個彩色商品 props
//   Q3（後左, -X +Z） 商品許願投票區 → 投票看板 + 高腳椅 + 木格柵牆飾
//   Q4（後右, +X +Z） AI 個性化推薦區 → Google 風辦公桌 + 椅 + 螢幕 + 綠植
//
// 設計準則：
//   - 木質點綴 #A87651（暖橡木）
//   - 高彩度限定 3 色（青綠 #4ECDC4、珊瑚紅 #FF6B6B、亮黃 #FFD93D），每 zone 用 1~2 色
//   - 白色主體 #F5F5F5
//
// 選單位置：Tools > Build Zones + Office Furniture
// =====================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AIAgentChat.Zones;
using AIAgentChat.Loot;

namespace AIAgentChat.EditorTools
{
    public static class ZoneOfficeSetupEditor
    {
        private const string FurnitureRootName = "OfficeFurniture";
        private const string ZoneAssetFolder = "Assets/AIAgentChat/Zones";
        private const string LootAssetFolder = "Assets/AIAgentChat/Loot";
        private const string MaterialsFolder = "Assets/Materials/Office";

        // 設計色票
        private static readonly Color WoodOak    = new Color(0.66f, 0.46f, 0.32f);
        private static readonly Color WoodDark   = new Color(0.42f, 0.28f, 0.18f);
        private static readonly Color White      = new Color(0.96f, 0.96f, 0.96f);
        private static readonly Color AccentTeal = new Color(0.31f, 0.80f, 0.77f); // #4ECDC4
        private static readonly Color AccentRed  = new Color(1.00f, 0.42f, 0.42f); // #FF6B6B
        private static readonly Color AccentYel  = new Color(1.00f, 0.85f, 0.24f); // #FFD93D
        private static readonly Color PlantGreen = new Color(0.32f, 0.56f, 0.32f);

        // 房間 half-size（與 LuxuryRoomSetupEditor 同步）
        private const float HalfW = 10f;
        private const float HalfD = 7.5f;

        // 每個 zone 的中心點與角色站立點。anchorYaw/cameraEuler 由 BuildAll 用 LookRotation 計算。
        private struct ZoneSpec
        {
            public string id;
            public string display;
            public Vector3 anchorPos;
            public Vector3 cameraPos;
            public Vector3 quadrantCenter; // 用於擺家具
            public ZoneUIMode uiMode;
            public Color accent;
            public string systemPrompt;
        }

        // unity-chan 視線高度（鏡頭看向她的 chest）
        private const float LookAtHeight = 1.15f;

        private static readonly ZoneSpec[] Specs = new[]
        {
            new ZoneSpec
            {
                // 家具靠牆 — anchor 在中央留出走廊空間，camera 對面，家具在 unity-chan 背後
                id = "chat", display = "一般閒聊",
                anchorPos = new Vector3(-3.0f, 0f, -1.5f),
                cameraPos = new Vector3(2.0f, 1.6f, -1.5f),
                quadrantCenter = new Vector3(-9f, 0f, -1.5f), // 沙發中心（靠左牆）
                uiMode = ZoneUIMode.Chat,
                accent = AccentTeal,
                systemPrompt =
                    "你是一個友善的虛擬代言人，目前在豪華沙發休息區陪伴使用者輕鬆閒聊。" +
                    "用溫暖、親切但不浮誇的口吻回應日常話題，盡量短而自然。" +
                    "每一次回覆都必須嚴格使用以下 JSON：" +
                    "{\"emotion\": \"happy|sad|thinking|greeting|neutral|surprised|angry\", \"message\": \"...\"}" +
                    " 回答語言：繁體中文",
            },
            new ZoneSpec
            {
                id = "qa", display = "產品問答",
                anchorPos = new Vector3(1.5f, 0f, -3.0f),
                cameraPos = new Vector3(1.5f, 1.6f, 2.0f),
                quadrantCenter = new Vector3(3f, 0f, -6.5f), // 展示桌中心（靠前牆）
                uiMode = ZoneUIMode.ProductQA,
                accent = AccentRed,
                systemPrompt =
                    "你是專業的產品顧問 AI，目前在產品展示區。" +
                    "使用者會詢問產品的規格、適用情境、價格區間、保固。" +
                    "回答必須條理清晰、列點，不誇大。若不確定，誠實說「我可以幫你查」。" +
                    "每一次回覆必須使用 JSON：{\"emotion\": \"...\", \"message\": \"...\"} 回答語言：繁體中文",
            },
            new ZoneSpec
            {
                id = "wish", display = "許願投票",
                anchorPos = new Vector3(-1.5f, 0f, 3.0f),
                cameraPos = new Vector3(-1.5f, 1.6f, -2.0f),
                quadrantCenter = new Vector3(-3f, 0f, 7f), // 投票板貼後牆
                uiMode = ZoneUIMode.WishVoting,
                accent = AccentYel,
                systemPrompt =
                    "你是社群許願版的主持人 AI，正在收集使用者對品牌想要看到的新商品提案。" +
                    "看到許願時要表現熱情，總結對方的需求並可以反問細節（例如顏色、材質、價位）。" +
                    "當使用者按下「我也要 +1」時，要稱讚他的眼光並補充其他人喜歡這個提案的原因。" +
                    "每一次回覆必須使用 JSON：{\"emotion\": \"...\", \"message\": \"...\"} 回答語言：繁體中文",
            },
            new ZoneSpec
            {
                id = "recommend", display = "個性化推薦",
                anchorPos = new Vector3(1.5f, 0f, 3.0f),
                cameraPos = new Vector3(1.5f, 1.6f, -2.0f),
                quadrantCenter = new Vector3(3f, 0f, 6.5f), // 辦公桌貼後牆
                uiMode = ZoneUIMode.Recommendation,
                accent = AccentTeal,
                systemPrompt =
                    "你是貼心的個性化推薦 AI，背景設定是在現代辦公室裡的私人顧問。" +
                    "根據前面對話的線索（風格、預算、用途）推薦商品，每次推薦 1~2 個。" +
                    "口吻像認識使用者的老朋友：『我覺得你會喜歡 …，因為你之前提到 …』。" +
                    "每一次回覆必須使用 JSON：{\"emotion\": \"...\", \"message\": \"...\"} 回答語言：繁體中文",
            },
        };

        /// <summary>從 anchorPos 與 cameraPos 計算 anchorYaw（角色面向相機）與 cameraEuler（鏡頭看向角色 chest）。</summary>
        public static (float anchorYaw, Vector3 cameraEuler) ComputeFraming(Vector3 anchor, Vector3 camera)
        {
            // 角色 yaw：面向相機的水平方向
            Vector3 toCam = camera - anchor;
            toCam.y = 0f;
            float yaw = toCam.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toCam).eulerAngles.y
                : 0f;

            // 相機 euler：看向角色 chest（anchor + LookAtHeight）
            Vector3 lookTarget = anchor + Vector3.up * LookAtHeight;
            Vector3 toAnchor = lookTarget - camera;
            Vector3 euler = toAnchor.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toAnchor).eulerAngles
                : Vector3.zero;
            return (yaw, euler);
        }

        [MenuItem("Tools/Build Zones + Office Furniture")]
        public static void BuildAll()
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(ZoneAssetFolder);
            EnsureFolder(LootAssetFolder);

            // 1) 建立 ZoneDefinition assets — yaw / euler 用 LookRotation 計算
            var zoneAssets = new List<ZoneDefinition>();
            foreach (var s in Specs)
            {
                var asset = LoadOrCreate<ZoneDefinition>($"{ZoneAssetFolder}/Zone_{s.id}.asset");
                var (yaw, euler) = ComputeFraming(s.anchorPos, s.cameraPos);
                asset.zoneId = s.id;
                asset.displayName = s.display;
                asset.anchorPosition = s.anchorPos;
                asset.anchorYaw = yaw;
                asset.cameraPosition = s.cameraPos;
                asset.cameraEulerAngles = euler;
                asset.cameraFOV = 50f;
                asset.uiMode = s.uiMode;
                asset.systemPrompt = s.systemPrompt;
                asset.tabAccentColor = s.accent;
                EditorUtility.SetDirty(asset);
                zoneAssets.Add(asset);
            }

            // 2) 建立 DiscountCodePool（若不存在）
            var pool = LoadOrCreate<DiscountCodePool>($"{LootAssetFolder}/DiscountCodePool.asset");
            if (pool.codes.Count == 0)
            {
                pool.codes.Add(new DiscountCodePool.Entry { code = "WELCOME10", description = "首次互動 9 折優惠" });
                pool.codes.Add(new DiscountCodePool.Entry { code = "VIP-GOLD", description = "尊榮會員 8 折優惠" });
                pool.codes.Add(new DiscountCodePool.Entry { code = "WISH-2026", description = "許願商品上架時 7 折早鳥優惠" });
                pool.codes.Add(new DiscountCodePool.Entry { code = "TALK-NOW", description = "本次對話內可使用的免運券" });
                EditorUtility.SetDirty(pool);
            }

            // 3) 建立 / 重建家具
            var existing = GameObject.Find(FurnitureRootName);
            if (existing != null) Object.DestroyImmediate(existing);
            var root = new GameObject(FurnitureRootName);
            root.transform.position = Vector3.zero;

            BuildChatZone(root.transform, Specs[0]);
            BuildQAZone(root.transform, Specs[1]);
            BuildWishVotingZone(root.transform, Specs[2]);
            BuildOfficeZone(root.transform, Specs[3]);

            // 4) 在每個 zone 中心放一個 ZoneAnchor 空 GO，便於 Inspector 視覺化
            foreach (var s in Specs)
            {
                var (yaw, _) = ComputeFraming(s.anchorPos, s.cameraPos);
                var anchor = new GameObject($"ZoneAnchor_{s.id}");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.position = s.anchorPos;
                anchor.transform.rotation = Quaternion.Euler(0, yaw, 0);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "Build Zones + Office Furniture",
                    "完成！\n\n" +
                    $"- ZoneDefinition assets: {ZoneAssetFolder}/Zone_*.asset (4 個)\n" +
                    $"- DiscountCodePool: {LootAssetFolder}/DiscountCodePool.asset\n" +
                    "- 場景中已建立 'OfficeFurniture' 根物件（4 個象限家具）\n\n" +
                    "下一步：執行 Tools > Wire Zone System 在場景中安裝 ZoneManager / TabBar / Spawner。",
                    "OK");
            }
        }

        // =================================================================
        // Zone furniture — Q1 閒聊區 (-X, -Z)
        // =================================================================
        private static void BuildChatZone(Transform parent, ZoneSpec s)
        {
            var g = NewGroup(parent, $"Zone_{s.id}_Chat");
            // 沙發靠左牆（x≈-9.5），yaw=90 讓沙發背朝 -X、面朝 +X（房內）
            SpawnBPS(g, "Furniture/Living Room/Sofa_Apt_01.prefab",  new Vector3(-9.3f, 0f, -1.5f), 90f, "BPS_Sofa");
            // 茶几在沙發前方
            SpawnBPS(g, "Furniture/Living Room/Table_Coffee_01.prefab", new Vector3(-7.3f, 0f, -1.5f), 0f, "BPS_CoffeeTable");
            // 落地燈在沙發旁角落
            SpawnBPS(g, "Props/Lighting/Lamp_Floor_Apt_01.prefab", new Vector3(-9.3f, 0f, 1.5f), 0f, "BPS_FloorLamp");
            // 地毯
            SpawnBPS(g, "Props/Art/Rug_Apt_01.prefab", new Vector3(-7.3f, 0.01f, -1.5f), 90f, "BPS_Rug");

            // 補一顆暖光突顯
            AddPointLightAt(g, "ChatZone_AccentLight", new Vector3(-7.5f, 1.8f, -1.5f), AccentTeal, 1.2f, 5f);
        }

        // =================================================================
        // Zone furniture — Q2 產品 Q&A 區 (+X, -Z)
        // =================================================================
        private static void BuildQAZone(Transform parent, ZoneSpec s)
        {
            var g = NewGroup(parent, $"Zone_{s.id}_QA");
            // 展示桌靠前牆（z≈-7），yaw=0 桌面朝上 + 桌背靠牆
            SpawnBPS(g, "Furniture/Living Room/Table_Dining_Apt_01.prefab", new Vector3(3.0f, 0f, -6.7f), 0f, "BPS_DisplayTable");

            // 桌上放 3 個花瓶當「商品展示」— 高度依 dining table 約 0.75m
            SpawnBPS(g, "Props/Misc/Vase_Apt_01.prefab", new Vector3(1.9f, 0.78f, -6.7f), 0f, "BPS_Product_Vase1");
            SpawnBPS(g, "Props/Misc/Vase_Apt_03.prefab", new Vector3(3.0f, 0.78f, -6.7f), 0f, "BPS_Product_Vase2");
            SpawnBPS(g, "Props/Misc/CandelSetting_apt_01.prefab", new Vector3(4.1f, 0.78f, -6.7f), 0f, "BPS_Product_Candle");

            // 前牆掛畫（背景裝飾）— yaw=180 讓畫面朝房內（+Z）
            SpawnBPS(g, "Props/Art/Canvas_Painting_01.prefab", new Vector3(3.0f, 2.0f, -7.42f), 180f, "BPS_WallArt");

            // 地毯
            SpawnBPS(g, "Props/Art/Rug_Apt_02.prefab", new Vector3(3.0f, 0.01f, -5.5f), 0f, "BPS_Rug");

            AddPointLightAt(g, "QAZone_AccentLight", new Vector3(3.0f, 2.2f, -6.0f), AccentRed, 1.0f, 5f);
        }

        // =================================================================
        // Zone furniture — Q3 許願投票區 (-X, +Z)
        // =================================================================
        private static void BuildWishVotingZone(Transform parent, ZoneSpec s)
        {
            var g = NewGroup(parent, $"Zone_{s.id}_Wish");
            // 投票板用大型 wall art 貼後牆 (z=+7.4)，yaw=0 讓畫面朝 -Z（房內）
            // 註：投票板本身（含 3 顆心型）保留 cube 版以呈現 emissive 投票感
            NewCube(g, "VoteBoard_Frame", new Vector3(-3.0f, 1.6f, 7.40f), new Vector3(3.6f, 2.4f, 0.1f), WoodDark);
            NewCube(g, "VoteBoard_Panel", new Vector3(-3.0f, 1.6f, 7.34f), new Vector3(3.4f, 2.2f, 0.06f), White);
            NewCubeEmissive(g, "VoteHeart1", new Vector3(-4.1f, 2.2f, 7.29f), new Vector3(0.3f, 0.3f, 0.05f), AccentYel, 1.6f);
            NewCubeEmissive(g, "VoteHeart2", new Vector3(-3.0f, 1.6f, 7.29f), new Vector3(0.3f, 0.3f, 0.05f), AccentRed, 1.6f);
            NewCubeEmissive(g, "VoteHeart3", new Vector3(-1.9f, 1.0f, 7.29f), new Vector3(0.3f, 0.3f, 0.05f), AccentTeal, 1.6f);

            // 板兩側用 BPS 掛畫點綴
            SpawnBPS(g, "Props/Art/Canvas_Painting_02.prefab", new Vector3(-5.5f, 1.6f, 7.42f), 180f, "BPS_WallArt_L");
            SpawnBPS(g, "Props/Art/Canvas_Painting_03.prefab", new Vector3(-0.5f, 1.6f, 7.42f), 180f, "BPS_WallArt_R");

            // 高腳桌（迎賓）用 BPS side table
            SpawnBPS(g, "Furniture/Living Room/Table_Side_Apt_01.prefab", new Vector3(-3.0f, 0f, 5.5f), 0f, "BPS_WishTable");

            // 椅子 x 2（迎賓桌兩側）
            SpawnBPS(g, "Furniture/Living Room/Chair_Apt_01.prefab", new Vector3(-4.2f, 0f, 5.5f), 90f, "BPS_Chair_L");
            SpawnBPS(g, "Furniture/Living Room/Chair_Apt_01.prefab", new Vector3(-1.8f, 0f, 5.5f), -90f, "BPS_Chair_R");

            // 地毯
            SpawnBPS(g, "Props/Art/Rug_Apt_01.prefab", new Vector3(-3.0f, 0.01f, 5.5f), 0f, "BPS_Rug");

            AddPointLightAt(g, "WishZone_AccentLight", new Vector3(-3.0f, 2.4f, 6.5f), AccentYel, 1.4f, 5f);
        }

        // =================================================================
        // Zone furniture — Q4 Google 風辦公室 / 推薦區 (+X, +Z)
        // =================================================================
        private static void BuildOfficeZone(Transform parent, ZoneSpec s)
        {
            var g = NewGroup(parent, $"Zone_{s.id}_Office");
            // 電腦桌一組（含螢幕/鍵盤）貼後牆 (z≈6.5)，yaw=180 讓桌背朝 +Z 牆
            SpawnBPS(g, "Furniture/Living Room/Table_Computer_01_Setup.prefab", new Vector3(3.0f, 0f, 6.7f), 180f, "BPS_OfficeDesk");

            // 椅子（在桌前方，面朝 +Z 也就是面朝桌）
            SpawnBPS(g, "Furniture/Living Room/Chair_Apt_01.prefab", new Vector3(3.0f, 0f, 5.6f), 0f, "BPS_Chair");

            // 後牆掛畫（背景）
            SpawnBPS(g, "Props/Art/WallArt_Apt_01.prefab", new Vector3(3.0f, 2.4f, 7.42f), 180f, "BPS_WallArt");

            // 桌邊落地燈
            SpawnBPS(g, "Props/Lighting/Lamp_Floor_Apt_02.prefab", new Vector3(5.5f, 0f, 6.5f), 0f, "BPS_FloorLamp");

            // 角落花瓶
            SpawnBPS(g, "Props/Misc/Vase_Apt_03.prefab", new Vector3(1.0f, 0f, 6.5f), 0f, "BPS_CornerVase");

            // 地毯
            SpawnBPS(g, "Props/Art/Rug_Apt_02.prefab", new Vector3(3.0f, 0.01f, 5.5f), 0f, "BPS_Rug");

            AddPointLightAt(g, "OfficeZone_AccentLight", new Vector3(3.0f, 2.0f, 5.8f), AccentTeal, 1.2f, 5f);
        }

        // =================================================================
        // Furniture primitives
        // =================================================================

        private static void BuildRug(GameObject parent, Vector3 center, Vector2 size, Color accent)
        {
            NewCube(parent, "WoodRug", center + new Vector3(0, 0.005f, 0),
                new Vector3(size.x, 0.01f, size.y), WoodOak);
            NewCube(parent, "RugAccentBand", center + new Vector3(0, 0.006f, 0),
                new Vector3(size.x * 0.6f, 0.012f, 0.15f), accent);
        }

        private static void BuildProduct(GameObject parent, Vector3 pos, Color color, string name)
        {
            NewCube(parent, $"{name}_Plinth", pos + new Vector3(0, 0.04f, 0), new Vector3(0.45f, 0.08f, 0.45f), White);
            NewCube(parent, $"{name}_Item",   pos + new Vector3(0, 0.22f, 0), new Vector3(0.3f, 0.3f, 0.3f), color);
        }

        private static void BuildBarStool(GameObject parent, Vector3 pos, Color seatColor)
        {
            NewCube(parent, "Stool_Seat",   pos + new Vector3(0, 0.85f, 0), new Vector3(0.45f, 0.06f, 0.45f), seatColor);
            NewCube(parent, "Stool_Pillar", pos + new Vector3(0, 0.42f, 0), new Vector3(0.06f, 0.85f, 0.06f), WoodDark);
            NewCube(parent, "Stool_Base",   pos + new Vector3(0, 0.02f, 0), new Vector3(0.4f, 0.04f, 0.4f), WoodDark);
            NewCube(parent, "Stool_Footring", pos + new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.02f, 0.5f), WoodDark);
        }

        private static void BuildOfficeChair(GameObject parent, Vector3 pos, Color seatColor)
        {
            NewCube(parent, "Chair_Seat",    pos + new Vector3(0, 0.5f, 0), new Vector3(0.55f, 0.08f, 0.55f), seatColor);
            NewCube(parent, "Chair_Back",    pos + new Vector3(0, 0.9f, -0.25f), new Vector3(0.55f, 0.7f, 0.08f), seatColor);
            NewCube(parent, "Chair_Pillar",  pos + new Vector3(0, 0.25f, 0), new Vector3(0.08f, 0.5f, 0.08f), new Color(0.15f, 0.15f, 0.15f));
            NewCube(parent, "Chair_Base",    pos + new Vector3(0, 0.03f, 0), new Vector3(0.6f, 0.04f, 0.6f), new Color(0.15f, 0.15f, 0.15f));
        }

        private static void BuildPlant(GameObject parent, Vector3 pos)
        {
            NewCube(parent, "Plant_Pot",     pos + new Vector3(0, 0.08f, 0), new Vector3(0.18f, 0.16f, 0.18f), White);
            NewCube(parent, "Plant_Leaves",  pos + new Vector3(0, 0.28f, 0), new Vector3(0.3f, 0.2f, 0.3f), PlantGreen);
            NewCube(parent, "Plant_Top",     pos + new Vector3(0, 0.45f, 0), new Vector3(0.18f, 0.18f, 0.18f), PlantGreen);
        }

        private static void BuildFloorPlant(GameObject parent, Vector3 pos)
        {
            NewCube(parent, "BigPlant_Pot",     pos + new Vector3(0, 0.3f, 0), new Vector3(0.55f, 0.6f, 0.55f), White);
            NewCube(parent, "BigPlant_Foliage", pos + new Vector3(0, 1.0f, 0), new Vector3(0.8f, 0.8f, 0.8f), PlantGreen);
            NewCube(parent, "BigPlant_Top",     pos + new Vector3(0, 1.6f, 0), new Vector3(0.5f, 0.5f, 0.5f), PlantGreen);
        }

        /// <summary>木格柵牆飾（直立木條等間距排列）。</summary>
        private static void BuildWoodSlatPanel(GameObject parent, Vector3 center, Vector3 size, int slatCount)
        {
            // 背板
            NewCube(parent, "SlatPanel_Back", center, size, WoodDark);

            // 直立木條（覆在背板前 0.04m）
            float slatWidth = (size.x - 0.2f) / slatCount;
            float halfX = (slatCount - 1) * 0.5f * slatWidth;
            for (int i = 0; i < slatCount; i++)
            {
                float x = -halfX + i * slatWidth;
                NewCube(parent, $"Slat_{i}",
                    center + new Vector3(x, 0, -size.z * 0.5f - 0.02f),
                    new Vector3(slatWidth * 0.6f, size.y * 0.95f, 0.04f),
                    WoodOak);
            }
        }

        // =================================================================
        // Material / GO helpers
        // =================================================================

        // =================================================================
        // BPS prefab helper
        // =================================================================
        private const string BPSRoot = "Assets/Brick Project Studio/Apartment Kit/_Prefabs";

        /// <summary>從 BPS 載入 prefab 並放到場景。找不到時 spawn 一個粉紅 cube 標示位置避免靜默失敗。</summary>
        private static GameObject SpawnBPS(GameObject parent, string relPath, Vector3 worldPos, float yawDeg, string nameOverride = null)
        {
            string fullPath = $"{BPSRoot}/{relPath}";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[ZoneOfficeSetup] BPS prefab 不存在：{fullPath} — 放粉紅 cube 標示位置");
                var ph = NewCube(parent,
                    $"MISSING_{System.IO.Path.GetFileNameWithoutExtension(relPath)}",
                    worldPos + Vector3.up * 0.5f,
                    new Vector3(0.5f, 1f, 0.5f),
                    new Color(1f, 0.2f, 1f));
                return ph;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            if (!string.IsNullOrEmpty(nameOverride)) go.name = nameOverride;
            // 注意：SetParent 後用 localPosition，且家具用 worldPosStays=true 比較好定位 → 用 transform.position 設絕對
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0, yawDeg, 0);
            return go;
        }

        private static GameObject NewGroup(Transform parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g;
        }

        private static GameObject NewCube(Transform parent, string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = GetColorMaterial(color);
            return go;
        }

        private static GameObject NewCube(GameObject parent, string name, Vector3 pos, Vector3 size, Color color)
            => NewCube(parent.transform, name, pos, size, color);

        private static GameObject NewCubeEmissive(GameObject parent, string name, Vector3 pos, Vector3 size, Color color, float emission)
        {
            var go = NewCube(parent.transform, name, pos, size, color);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = GetEmissiveMaterial(color, emission);
            return go;
        }

        private static void AddPointLightAt(GameObject parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        private static readonly Dictionary<Color, Material> _matCache = new Dictionary<Color, Material>();

        private static Material GetColorMaterial(Color c)
        {
            if (_matCache.TryGetValue(c, out var m) && m != null) return m;

            string colorHex = ColorUtility.ToHtmlStringRGB(c);
            string path = $"{MaterialsFolder}/Color_{colorHex}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                mat.SetColor("_Color", c);
                mat.SetFloat("_Metallic", 0.05f);
                mat.SetFloat("_Glossiness", 0.35f);
                AssetDatabase.CreateAsset(mat, path);
            }
            _matCache[c] = mat;
            return mat;
        }

        private static Material GetEmissiveMaterial(Color c, float emission)
        {
            string colorHex = ColorUtility.ToHtmlStringRGB(c);
            string path = $"{MaterialsFolder}/Emi_{colorHex}_{emission:F1}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                mat.SetColor("_Color", c);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c * emission);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0.4f);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        // =================================================================
        // Asset helpers
        // =================================================================

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var inst = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(inst, path);
            return inst;
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
