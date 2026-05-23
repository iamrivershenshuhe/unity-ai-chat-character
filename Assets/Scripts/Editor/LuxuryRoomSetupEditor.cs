// =====================================================================
// LuxuryRoomSetupEditor.cs
// Editor Tool：在當前場景搭建「黑色大理石高級套房」環境
//   - 20m × 15m × 4m 房間（地板、牆面、天花板）
//   - 黑色大理石地板（高反射）、深色面板牆、落地玻璃窗 + 金屬窗框
//   - 暖金色間接燈帶（天花板 cove + 地板基線）、天花板 downlight
//   - 黃昏 Procedural Skybox、Reflection Probe（Realtime, Box Projection）
//   - 自動把 unity-chan 擺到房間中央偏前
//   - 調整相機到可看見角色與城市景觀的對角視角
// 選單位置：Tools > Build Luxury Room
// =====================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIAgentChat.EditorTools
{
    /// <summary>
    /// 在當前開啟的場景中搭建黑色大理石高級套房環境。
    /// 重複執行會先刪除舊的 "LuxuryRoom" 根物件再重建，材質會就地更新。
    /// </summary>
    public static class LuxuryRoomSetupEditor
    {
        // ----- Folders / Names -----
        private const string MaterialsFolder = "Assets/Materials/Room";
        private const string SkyboxFolder = "Assets/Materials/Room";
        private const string EnvRootName = "LuxuryRoom";
        private const string MarbleTextureGuid = "6a9d24f22a42946c1bf5785a43d8f7d8"; // 大理石材質.jpg
        private const string UnityChanPrefabPath = "Assets/unity-chan!/Unity-chan! Model/Prefabs/unitychan.prefab";

        // ----- Room dimensions (metres) -----
        private const float RoomWidth = 20f;   // X
        private const float RoomDepth = 15f;   // Z
        private const float RoomHeight = 4f;   // Y

        // Wall thickness (0.1m) — gives walls visible depth.
        private const float WallT = 0.1f;

        // Half extents — convenience.
        private const float HalfW = RoomWidth * 0.5f;
        private const float HalfD = RoomDepth * 0.5f;

        // ----- Cached materials (rebuilt every run) -----
        private static Material _floorMat;
        private static Material _wallMat;
        private static Material _ceilingMat;
        private static Material _frameMat;
        private static Material _glassMat;
        private static Material _goldEmissiveMat;
        private static Material _whiteEmissiveMat;

        [MenuItem("Tools/Build Luxury Room")]
        public static void BuildLuxuryRoom()
        {
            EnsureFolder(MaterialsFolder);

            // 1) 建立 / 更新所有材質
            CreateMaterials();

            // 2) 若已有舊 LuxuryRoom 根物件 → 先刪除（避免重複堆疊）
            var existingRoot = GameObject.Find(EnvRootName);
            if (existingRoot != null)
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Build Luxury Room",
                    $"場景中已存在 '{EnvRootName}' 根物件。\n要刪除並重建嗎？",
                    "重建", "取消");
                if (!ok) return;
                Object.DestroyImmediate(existingRoot);
            }

            // 3) 建立房間根物件
            var root = new GameObject(EnvRootName);
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            // 4) 幾何
            BuildFloor(root.transform);
            BuildCeiling(root.transform);
            BuildBackWall(root.transform);
            BuildLeftWall(root.transform);
            BuildFrontWall(root.transform);          // 鏡頭背後也補一面（避免穿幫）
            BuildWindowWall(root.transform);         // 右側落地窗

            // 5) 燈光裝飾
            BuildCeilingCoveLights(root.transform);  // 天花板間接燈帶
            BuildBaseboardGoldLines(root.transform); // 地板暖金色基線
            BuildCeilingDownlights(root.transform);  // 天花板 3 個 spot 投射燈
            BuildAccentPointLights(root.transform);  // 室內幾顆暖光點光源

            // 6) Reflection Probe（提升地板真實反射）
            BuildReflectionProbe(root.transform);

            // 7) 場景全域設定
            SetupDirectionalLight();
            SetupSkyboxAndAmbient();

            // 8) 角色 + 相機
            EnsureUnityChan();
            SetupCamera();

            // 9) 標記場景髒
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "Build Luxury Room",
                    "高級套房場景已建立完成！\n\n" +
                    "下一步：\n" +
                    "1. 按 Cmd/Ctrl+S 存檔\n" +
                    "2. 按 Play 進入場景\n" +
                    "3. 想調整：選 LuxuryRoom → 子物件 → 改 Transform 即可\n\n" +
                    "提示：Reflection Probe 為 Realtime 模式，Play 時會自動烘焙；\n" +
                    "若 Edit Mode 反射看起來不對，按 ReflectionProbe 上的 Bake 一次。",
                    "OK");
            }
        }

        // =================================================================
        // Materials
        // =================================================================

        private static void CreateMaterials()
        {
            // 黑大理石地板（用既有的大理石貼圖 + 黑色 tint + 高反射）
            _floorMat = CreateOrUpdateMaterial("BlackMarbleFloor", mat =>
            {
                mat.SetColor("_Color", new Color(0.10f, 0.10f, 0.11f, 1f));
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath(MarbleTextureGuid));
                if (tex != null)
                {
                    mat.SetTexture("_MainTex", tex);
                    mat.SetTextureScale("_MainTex", new Vector2(3f, 2.5f));
                }
                mat.SetFloat("_Metallic", 0.15f);
                mat.SetFloat("_Glossiness", 0.93f); // _Smoothness 在 Standard shader 裡叫 _Glossiness
            });

            // 深色面板牆（霧面，幾乎不反光）
            _wallMat = CreateOrUpdateMaterial("DarkPanelWall", mat =>
            {
                mat.SetColor("_Color", new Color(0.045f, 0.045f, 0.05f, 1f));
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0.25f);
            });

            // 天花板（比牆面再深一點）
            _ceilingMat = CreateOrUpdateMaterial("DarkCeiling", mat =>
            {
                mat.SetColor("_Color", new Color(0.03f, 0.03f, 0.035f, 1f));
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0.15f);
            });

            // 窗框 — 偏黑、略金屬
            _frameMat = CreateOrUpdateMaterial("BlackMetalFrame", mat =>
            {
                mat.SetColor("_Color", new Color(0.02f, 0.02f, 0.025f, 1f));
                mat.SetFloat("_Metallic", 0.8f);
                mat.SetFloat("_Glossiness", 0.55f);
            });

            // 玻璃（透明、高 smoothness）
            _glassMat = CreateOrUpdateMaterial("ClearGlass", mat =>
            {
                SetStandardTransparent(mat);
                mat.SetColor("_Color", new Color(0.75f, 0.82f, 0.9f, 0.18f));
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0.96f);
            });

            // 暖金色發光（牆角線 + cove 燈帶）
            _goldEmissiveMat = CreateOrUpdateMaterial("WarmGoldEmissive", mat =>
            {
                mat.SetColor("_Color", new Color(1f, 0.78f, 0.4f, 1f));
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0.45f);
                // Emission
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.22f) * 3.5f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            });

            // 天花板 downlight 出光面（柔和暖白）
            _whiteEmissiveMat = CreateOrUpdateMaterial("DownlightDiscEmissive", mat =>
            {
                mat.SetColor("_Color", new Color(1f, 0.95f, 0.85f, 1f));
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0.5f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.6f) * 2.5f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            });
        }

        private static Material CreateOrUpdateMaterial(string name, System.Action<Material> configure)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                mat.name = name;
                AssetDatabase.CreateAsset(mat, path);
            }
            configure(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Standard shader → Transparent rendering mode（手動設定 _Mode、blend、queue、keyword）。</summary>
        private static void SetStandardTransparent(Material mat)
        {
            mat.SetFloat("_Mode", 3f); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        // =================================================================
        // Geometry helpers
        // =================================================================

        /// <summary>建立一個 Cube primitive 並設定位置、大小、材質、不投影陰影選項。</summary>
        private static GameObject CreateCube(Transform parent, string name, Vector3 pos, Vector3 size,
            Material material, bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = material;
                if (!castShadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 室內裝飾的小物件不需要 Collider，移除以節省
            var col = go.GetComponent<Collider>();
            // 留地板、牆面的 Collider，方便之後做 NavMesh / 物理；裝飾物在呼叫端會自行刪除
            _ = col;
            return go;
        }

        private static void BuildFloor(Transform parent)
        {
            CreateCube(parent, "Floor",
                pos: new Vector3(0, -WallT * 0.5f, 0),
                size: new Vector3(RoomWidth, WallT, RoomDepth),
                material: _floorMat);
        }

        private static void BuildCeiling(Transform parent)
        {
            CreateCube(parent, "Ceiling",
                pos: new Vector3(0, RoomHeight + WallT * 0.5f, 0),
                size: new Vector3(RoomWidth, WallT, RoomDepth),
                material: _ceilingMat,
                castShadows: false);
        }

        private static void BuildBackWall(Transform parent)
        {
            var wallParent = new GameObject("WallBack");
            wallParent.transform.SetParent(parent, false);

            // 主牆面
            CreateCube(wallParent.transform, "Surface",
                pos: new Vector3(0, RoomHeight * 0.5f, HalfD + WallT * 0.5f),
                size: new Vector3(RoomWidth + WallT * 2f, RoomHeight, WallT),
                material: _wallMat);

            // 兩道凹凸面板細節（中段加兩條深色帶模仿線板）
            // 簡化：略過細部嵌板，藉燈光投影自然分塊
        }

        private static void BuildLeftWall(Transform parent)
        {
            var wallParent = new GameObject("WallLeft");
            wallParent.transform.SetParent(parent, false);

            CreateCube(wallParent.transform, "Surface",
                pos: new Vector3(-HalfW - WallT * 0.5f, RoomHeight * 0.5f, 0),
                size: new Vector3(WallT, RoomHeight, RoomDepth),
                material: _wallMat);
        }

        /// <summary>鏡頭背後也補一面，避免反射看到背景虛空。</summary>
        private static void BuildFrontWall(Transform parent)
        {
            var wallParent = new GameObject("WallFront");
            wallParent.transform.SetParent(parent, false);

            CreateCube(wallParent.transform, "Surface",
                pos: new Vector3(0, RoomHeight * 0.5f, -HalfD - WallT * 0.5f),
                size: new Vector3(RoomWidth + WallT * 2f, RoomHeight, WallT),
                material: _wallMat,
                castShadows: false);
        }

        /// <summary>右側落地窗：上下橫梁 + 中段直立窗櫺 + 整片玻璃。</summary>
        private static void BuildWindowWall(Transform parent)
        {
            var windowParent = new GameObject("WindowWall");
            windowParent.transform.SetParent(parent, false);
            windowParent.transform.localPosition = new Vector3(HalfW, 0, 0);

            // 玻璃面板（單一大片，位於外側讓窗櫺看起來壓在玻璃前）
            var glass = CreateCube(windowParent.transform, "Glass",
                pos: new Vector3(WallT * 0.5f, RoomHeight * 0.5f, 0),
                size: new Vector3(0.05f, RoomHeight - 0.4f, RoomDepth - 0.1f),
                material: _glassMat,
                castShadows: false);
            // 玻璃不需要 Collider
            var glassCol = glass.GetComponent<Collider>();
            if (glassCol != null) Object.DestroyImmediate(glassCol);

            // 上橫梁
            CreateCube(windowParent.transform, "TopRail",
                pos: new Vector3(0, RoomHeight - 0.1f, 0),
                size: new Vector3(0.25f, 0.2f, RoomDepth),
                material: _frameMat);
            // 下橫梁
            CreateCube(windowParent.transform, "BottomRail",
                pos: new Vector3(0, 0.15f, 0),
                size: new Vector3(0.25f, 0.3f, RoomDepth),
                material: _frameMat);

            // 5 條直立窗櫺，平均分佈於 z = -5 .. +5
            for (int i = 0; i < 5; i++)
            {
                float z = -5f + i * 2.5f;
                CreateCube(windowParent.transform, $"Mullion_{i}",
                    pos: new Vector3(0, RoomHeight * 0.5f, z),
                    size: new Vector3(0.18f, RoomHeight - 0.5f, 0.12f),
                    material: _frameMat);
            }

            // 兩端角柱（也是窗框）
            CreateCube(windowParent.transform, "CornerPost_Back",
                pos: new Vector3(0, RoomHeight * 0.5f, HalfD - 0.1f),
                size: new Vector3(0.25f, RoomHeight, 0.2f),
                material: _frameMat);
            CreateCube(windowParent.transform, "CornerPost_Front",
                pos: new Vector3(0, RoomHeight * 0.5f, -HalfD + 0.1f),
                size: new Vector3(0.25f, RoomHeight, 0.2f),
                material: _frameMat);
        }

        // =================================================================
        // Lighting decorations
        // =================================================================

        /// <summary>天花板四週的暖光間接燈帶（內凹發光條，仿造 cove light 效果）。</summary>
        private static void BuildCeilingCoveLights(Transform parent)
        {
            var coveParent = new GameObject("CeilingCove");
            coveParent.transform.SetParent(parent, false);

            float y = RoomHeight - 0.08f;
            float inset = 0.3f;     // 從牆面往內凹的距離
            float stripT = 0.08f;   // 燈條厚度

            // 4 條：後、左、前、右
            CreateCube(coveParent.transform, "Strip_Back",
                pos: new Vector3(0, y, HalfD - inset),
                size: new Vector3(RoomWidth - 2 * inset, stripT, 0.15f),
                material: _goldEmissiveMat,
                castShadows: false);
            CreateCube(coveParent.transform, "Strip_Left",
                pos: new Vector3(-HalfW + inset, y, 0),
                size: new Vector3(0.15f, stripT, RoomDepth - 2 * inset),
                material: _goldEmissiveMat,
                castShadows: false);
            CreateCube(coveParent.transform, "Strip_Front",
                pos: new Vector3(0, y, -HalfD + inset),
                size: new Vector3(RoomWidth - 2 * inset, stripT, 0.15f),
                material: _goldEmissiveMat,
                castShadows: false);
            CreateCube(coveParent.transform, "Strip_Right",
                pos: new Vector3(HalfW - inset, y, 0),
                size: new Vector3(0.15f, stripT, RoomDepth - 2 * inset),
                material: _goldEmissiveMat,
                castShadows: false);

            // 配合的 point lights（提供實際照明，材質本身的 emission 只有 GI 影響不夠強）
            float lightY = RoomHeight - 0.25f;
            AddPointLight(coveParent.transform, "CoveLight_BackL",
                pos: new Vector3(-HalfW * 0.5f, lightY, HalfD - 0.6f),
                color: new Color(1f, 0.7f, 0.35f), intensity: 1.6f, range: 7f);
            AddPointLight(coveParent.transform, "CoveLight_BackR",
                pos: new Vector3(HalfW * 0.5f, lightY, HalfD - 0.6f),
                color: new Color(1f, 0.7f, 0.35f), intensity: 1.6f, range: 7f);
            AddPointLight(coveParent.transform, "CoveLight_LeftMid",
                pos: new Vector3(-HalfW + 0.6f, lightY, 0),
                color: new Color(1f, 0.72f, 0.35f), intensity: 1.4f, range: 7f);
        }

        /// <summary>地板基線的暖金色發光線（沿著左牆、後牆底部）。</summary>
        private static void BuildBaseboardGoldLines(Transform parent)
        {
            var baseParent = new GameObject("BaseboardGold");
            baseParent.transform.SetParent(parent, false);

            float y = 0.06f;
            float stripT = 0.08f;
            float inset = 0.05f;

            // 左牆底
            CreateCube(baseParent.transform, "GoldLine_Left",
                pos: new Vector3(-HalfW + inset + 0.05f, y, 0),
                size: new Vector3(0.1f, stripT, RoomDepth - 0.4f),
                material: _goldEmissiveMat,
                castShadows: false);
            // 後牆底
            CreateCube(baseParent.transform, "GoldLine_Back",
                pos: new Vector3(0, y, HalfD - inset - 0.05f),
                size: new Vector3(RoomWidth - 0.4f, stripT, 0.1f),
                material: _goldEmissiveMat,
                castShadows: false);
            // 前牆底（鏡頭背後）— 為了反射對稱
            CreateCube(baseParent.transform, "GoldLine_Front",
                pos: new Vector3(0, y, -HalfD + inset + 0.05f),
                size: new Vector3(RoomWidth - 0.4f, stripT, 0.1f),
                material: _goldEmissiveMat,
                castShadows: false);

            // 補一顆貼地的暖光（讓 gold line 在地板上有反光暈）
            AddPointLight(baseParent.transform, "BaseGlow_LeftMid",
                pos: new Vector3(-HalfW + 0.4f, 0.3f, 0),
                color: new Color(1f, 0.65f, 0.3f), intensity: 0.9f, range: 5f);
            AddPointLight(baseParent.transform, "BaseGlow_BackMid",
                pos: new Vector3(0, 0.3f, HalfD - 0.4f),
                color: new Color(1f, 0.65f, 0.3f), intensity: 0.9f, range: 5f);
        }

        /// <summary>天花板 3 個 spot 投射燈，照亮主角區域。</summary>
        private static void BuildCeilingDownlights(Transform parent)
        {
            var dlParent = new GameObject("CeilingDownlights");
            dlParent.transform.SetParent(parent, false);

            float y = RoomHeight - 0.05f;
            float[] xs = { -3f, 0f, 3f };
            int idx = 0;
            foreach (float x in xs)
            {
                // 出光面（圓盤效果以 Cube + emissive 簡單模擬）
                CreateCube(dlParent.transform, $"DownlightDisc_{idx}",
                    pos: new Vector3(x, y, -3f),
                    size: new Vector3(0.4f, 0.05f, 0.4f),
                    material: _whiteEmissiveMat,
                    castShadows: false);

                // 對應的 Spot Light
                var lightGO = new GameObject($"DownlightSpot_{idx}");
                lightGO.transform.SetParent(dlParent.transform, false);
                lightGO.transform.localPosition = new Vector3(x, y - 0.1f, -3f);
                lightGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 朝下
                var l = lightGO.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.92f, 0.78f);
                l.intensity = 5f;
                l.range = 8f;
                l.spotAngle = 70f;
                l.innerSpotAngle = 25f;
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.65f;
                idx++;
            }
        }

        /// <summary>幾顆室內暖光，加強氛圍與地板反射。</summary>
        private static void BuildAccentPointLights(Transform parent)
        {
            var apParent = new GameObject("AccentLights");
            apParent.transform.SetParent(parent, false);

            // 角色腰高的補光（讓 unity-chan 不會太暗）
            AddPointLight(apParent.transform, "FillLight_Character",
                pos: new Vector3(-1f, 1.5f, -1f),
                color: new Color(1f, 0.92f, 0.82f), intensity: 1.2f, range: 6f);

            // 窗邊溫暖光（模擬窗外光線反射回來）
            AddPointLight(apParent.transform, "WindowGlow_Mid",
                pos: new Vector3(HalfW - 1.5f, 1.5f, 0),
                color: new Color(1f, 0.78f, 0.6f), intensity: 1.4f, range: 8f);
        }

        private static void AddPointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None; // 多顆點光 + 陰影會炸效能
        }

        // =================================================================
        // Reflection Probe
        // =================================================================

        private static void BuildReflectionProbe(Transform parent)
        {
            var probeGO = new GameObject("RoomReflectionProbe");
            probeGO.transform.SetParent(parent, false);
            probeGO.transform.localPosition = new Vector3(0, 1.5f, 0);

            var probe = probeGO.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.boxProjection = true;
            probe.size = new Vector3(RoomWidth, RoomHeight, RoomDepth);
            probe.center = new Vector3(0, RoomHeight * 0.5f - 1.5f, 0);
            probe.intensity = 1f;
            probe.resolution = 256;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        }

        // =================================================================
        // Global lighting
        // =================================================================

        /// <summary>調整場景中 Directional Light 模擬黃昏低角度暖陽。</summary>
        private static void SetupDirectionalLight()
        {
            Light dir = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.type == LightType.Directional) { dir = l; break; }
            }
            if (dir == null)
            {
                var go = new GameObject("Directional Light");
                dir = go.AddComponent<Light>();
                dir.type = LightType.Directional;
            }

            // 黃昏：水平偏低、從窗戶側射入（+X 方向）
            dir.transform.rotation = Quaternion.Euler(12f, -120f, 0f);
            dir.color = new Color(1f, 0.78f, 0.55f);
            dir.intensity = 1.1f;
            dir.shadows = LightShadows.Soft;
            dir.shadowStrength = 0.8f;
            dir.shadowBias = 0.05f;
            dir.shadowNormalBias = 0.4f;
            EditorUtility.SetDirty(dir);
            EditorUtility.SetDirty(dir.transform);
        }

        /// <summary>建立黃昏 Procedural Skybox 並設定環境光。</summary>
        private static void SetupSkyboxAndAmbient()
        {
            string path = $"{SkyboxFolder}/SunsetCitySkybox.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                sky.name = "SunsetCitySkybox";
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 0.65f);
            sky.SetColor("_SkyTint", new Color(0.62f, 0.55f, 0.78f)); // 紫粉
            sky.SetColor("_GroundColor", new Color(0.12f, 0.1f, 0.14f));
            sky.SetFloat("_Exposure", 1.05f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.85f;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = false; // 室內不開霧
            DynamicGI.UpdateEnvironment();
        }

        // =================================================================
        // unity-chan + camera
        // =================================================================

        /// <summary>場景中若無角色，instantiate unity-chan prefab；有 placeholder 則保留。</summary>
        private static void EnsureUnityChan()
        {
            // 若已有 AICharacter（不管是 placeholder 還是 unity-chan）就只調位置 + 朝向
            var ai = GameObject.Find("AICharacter");
            if (ai == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnityChanPrefabPath);
                if (prefab != null)
                {
                    ai = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    ai.name = "AICharacter";
                }
                else
                {
                    // 找不到 prefab 就先放 capsule placeholder
                    ai = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    ai.name = "AICharacter";
                    ai.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f);
                    Debug.LogWarning($"[LuxuryRoomSetupEditor] 找不到 unity-chan prefab：{UnityChanPrefabPath}，已放 placeholder");
                }
            }

            // 房間中央偏前（鏡頭看得到的位置），面向 -Z（朝鏡頭）
            ai.transform.position = new Vector3(0.8f, 0f, 1.5f);
            ai.transform.rotation = Quaternion.Euler(0, 200f, 0); // 略側身一點，避免完全正面
            EditorUtility.SetDirty(ai.transform);
        }

        /// <summary>把主鏡頭調整成對角取景，能同時看到角色與右側落地窗。</summary>
        private static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            // 站在房間左前角附近，往右後方看
            cam.transform.position = new Vector3(-3.2f, 1.55f, -4.2f);
            cam.transform.rotation = Quaternion.Euler(5f, 22f, 0f);
            cam.fieldOfView = 48f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.Skybox; // 看得到 skybox

            EditorUtility.SetDirty(cam);
            EditorUtility.SetDirty(cam.transform);
        }

        // =================================================================
        // Misc helpers
        // =================================================================

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
