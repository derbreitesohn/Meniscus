using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.Editor
{
    public static class SaloonPrototypeSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Saloon.unity";
        const string PrototypeRootName = "PrototypeRuntime";
        const string GlassTag = "Glass";
        const string MaterialFolder = "Assets/Prototype/Materials";

        [MenuItem("Meniscus/Build Prototype Saloon Scene")]
        public static void BuildPrototypeSaloonScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildIntoOpenScene(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SaloonPrototypeSceneBuilder] Built and saved prototype scene: {ScenePath}");
        }

        public static void BuildFromCommandLine()
        {
            BuildPrototypeSaloonScene();
            EditorApplication.Exit(0);
        }

        static void BuildIntoOpenScene(Scene scene)
        {
            EnsureGlassTag();
            EnsureMaterialFolder();
            RemoveExistingPrototypeRoot();
            RemoveLegacyGameObject();

            var root = CreateRoot(PrototypeRootName);
            var managers = CreateChild(root.transform, "Managers");
            var props = CreateChild(root.transform, "TablePrototype");

            var camera = EnsureMainCamera();
            var glassManager = managers.AddComponent<GlassManager>();
            var economyManager = managers.AddComponent<EconomyManager>();
            var enemyAI = managers.AddComponent<EnemyAI>();
            var cameraController = managers.AddComponent<CameraController>();
            var shopManager = managers.AddComponent<ShopManager>();
            var endScreenManager = managers.AddComponent<EndScreenManager>();
            var gameManager = managers.AddComponent<GameManager>();
            var debugOverlay = managers.AddComponent<GameTestController>();
            var playerController = EnsurePlayerController(camera);

            var table = EnsurePrototypeTable(props.transform);
            var opponent = CreateOpponent(props.transform);
            var glass = CreateGlass(props.transform, glassManager);
            var playerCoins = CreateCoinPile(props.transform, "PlayerCoins", true, new Vector3(-0.62f, 1.09f, -1.02f));
            var enemyCoins = CreateCoinPile(props.transform, "EnemyCoins", false, new Vector3(-0.62f, 1.09f, 0.88f));
            var shopCanvas = CreateShopCanvas(root.transform, shopManager);
            CreateEndScreenCanvas(root.transform, endScreenManager);
            EnsureEventSystem();

            WirePlayerController(playerController, gameManager, camera);
            WireShopManager(shopManager, gameManager, economyManager, shopCanvas);
            WireGameManager(
                gameManager,
                glassManager,
                economyManager,
                enemyAI,
                cameraController,
                shopManager,
                endScreenManager,
                playerCoins,
                enemyCoins);
            WireDebugOverlay(debugOverlay, gameManager, glassManager, economyManager);

            Selection.activeObject = root;
            Debug.Log(
                $"[SaloonPrototypeSceneBuilder] Prototype scene ready. Objects: table={table.name}, " +
                $"opponent={opponent.name}, glass={glass.name}, playerCoins={playerCoins.Count}, enemyCoins={enemyCoins.Count}.");
        }

        static GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.position = Vector3.zero;
            return root;
        }

        static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        static Camera EnsureMainCamera()
        {
            var camera = Camera.main;

            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.transform.position = new Vector3(0f, 2.35f, -3.25f);
            camera.transform.LookAt(new Vector3(0f, 1.02f, 0.25f), Vector3.up);
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            Debug.Log("[SaloonPrototypeSceneBuilder] Main Camera fixed to saloon table view.");
            return camera;
        }

        static PlayerController EnsurePlayerController(Camera camera)
        {
            var playerController = camera.GetComponent<PlayerController>();

            if (playerController == null)
                playerController = camera.gameObject.AddComponent<PlayerController>();

            return playerController;
        }

        static GameObject EnsurePrototypeTable(Transform parent)
        {
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Clickable Saloon Table";
            table.transform.SetParent(parent, false);
            table.transform.position = new Vector3(0f, 0.86f, 0.07f);
            table.transform.localScale = new Vector3(3.85f, 0.18f, 3.1f);
            ApplyMaterial(table, "Prototype_Table_DarkWood", new Color(0.24f, 0.12f, 0.055f));

            var playSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playSurface.name = "Dark Leather Play Surface";
            playSurface.transform.SetParent(parent, false);
            playSurface.transform.position = new Vector3(0f, 0.965f, 0.07f);
            playSurface.transform.localScale = new Vector3(3.24f, 0.025f, 2.46f);
            ApplyMaterial(playSurface, "Prototype_Table_DarkLeather", new Color(0.055f, 0.04f, 0.034f));

            CreateCoinLane(parent, "Player Coin Lane", new Vector3(0f, 0.99f, -0.9f));
            CreateCoinLane(parent, "Dealer Coin Lane", new Vector3(0f, 0.99f, 1.04f));
            CreateGlassFocusRing(parent);
            CreateTableRail(parent, "Near Table Rail", new Vector3(0f, 1.02f, -1.48f), new Vector3(3.9f, 0.12f, 0.14f));
            CreateTableRail(parent, "Far Table Rail", new Vector3(0f, 1.02f, 1.62f), new Vector3(3.9f, 0.12f, 0.14f));
            CreateTableRail(parent, "Left Table Rail", new Vector3(-1.94f, 1.02f, 0.07f), new Vector3(0.14f, 0.12f, 3.1f));
            CreateTableRail(parent, "Right Table Rail", new Vector3(1.94f, 1.02f, 0.07f), new Vector3(0.14f, 0.12f, 3.1f));

            return table;
        }

        static void CreateCoinLane(Transform parent, string name, Vector3 position)
        {
            var lane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lane.name = name;
            lane.transform.SetParent(parent, false);
            lane.transform.position = position;
            lane.transform.localScale = new Vector3(2.82f, 0.018f, 0.44f);
            ApplyMaterial(lane, "Prototype_Coin_Lane_WornWood", new Color(0.12f, 0.062f, 0.034f));
        }

        static void CreateGlassFocusRing(Transform parent)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Glass Tension Focus Ring";
            ring.transform.SetParent(parent, false);
            ring.transform.position = new Vector3(0f, 1.002f, 0.08f);
            ring.transform.localScale = new Vector3(0.86f, 0.01f, 0.86f);
            ApplyMaterial(ring, "Prototype_Glass_Tension_Ring", new Color(0.21f, 0.13f, 0.07f));
        }

        static void CreateTableRail(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = name;
            rail.transform.SetParent(parent, false);
            rail.transform.position = position;
            rail.transform.localScale = scale;
            ApplyMaterial(rail, "Prototype_Table_Rail_DarkWood", new Color(0.16f, 0.072f, 0.032f));
        }

        static GameObject CreateOpponent(Transform parent)
        {
            var opponent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            opponent.name = "Opponent Placeholder";
            opponent.transform.SetParent(parent, false);
            opponent.transform.position = new Vector3(0f, 1.62f, 2.12f);
            opponent.transform.localScale = new Vector3(0.64f, 1.08f, 0.64f);
            ApplyMaterial(opponent, "Prototype_Opponent_RedVest", new Color(0.35f, 0.05f, 0.045f));

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Opponent Head";
            head.transform.SetParent(opponent.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.78f, 0f);
            head.transform.localScale = new Vector3(0.52f, 0.42f, 0.52f);
            ApplyMaterial(head, "Prototype_Opponent_Head", new Color(0.62f, 0.48f, 0.36f));

            return opponent;
        }

        static GameObject CreateGlass(Transform parent, GlassManager glassManager)
        {
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            glass.name = "GlassInteractable";
            glass.tag = GlassTag;
            glass.transform.SetParent(parent, false);
            glass.transform.position = new Vector3(0f, 1.34f, 0.08f);
            glass.transform.localScale = new Vector3(0.38f, 0.33f, 0.38f);
            ApplyMaterial(glass, "Prototype_Glass_ClearBlue", new Color(0.64f, 0.88f, 1f, 0.24f));

            var coaster = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coaster.name = "Glass Wet Coaster";
            coaster.transform.SetParent(parent, false);
            coaster.transform.position = new Vector3(0f, 1.0f, 0.08f);
            coaster.transform.localScale = new Vector3(0.58f, 0.018f, 0.58f);
            ApplyMaterial(coaster, "Prototype_Glass_Coaster", new Color(0.06f, 0.055f, 0.05f));

            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "WaterSurface";
            water.transform.SetParent(glass.transform, false);
            water.transform.localPosition = new Vector3(0f, -0.34f, 0f);
            water.transform.localScale = new Vector3(0.82f, 0.025f, 0.82f);
            ApplyMaterial(water, "Prototype_Water", new Color(0.12f, 0.54f, 0.95f, 0.72f));

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Glass Rim Highlight";
            rim.transform.SetParent(glass.transform, false);
            rim.transform.localPosition = new Vector3(0f, 1.02f, 0f);
            rim.transform.localScale = new Vector3(1.1f, 0.025f, 1.1f);
            ApplyMaterial(rim, "Prototype_Glass_Rim", new Color(0.88f, 0.97f, 1f, 0.68f));

            var baseHighlight = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseHighlight.name = "Glass Heavy Base";
            baseHighlight.transform.SetParent(glass.transform, false);
            baseHighlight.transform.localPosition = new Vector3(0f, -1.01f, 0f);
            baseHighlight.transform.localScale = new Vector3(1.02f, 0.05f, 1.02f);
            ApplyMaterial(baseHighlight, "Prototype_Glass_Base", new Color(0.78f, 0.92f, 1f, 0.45f));

            var visual = glass.AddComponent<GlassVisualController>();
            var serializedVisual = new SerializedObject(visual);
            serializedVisual.FindProperty("glassManager").objectReferenceValue = glassManager;
            serializedVisual.FindProperty("waterTransform").objectReferenceValue = water.transform;
            serializedVisual.FindProperty("waterSurfaceScale").vector3Value = new Vector3(0.82f, 0.025f, 0.82f);
            serializedVisual.FindProperty("baseFillRatio").floatValue = 0.45f;
            serializedVisual.FindProperty("lowSurfaceLocalY").floatValue = -0.34f;
            serializedVisual.FindProperty("highSurfaceLocalY").floatValue = 0.72f;
            serializedVisual.FindProperty("riseSpeed").floatValue = 1.1f;
            serializedVisual.ApplyModifiedPropertiesWithoutUndo();

            return glass;
        }

        static List<Coin> CreateCoinPile(Transform parent, string rowName, bool playerOwned, Vector3 start)
        {
            var row = CreateChild(parent, rowName);
            var coins = new List<Coin>();
            var coinCount = GameConstants.MaxCoinsPerActor;

            for (var i = 0; i < coinCount; i++)
            {
                var column = i % 4;
                var stack = i / 4;
                var stagger = stack % 2 == 0 ? 0f : 0.11f;
                var size = RollPrototypeCoinSize(i);
                var displayName = GameConstants.GetDisplayNameForSize(size);
                var coinObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                coinObject.name = $"{(playerOwned ? "Player" : "Enemy")} {displayName} Coin {i + 1}";
                coinObject.transform.SetParent(row.transform, false);
                coinObject.transform.position = start + new Vector3(column * 0.38f + stagger, stack * 0.034f, stack * 0.24f);
                coinObject.transform.rotation = Quaternion.identity;
                coinObject.transform.localScale = GameConstants.GetVisualScaleForSize(size);
                ApplyMaterial(
                    coinObject,
                    $"Prototype_{displayName}_Coin",
                    GameConstants.GetMaterialColorForSize(size));

                var coin = coinObject.AddComponent<Coin>();
                coin.Configure(
                    size,
                    GameConstants.GetRiskForSize(size),
                    GameConstants.GetBasePayoutForSize(size),
                    playerOwned);
                coins.Add(coin);
            }

            return coins;
        }

        static CoinSize RollPrototypeCoinSize(int index)
        {
            if (index == 0)
                return CoinSize.Small;

            if (index == 1)
                return CoinSize.Medium;

            if (index == 2)
                return CoinSize.Large;

            var roll = Random.value;

            if (roll < 0.42f)
                return CoinSize.Small;

            return roll < 0.78f ? CoinSize.Medium : CoinSize.Large;
        }

        static Canvas CreateShopCanvas(Transform parent, ShopManager shopManager)
        {
            var canvasObject = new GameObject("ShopCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = false;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var card = CreateUiImage(canvasObject.transform, "Saloon Menu Card", new Vector2(390f, 300f), new Vector2(0f, 0f), new Color(0.55f, 0.42f, 0.24f, 0.96f));
            CreateUiText(card.transform, "Title", "SALOON MENU", new Vector2(0f, 105f), 28, TextAnchor.MiddleCenter);
            CreateUiText(card.transform, "Description", "Spend banked cash between rounds.", new Vector2(0f, 58f), 18, TextAnchor.MiddleCenter);
            CreateButton(card.transform, "Cheap Item Button", "BUY $25", new Vector2(-95f, -25f), shopManager.BuyCheapItemPlaceholder);
            CreateButton(card.transform, "Premium Item Button", "BUY $75", new Vector2(95f, -25f), shopManager.BuyPremiumItemPlaceholder);
            CreateButton(card.transform, "Finish Drink Button", "FINISH DRINK", new Vector2(0f, -105f), shopManager.FinishOrdering);

            return canvas;
        }

        static Canvas CreateEndScreenCanvas(Transform parent, EndScreenManager endScreenManager)
        {
            var canvasObject = new GameObject("EndScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = false;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var scrim = CreateUiImage(canvasObject.transform, "End Screen Scrim", new Vector2(1920f, 1080f), Vector2.zero, new Color(0.02f, 0.012f, 0.008f, 0.86f));
            var title = CreateUiText(scrim.transform, "Outcome Title", string.Empty, new Vector2(0f, 72f), 74, TextAnchor.MiddleCenter);
            var detail = CreateUiText(scrim.transform, "Outcome Detail", string.Empty, new Vector2(0f, -28f), 26, TextAnchor.MiddleCenter);

            title.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 110f);
            detail.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 88f);

            var titleText = title.GetComponent<Text>();
            titleText.color = new Color(0.95f, 0.82f, 0.52f);
            titleText.fontStyle = FontStyle.Bold;

            var detailText = detail.GetComponent<Text>();
            detailText.color = new Color(0.82f, 0.72f, 0.58f);

            endScreenManager.Configure(canvas, titleText, detailText);
            return canvas;
        }

        static GameObject CreateUiImage(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            var rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = position;
            imageObject.GetComponent<Image>().color = color;
            return imageObject;
        }

        static GameObject CreateUiText(Transform parent, string name, string text, Vector2 position, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(330f, 44f);
            rectTransform.anchoredPosition = position;

            var uiText = textObject.GetComponent<Text>();
            uiText.text = text;
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = new Color(0.08f, 0.045f, 0.025f);
            return textObject;
        }

        static void CreateButton(Transform parent, string name, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var buttonObject = CreateUiImage(parent, name, new Vector2(150f, 46f), position, new Color(0.18f, 0.08f, 0.04f, 1f));
            var button = buttonObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.18f, 0.08f, 0.04f, 1f);
            colors.highlightedColor = new Color(0.34f, 0.15f, 0.07f, 1f);
            colors.pressedColor = new Color(0.08f, 0.03f, 0.02f, 1f);
            button.colors = colors;
            UnityEventTools.AddPersistentListener(button.onClick, action);

            CreateUiText(buttonObject.transform, "Label", label, Vector2.zero, 18, TextAnchor.MiddleCenter);
            var labelText = buttonObject.transform.Find("Label").GetComponent<Text>();
            labelText.color = new Color(0.98f, 0.86f, 0.58f);
        }

        static void WirePlayerController(PlayerController controller, GameManager gameManager, Camera camera)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("gameManager").objectReferenceValue = gameManager;
            serialized.FindProperty("raycastCamera").objectReferenceValue = camera;
            serialized.FindProperty("maxRaycastDistance").floatValue = 80f;
            serialized.FindProperty("interactionMask").intValue = ~0;
            serialized.FindProperty("glassTag").stringValue = GlassTag;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireShopManager(ShopManager shopManager, GameManager gameManager, EconomyManager economyManager, Canvas canvas)
        {
            var serialized = new SerializedObject(shopManager);
            serialized.FindProperty("shopCanvas").objectReferenceValue = canvas;
            serialized.FindProperty("economyManager").objectReferenceValue = economyManager;
            serialized.FindProperty("gameManager").objectReferenceValue = gameManager;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireGameManager(
            GameManager gameManager,
            GlassManager glassManager,
            EconomyManager economyManager,
            EnemyAI enemyAI,
            CameraController cameraController,
            ShopManager shopManager,
            EndScreenManager endScreenManager,
            IReadOnlyList<Coin> playerCoins,
            IReadOnlyList<Coin> enemyCoins)
        {
            var serialized = new SerializedObject(gameManager);
            serialized.FindProperty("autoStart").boolValue = true;
            serialized.FindProperty("shopBetweenRoundsEnabled").boolValue = true;
            serialized.FindProperty("glassManager").objectReferenceValue = glassManager;
            serialized.FindProperty("economyManager").objectReferenceValue = economyManager;
            serialized.FindProperty("enemyAI").objectReferenceValue = enemyAI;
            serialized.FindProperty("cameraController").objectReferenceValue = cameraController;
            serialized.FindProperty("shopManager").objectReferenceValue = shopManager;
            serialized.FindProperty("endScreenManager").objectReferenceValue = endScreenManager;
            AssignCoinList(serialized.FindProperty("handAuthoredPlayerCoins"), playerCoins);
            AssignCoinList(serialized.FindProperty("handAuthoredEnemyCoins"), enemyCoins);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireDebugOverlay(GameTestController debugOverlay, GameManager gameManager, GlassManager glassManager, EconomyManager economyManager)
        {
            var serialized = new SerializedObject(debugOverlay);
            serialized.FindProperty("gameManager").objectReferenceValue = gameManager;
            serialized.FindProperty("glassManager").objectReferenceValue = glassManager;
            serialized.FindProperty("economyManager").objectReferenceValue = economyManager;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AssignCoinList(SerializedProperty property, IReadOnlyList<Coin> coins)
        {
            property.ClearArray();

            for (var i = 0; i < coins.Count; i++)
            {
                property.InsertArrayElementAtIndex(i);
                property.GetArrayElementAtIndex(i).objectReferenceValue = coins[i];
            }
        }

        static void EnsureEventSystem()
        {
            var eventSystem = Object.FindAnyObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            var standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();

            if (standaloneModule != null)
                Object.DestroyImmediate(standaloneModule);

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        static void RemoveExistingPrototypeRoot()
        {
            var existing = GameObject.Find(PrototypeRootName);

            if (existing != null)
                Object.DestroyImmediate(existing);
        }

        static void RemoveLegacyGameObject()
        {
            var legacyGame = GameObject.Find("Game");

            if (legacyGame != null)
                Object.DestroyImmediate(legacyGame);
        }

        static void ApplyMaterial(GameObject gameObject, string materialName, Color color)
        {
            var renderer = gameObject.GetComponent<Renderer>();

            if (renderer == null)
                return;

            renderer.sharedMaterial = EnsureMaterial(materialName, color);
        }

        static Material EnsureMaterial(string materialName, Color color)
        {
            var path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material != null)
            {
                ApplyMaterialProperties(material, color);
                return material;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                name = materialName
            };

            ApplyMaterialProperties(material, color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void ApplyMaterialProperties(Material material, Color color)
        {
            material.color = color;

            if (color.a >= 0.99f)
            {
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_ZWrite", 1f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = -1;
                return;
            }

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static void EnsureMaterialFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prototype"))
                AssetDatabase.CreateFolder("Assets", "Prototype");

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Prototype", "Materials");
        }

        static void EnsureGlassTag()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tagManager.FindProperty("tags");

            for (var i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == GlassTag)
                    return;
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = GlassTag;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[SaloonPrototypeSceneBuilder] Added Glass tag.");
        }
    }
}
