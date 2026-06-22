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
        [SerializeField, Range(0f, 45f)] float carryTiltDegrees = 12f;   // gentle bank while the coin is carried
        [SerializeField, Range(0f, 120f)] float releaseTipDegrees = 40f; // leading edge dips in as it is released
        [SerializeField] AK.Wwise.Event coinIntoWater;   // assign Play_Coin_IntoWater in the Inspector

        const float LiftPhaseEnd = 0.58f;
        const float HoldPhaseEnd = 0.72f;

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
        }

        GameObject CreateCoinProxy(Coin sourceCoin, TurnActor actor, int index)
        {
            // Fly the coin's *visible* mesh so a model coin flies as itself and a placeholder coin still
            // flies as its cylinder. When a model is showing, the root cylinder renderer is disabled, so we
            // clone the coin's actual model (preserving multi-part meshes and materials) and only fall back
            // to mirroring the cylinder body when no model is present.
            var visibleModel = sourceCoin.ActiveModel;
            GameObject proxy;
            Transform visualTransform;

            if (visibleModel != null)
            {
                proxy = Instantiate(visibleModel);
                visualTransform = visibleModel.transform;
            }
            else
            {
                var bodyRenderer = sourceCoin.GetComponentInChildren<Renderer>();
                var bodyFilter = sourceCoin.GetComponentInChildren<MeshFilter>();
                visualTransform = bodyRenderer != null ? bodyRenderer.transform : sourceCoin.transform;

                proxy = GameObject.CreatePrimitive(PrimitiveType.Cylinder);

                var proxyFilter = proxy.GetComponent<MeshFilter>();
                if (bodyFilter != null && bodyFilter.sharedMesh != null && proxyFilter != null)
                    proxyFilter.sharedMesh = bodyFilter.sharedMesh;

                var proxyRenderer = proxy.GetComponent<Renderer>();
                if (bodyRenderer != null && proxyRenderer != null)
                    proxyRenderer.sharedMaterial = bodyRenderer.sharedMaterial;
            }

            proxy.name = $"{actor} Drop Proxy {sourceCoin.name}";
            proxy.transform.position = visualTransform.position;
            proxy.transform.rotation = visualTransform.rotation;
            proxy.transform.localScale = visualTransform.lossyScale;

            // A proxy is purely visual; strip any colliders the primitive or model brought along.
            foreach (var proxyCollider in proxy.GetComponentsInChildren<Collider>())
                Destroy(proxyCollider);

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

            var startRotation = proxy.transform.rotation;
            var carryRotation = CalculateCarryRotation(startRotation, start, hold, carryTiltDegrees);
            var releaseRotation = CalculateReleaseRotation(carryRotation, hold, end, releaseTipDegrees);
            var elapsed = 0f;

            while (elapsed < dropAnimationSeconds && proxy != null)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / dropAnimationSeconds);
                proxy.transform.position = CalculateStagedDropPosition(start, hold, end, normalized, liftArcHeight);
                proxy.transform.rotation = CalculateStagedDropRotation(startRotation, carryRotation, releaseRotation, normalized);
                yield return null;
            }

            if (proxy == null)
                yield break;

            proxy.transform.position = end;
            proxy.transform.rotation = releaseRotation;

            var emitter = glassTarget != null ? glassTarget.gameObject : gameObject;
            coinIntoWater?.Post(emitter);   // splash at the glass

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
            var t = Mathf.Clamp01(normalizedTime);

            if (t <= LiftPhaseEnd)
            {
                var liftT = Mathf.SmoothStep(0f, 1f, t / LiftPhaseEnd);
                return CalculateArcPosition(start, hold, liftT, liftArcHeight);
            }

            if (t <= HoldPhaseEnd)
                return hold;

            var releaseT = Mathf.SmoothStep(0f, 1f, (t - HoldPhaseEnd) / (1f - HoldPhaseEnd));
            var sideSag = Mathf.Sin(releaseT * Mathf.PI) * 0.035f;
            return Vector3.Lerp(hold, end, releaseT) + Vector3.down * sideSag;
        }

        public static Quaternion CalculateStagedDropRotation(
            Quaternion start,
            Quaternion carry,
            Quaternion release,
            float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);

            if (t <= LiftPhaseEnd)
            {
                var liftT = Mathf.SmoothStep(0f, 1f, t / LiftPhaseEnd);
                return Quaternion.Slerp(start, carry, liftT);
            }

            if (t <= HoldPhaseEnd)
                return carry;

            var releaseT = Mathf.SmoothStep(0f, 1f, (t - HoldPhaseEnd) / (1f - HoldPhaseEnd));
            return Quaternion.Slerp(carry, release, releaseT);
        }

        static Quaternion CalculateCarryRotation(Quaternion start, Vector3 from, Vector3 to, float tiltDegrees)
        {
            var travel = to - from;
            travel.y = 0f;

            if (travel.sqrMagnitude < 1e-5f)
                return start;

            // Bank the coin toward the way it is being carried, as if pinched steady between fingers.
            var bankAxis = Vector3.Cross(Vector3.up, travel.normalized);
            return Quaternion.AngleAxis(tiltDegrees, bankAxis) * start;
        }

        static Quaternion CalculateReleaseRotation(Quaternion carry, Vector3 from, Vector3 to, float tipDegrees)
        {
            var travel = to - from;
            travel.y = 0f;

            // Tip the leading edge down so the coin slides into the water as the fingers let go, not spinning.
            var tipAxis = travel.sqrMagnitude < 1e-5f
                ? Vector3.right
                : Vector3.Cross(Vector3.up, travel.normalized);

            return Quaternion.AngleAxis(tipDegrees, tipAxis) * carry;
        }
    }
}
