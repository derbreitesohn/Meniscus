using System.Collections;
using System.Collections.Generic;
using Meniscus.Core;
using UnityEngine;


namespace Meniscus.Gameplay
{
    [DisallowMultipleComponent]
    public class CoinDropPresentationController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] Transform glassTarget;
        [SerializeField, Min(0.05f)] float dropAnimationSeconds = 0.98f;
        [SerializeField, Min(0f)] float liftArcHeight = 0.34f;
        [SerializeField, Min(0f)] float proxyLifetimeAfterDrop = 0.08f;
        [SerializeField] AK.Wwise.Event coinIntoWater;   // im Inspector Play_Coin_IntoWater zuweisen


        void OnEnable()
        {
            ResolveReferences();

            if (gameManager != null)
                gameManager.DropCommitted += OnDropCommitted;
        }

        void OnDisable()
        {
            if (gameManager != null)
                gameManager.DropCommitted -= OnDropCommitted;
        }

        public void Configure(GameManager manager, Transform target)
        {
            if (gameManager != null)
                gameManager.DropCommitted -= OnDropCommitted;

            gameManager = manager;
            glassTarget = target;

            if (isActiveAndEnabled && gameManager != null)
                gameManager.DropCommitted += OnDropCommitted;
        }

        void OnDropCommitted(TurnActor actor, IReadOnlyList<Coin> coins)
        {
            if (coins == null || coins.Count == 0)
                return;

            var targetPosition = GetGlassTargetPosition();

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                var proxy = CreateCoinProxy(coins[i], actor, i);
                StartCoroutine(AnimateProxyDrop(proxy, targetPosition, actor, i));
            }

            Debug.Log(
                $"[CoinDropPresentationController] Started {coins.Count} coin drop proxy animation(s) " +
                $"for {actor} toward {targetPosition}.");
        }

        GameObject CreateCoinProxy(Coin sourceCoin, TurnActor actor, int index)
        {
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            proxy.name = $"{actor} Drop Proxy {sourceCoin.name}";
            proxy.transform.position = sourceCoin.transform.position;
            proxy.transform.rotation = sourceCoin.transform.rotation;
            proxy.transform.localScale = sourceCoin.transform.lossyScale;

            var collider = proxy.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var sourceRenderer = sourceCoin.GetComponentInChildren<Renderer>();
            var proxyRenderer = proxy.GetComponent<Renderer>();

            if (sourceRenderer != null && proxyRenderer != null)
                proxyRenderer.sharedMaterial = sourceRenderer.sharedMaterial;

            proxy.transform.position += new Vector3(index * 0.03f, 0.02f, -index * 0.02f);
            return proxy;
        }

        IEnumerator AnimateProxyDrop(GameObject proxy, Vector3 targetPosition, TurnActor actor, int index)
        {
            if (proxy == null)
                yield break;

            var start = proxy.transform.position;
            var hold = CalculateRimHoldPosition(targetPosition, actor, index);
            var end = targetPosition + new Vector3((index - 0.5f) * 0.035f, -0.02f, index * 0.02f);
            var elapsed = 0f;

            while (elapsed < dropAnimationSeconds && proxy != null)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / dropAnimationSeconds);
                proxy.transform.position = CalculateStagedDropPosition(start, hold, end, normalized, liftArcHeight);
                proxy.transform.Rotate(
                    normalized < 0.58f ? 0f : 180f * Time.deltaTime,
                    520f * Time.deltaTime,
                    normalized < 0.72f ? 90f * Time.deltaTime : 420f * Time.deltaTime,
                    Space.Self);
                yield return null;
            }

            if (proxy == null)
                yield break;

            proxy.transform.position = end;

             var emitter = glassTarget != null ? glassTarget.gameObject : gameObject;
        coinIntoWater?.Post(emitter);          // Splash am Glas 

            Destroy(proxy, proxyLifetimeAfterDrop);


        }

        static Vector3 CalculateRimHoldPosition(Vector3 targetPosition, TurnActor actor, int index)
        {
            var side = index % 2 == 0 ? -1f : 1f;
            var actorDepth = actor == TurnActor.Player ? -0.15f : 0.15f;
            return targetPosition + new Vector3(side * 0.24f, 0.22f, actorDepth);
        }

        Vector3 GetGlassTargetPosition()
        {
            if (glassTarget == null)
            {
                var glassObject = GameObject.FindGameObjectWithTag("Glass");

                if (glassObject != null)
                    glassTarget = glassObject.transform;
            }

            return glassTarget == null
                ? Vector3.up
                : glassTarget.position + Vector3.up * 0.42f;
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();
        }

        public static Vector3 CalculateArcPosition(Vector3 start, Vector3 end, float normalizedTime, float height)
        {
            var t = Mathf.Clamp01(normalizedTime);
            var linear = Vector3.Lerp(start, end, t);
            var arc = Mathf.Sin(t * Mathf.PI) * Mathf.Max(0f, height);
            return linear + Vector3.up * arc;
        }

        public static Vector3 CalculateStagedDropPosition(
            Vector3 start,
            Vector3 hold,
            Vector3 end,
            float normalizedTime,
            float liftArcHeight)
        {
            const float liftEnd = 0.58f;
            const float holdEnd = 0.72f;

            var t = Mathf.Clamp01(normalizedTime);

            if (t <= liftEnd)
            {
                var liftT = Mathf.SmoothStep(0f, 1f, t / liftEnd);
                return CalculateArcPosition(start, hold, liftT, liftArcHeight);
            }

            if (t <= holdEnd)
                return hold;

            var releaseT = Mathf.SmoothStep(0f, 1f, (t - holdEnd) / (1f - holdEnd));
            var sideSag = Mathf.Sin(releaseT * Mathf.PI) * 0.035f;
            return Vector3.Lerp(hold, end, releaseT) + Vector3.down * sideSag;
        }
    }
}
