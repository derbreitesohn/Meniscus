using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Decorative backdrop of the real 3D coins (copper / silver / gold) falling slowly in front of the
    /// menu camera. Built entirely at runtime: a rig parented to the camera holds the coins (and a key
    /// light, because the menu scene has no lights of its own), each slot keeps one persistent coin
    /// instance so all three metals stay represented, and the coins live in world space behind the
    /// screen-space menu UI so the title and buttons draw on top of them.
    ///
    /// Placement and recycling are done against the camera's real projection (ViewportToWorldPoint) rather
    /// than a hand-computed frustum, so a coin only ever recycles once it is genuinely off-screen — it
    /// never vanishes while still partly in view, whatever the field of view or aspect ratio.
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuCoinBackground : MonoBehaviour
    {
        const int CoinCount = 32;
        const float CoinDistance = 4.5f;   // metres in front of the camera; set back so coins sit deep in view.
        const float FallSpeedMin = 0.12f;
        const float FallSpeedMax = 0.3f;
        const float DiameterMin = 0.55f;
        const float DiameterMax = 1.05f;
        // Coins are pushed back from the base distance by 0..DepthSpread for layering — never pulled
        // closer, so none ever loom right in front of the camera.
        const float DepthSpread = 1.5f;

        // Resting orientation that points a flat coin face at the camera. Coins hold this pose as they
        // fall — no spin.
        static readonly Vector3 FaceEuler = new(-90f, 0f, 0f);

        static readonly Color[] FallbackMetalTints =
        {
            new(0.72f, 0.45f, 0.20f), // copper
            new(0.80f, 0.80f, 0.84f), // silver
            new(0.92f, 0.74f, 0.30f), // gold
        };

        struct Faller
        {
            public Transform transform;
            public float fallSpeed;
            public float radius;   // world-space half-size, used to test when fully off-screen.
        }

        GameObject[] models;
        Faller[] coins;
        Transform rig;
        Camera cam;

        public static MenuCoinBackground Create(GameObject copper, GameObject silver, GameObject gold)
        {
            var background = new GameObject("Menu Coin Background").AddComponent<MenuCoinBackground>();
            background.models = new[] { copper, silver, gold };
            return background;
        }

        void Start()
        {
            cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (cam == null)
                return;

            BuildRig(cam);
            SpawnCoins();
        }

        void BuildRig(Camera camera)
        {
            rig = new GameObject("Coin Rain Rig").transform;
            rig.SetParent(camera.transform, false);
            rig.localPosition = Vector3.forward * CoinDistance;
            rig.localRotation = Quaternion.identity;

            // The menu scene has no lights, so add a warm key light or the metal coins render near-black.
            var lightObject = new GameObject("Coin Rain Light");
            lightObject.transform.SetParent(rig, false);
            lightObject.transform.localRotation = Quaternion.Euler(35f, -25f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.86f);
        }

        void SpawnCoins()
        {
            coins = new Faller[CoinCount];

            for (var i = 0; i < CoinCount; i++)
            {
                var instance = CreateCoinInstance(i);
                coins[i] = NewCoin(instance.transform, respawnAtTop: false);
            }
        }

        // Cycles through the three metals so copper, silver and gold are all on screen, then keeps each
        // slot's metal as it recycles. Falls back to a tinted flat cylinder if a model is unwired.
        GameObject CreateCoinInstance(int index)
        {
            var metalIndex = index % 3;
            var model = models != null && metalIndex < models.Length ? models[metalIndex] : null;
            GameObject instance;

            if (model != null)
            {
                instance = Instantiate(model);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                instance.transform.localScale = new Vector3(1f, 0.08f, 1f);
                var renderer = instance.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = FallbackMetalTints[metalIndex];
            }

            instance.name = $"Menu Coin {index + 1}";
            StripColliders(instance);
            instance.transform.SetParent(rig, false);
            return instance;
        }

        Faller NewCoin(Transform coinTransform, bool respawnAtTop)
        {
            var diameter = Random.Range(DiameterMin, DiameterMax);
            FitToDiameter(coinTransform.gameObject, diameter);
            var radius = diameter * 0.5f;

            var camDistance = CoinDistance + Random.Range(0f, DepthSpread);

            // Measure the visible rectangle at this depth straight from the camera's projection.
            var center = cam.transform.position + cam.transform.forward * camDistance;
            var halfHeight = 0.5f * (
                cam.ViewportToWorldPoint(new Vector3(0.5f, 1f, camDistance)) -
                cam.ViewportToWorldPoint(new Vector3(0.5f, 0f, camDistance))).magnitude;
            var halfWidth = 0.5f * (
                cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, camDistance)) -
                cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, camDistance))).magnitude;

            var offsetX = Random.Range(-(halfWidth - radius), halfWidth - radius);
            // Initial coins scatter across the visible height; recycled coins drop in just above the top.
            var offsetY = respawnAtTop
                ? halfHeight + radius + Random.Range(0f, 0.6f)
                : Random.Range(-(halfHeight - radius), halfHeight - radius);

            coinTransform.position = center + cam.transform.right * offsetX + cam.transform.up * offsetY;
            // Face the camera, with a random roll so the faces aren't all oriented identically.
            coinTransform.rotation = cam.transform.rotation
                * Quaternion.Euler(FaceEuler)
                * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            return new Faller
            {
                transform = coinTransform,
                fallSpeed = Random.Range(FallSpeedMin, FallSpeedMax),
                radius = radius,
            };
        }

        void Update()
        {
            if (coins == null || cam == null)
                return;

            var dt = Time.unscaledDeltaTime;
            var down = cam.transform.up;

            for (var i = 0; i < coins.Length; i++)
            {
                var coin = coins[i];
                if (coin.transform == null)
                    continue;

                var position = coin.transform.position - down * (coin.fallSpeed * dt);
                coin.transform.position = position;

                // How far the coin's centre sits above the bottom edge of the view at its own depth.
                var camDistance = Mathf.Max(0.01f, Vector3.Dot(position - cam.transform.position, cam.transform.forward));
                var bottomEdge = cam.ViewportToWorldPoint(new Vector3(0.5f, 0f, camDistance));
                var heightAboveBottom = Vector3.Dot(position - bottomEdge, cam.transform.up);

                // Recycle only once the whole coin has cleared the bottom edge.
                if (heightAboveBottom < -coin.radius)
                    coins[i] = NewCoin(coin.transform, respawnAtTop: true);
            }
        }

        // Uniformly scale the instance so its largest dimension matches the target world diameter, so each
        // coin reads at a consistent size whatever its model's authored scale.
        static void FitToDiameter(GameObject instance, float targetDiameter)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largest <= 1e-4f)
                return;

            instance.transform.localScale *= targetDiameter / largest;
        }

        static void StripColliders(GameObject instance)
        {
            var colliders = instance.GetComponentsInChildren<Collider>();
            for (var i = 0; i < colliders.Length; i++)
                Destroy(colliders[i]);
        }
    }
}
