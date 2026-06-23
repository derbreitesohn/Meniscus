using System.Collections;
using Meniscus.Core;
using Meniscus.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Top-right wallet HUD: a single counter for the player's money "in general" — the live total
    /// of banked cash plus the current at-risk hand. Each safe drop's payout rolls the counter up
    /// and floats a "+$X" up out of the glass water in the centre of the screen; a bust rolls it
    /// back down to the banked amount.
    /// </summary>
    [DisallowMultipleComponent]
    public class MoneyHudWidget : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] EconomyManager economyManager;

        Canvas _canvas;
        Text _amount;

        Transform _glass;
        GlassVisualController _glassVisual;
        Camera _camera;

        int _displayedTotal;
        Coroutine _roll;

        static readonly Color PanelBg    = new Color(0.085f, 0.045f, 0.018f, 0.92f);
        static readonly Color BorderCol  = new Color(0.72f,  0.54f,  0.17f,  0.78f);
        static readonly Color GoldBright = new Color(0.97f,  0.83f,  0.31f,  1f);
        static readonly Color GoldDim    = new Color(0.62f,  0.47f,  0.16f,  1f);
        static readonly Color RedFlash   = new Color(0.95f,  0.22f,  0.13f,  1f);
        static readonly Color FloatShadow = new Color(0f, 0f, 0f, 0.65f);
        // Hot amber the "+$X" shifts toward as the multiplier climbs (greed/combo payouts read hotter).
        static readonly Color MultHot    = new Color(1f,    0.55f,  0.18f,  1f);

        const float PanelW = 224f;
        const float PanelH = 86f;
        const float Margin = 24f;

        // Fallback anchor (viewport fraction) when the glass/camera can't be resolved.
        static readonly Vector2 ScreenCentre = new Vector2(0.5f, 0.55f);

        // ─── lifecycle ────────────────────────────────────────────────────────────

        void Awake()
        {
            ResolveReferences();
            BuildUI();
            Subscribe();
            SnapToEconomy();
        }

        void OnDestroy() => Unsubscribe();

        public void Configure(GameManager gm, EconomyManager eco)
        {
            gameManager = gm;
            economyManager = eco;
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();
            if (economyManager == null)
                economyManager = FindAnyObjectByType<EconomyManager>();
        }

        // ─── events ───────────────────────────────────────────────────────────────

        void Subscribe()
        {
            if (economyManager != null)
            {
                economyManager.MoneyAwarded       += OnMoneyAwarded;
                economyManager.RoundEarningsWiped += OnRoundEarningsWiped;
                economyManager.EarningsBanked     += OnEarningsBanked;
            }

            if (gameManager != null)
                gameManager.RoundStarted += OnRoundStarted;
        }

        void Unsubscribe()
        {
            if (economyManager != null)
            {
                economyManager.MoneyAwarded       -= OnMoneyAwarded;
                economyManager.RoundEarningsWiped -= OnRoundEarningsWiped;
                economyManager.EarningsBanked     -= OnEarningsBanked;
            }

            if (gameManager != null)
                gameManager.RoundStarted -= OnRoundStarted;
        }

        void OnRoundStarted(int _)
        {
            // Round earnings have just been reset, so the live total is simply the bank.
            StopRoll();
            _displayedTotal = LiveTotal();
            SetAmount(_displayedTotal, GoldBright);
        }

        void OnMoneyAwarded(int payout, int newRoundTotal)
        {
            var multiplier = economyManager != null ? economyManager.LastSafeDropMultiplier : 1f;
            var combo = economyManager != null && economyManager.LastSafeDropComboApplied;
            StartCoroutine(FloatGainOverWater(payout, multiplier, combo));
            StartRoll(LiveTotal(), GoldBright, flashRed: false, punch: true);
        }

        // Player busted: the at-risk hand is gone, so the wallet drops back to the banked amount.
        void OnRoundEarningsWiped() =>
            StartRoll(LiveTotal(), GoldDim, flashRed: true, punch: false);

        // Banking moves the (already-counted) hand into the bank, so the live total is unchanged.
        // Pulse to signal the money is now locked in.
        void OnEarningsBanked(int earned, int newBankTotal) =>
            StartRoll(LiveTotal(), GoldBright, flashRed: false, punch: true);

        // ─── counter animation ──────────────────────────────────────────────────────

        int LiveTotal() =>
            economyManager == null
                ? 0
                : economyManager.PlayerTotalBankedCash + economyManager.CurrentRoundEarnings;

        void StartRoll(int to, Color endColor, bool flashRed, bool punch)
        {
            StopRoll();
            _roll = StartCoroutine(RollRoutine(to, endColor, flashRed, punch));
        }

        void StopRoll()
        {
            if (_roll != null) { StopCoroutine(_roll); _roll = null; }
        }

        IEnumerator RollRoutine(int to, Color endColor, bool flashRed, bool punch)
        {
            if (punch)
                StartCoroutine(PunchScale(_amount.rectTransform, 1.3f, 0.4f));

            var from = _displayedTotal;
            var startColor = flashRed ? RedFlash : endColor;
            const float dur = 0.5f;

            for (var t = 0f; t < dur; t += Time.deltaTime)
            {
                var p = Mathf.SmoothStep(0f, 1f, t / dur);
                _displayedTotal = Mathf.RoundToInt(Mathf.Lerp(from, to, p));
                _amount.text  = Dollars(_displayedTotal);
                _amount.color = Color.Lerp(startColor, endColor, p);
                yield return null;
            }

            _displayedTotal = to;
            SetAmount(to, endColor);
            _roll = null;
        }

        IEnumerator PunchScale(RectTransform rt, float peak, float dur)
        {
            for (var t = 0f; t < dur; t += Time.deltaTime)
            {
                var p = t / dur;
                var s = p < 0.35f
                    ? Mathf.Lerp(1f, peak, p / 0.35f)
                    : Mathf.Lerp(peak, 1f, (p - 0.35f) / 0.65f);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            rt.localScale = Vector3.one;
        }

        // ─── "+$X" floating up out of the water ───────────────────────────────────────

        IEnumerator FloatGainOverWater(int amount, float multiplier, bool combo)
        {
            if (amount == 0)
                yield break;

            var anchor = ResolveWaterViewportAnchor();

            // A real bonus (greed and/or combo, or a shop multiplier) reads hotter and pops harder, so a
            // bigger or riskier pour feels earned. A plain ×1 drop keeps the calm gold look.
            var showMult = multiplier >= 1.1f;
            var heat = Mathf.Clamp01(Mathf.InverseLerp(1f, 3f, multiplier));
            var amountColor = showMult ? Color.Lerp(GoldBright, MultHot, heat) : GoldBright;
            var peak = showMult ? Mathf.Lerp(1.18f, 1.5f, heat) : 1.12f;

            var go = new GameObject("MoneyGain", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(_canvas.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(380f, 120f);
            rt.anchoredPosition = Vector2.zero;

            var txt = go.GetComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 62;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text      = $"+${amount}";
            txt.color     = amountColor;

            // Dark outline keeps the gold legible over the glass and water behind it.
            var outline = go.GetComponent<Outline>();
            outline.effectColor    = FloatShadow;
            outline.effectDistance = new Vector2(2.5f, -2.5f);

            // "×2.4  COMBO" badge under the amount, shown only when a real multiplier applied, so the
            // player sees why a greedy or multi-coin pour paid out more.
            Text badgeTxt = null;
            Outline badgeOutline = null;
            if (showMult)
            {
                var label = combo ? $"×{multiplier:0.0}  COMBO" : $"×{multiplier:0.0}";
                var badgeGo = new GameObject("MoneyGainMult", typeof(RectTransform), typeof(Text), typeof(Outline));
                badgeGo.transform.SetParent(rt, false);

                var brt = badgeGo.GetComponent<RectTransform>();
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = new Vector2(380f, 44f);
                brt.anchoredPosition = new Vector2(0f, -48f);

                badgeTxt = badgeGo.GetComponent<Text>();
                badgeTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                badgeTxt.fontSize  = 32;
                badgeTxt.fontStyle = FontStyle.Bold;
                badgeTxt.alignment = TextAnchor.MiddleCenter;
                badgeTxt.text      = label;
                badgeTxt.color     = amountColor;

                badgeOutline = badgeGo.GetComponent<Outline>();
                badgeOutline.effectColor    = FloatShadow;
                badgeOutline.effectDistance = new Vector2(2f, -2f);
            }

            const float dur  = 1.35f;
            const float rise = 150f;

            for (var t = 0f; t < dur; t += Time.deltaTime)
            {
                var p = t / dur;

                // A small overshoot pop on entry, then settle to full size.
                var s = p < 0.18f
                    ? Mathf.Lerp(0.55f, peak, p / 0.18f)
                    : Mathf.Lerp(peak, 1f, Mathf.Min(1f, (p - 0.18f) / 0.30f));
                rt.localScale = new Vector3(s, s, 1f);

                rt.anchoredPosition = new Vector2(0f, Mathf.SmoothStep(0f, rise, p));

                var alpha = p < 0.55f ? 1f : Mathf.SmoothStep(1f, 0f, (p - 0.55f) / 0.45f);
                txt.color = new Color(amountColor.r, amountColor.g, amountColor.b, alpha);
                outline.effectColor = new Color(FloatShadow.r, FloatShadow.g, FloatShadow.b, FloatShadow.a * alpha);

                if (badgeTxt != null)
                {
                    badgeTxt.color = new Color(amountColor.r, amountColor.g, amountColor.b, alpha);
                    badgeOutline.effectColor = new Color(FloatShadow.r, FloatShadow.g, FloatShadow.b, FloatShadow.a * alpha);
                }

                yield return null;
            }

            Destroy(go);
        }

        // Viewport-fraction anchor (0..1) over the glass's liquid surface, so the float reads as
        // rising out of the water. Falls back to screen centre when the glass/camera is unavailable.
        Vector2 ResolveWaterViewportAnchor()
        {
            EnsureGlassReferences();

            var cam = ResolveCamera();
            if (cam != null && _glass != null && _glassVisual != null)
            {
                var world = _glass.TransformPoint(new Vector3(0f, _glassVisual.StableSurfaceLocalY, 0f));
                var vp = cam.WorldToViewportPoint(world);

                if (vp.z > 0f)
                    return new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
            }

            return ScreenCentre;
        }

        void EnsureGlassReferences()
        {
            if (_glass == null)
            {
                var glassObject = GameObject.FindGameObjectWithTag("Glass");
                if (glassObject != null)
                    _glass = glassObject.transform;
            }

            if (_glassVisual == null && _glass != null)
                _glassVisual = _glass.GetComponent<GlassVisualController>();

            if (_glassVisual == null)
                _glassVisual = FindAnyObjectByType<GlassVisualController>();

            if (_glass == null && _glassVisual != null)
                _glass = _glassVisual.transform;
        }

        Camera ResolveCamera()
        {
            if (Camera.main != null)
                return Camera.main;

            if (_camera == null)
                _camera = FindAnyObjectByType<Camera>();

            return _camera;
        }

        // ─── helpers ──────────────────────────────────────────────────────────────

        void SnapToEconomy()
        {
            _displayedTotal = LiveTotal();
            SetAmount(_displayedTotal, GoldBright);
        }

        void SetAmount(int amount, Color color)
        {
            if (_amount == null) return;
            _amount.text  = Dollars(amount);
            _amount.color = color;
        }

        static string Dollars(int n) => $"${n}";

        // ─── UI construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            _canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Money HUD Canvas");
            _canvas.sortingOrder = 1;

            // Gold border behind the panel, pinned to the top-right corner.
            var border = MakeRect(_canvas.transform, "Money Border");
            AnchorTopRight(border, PanelW + 4f, PanelH + 4f, Margin - 2f);
            border.gameObject.AddComponent<Image>().color = BorderCol;

            // Dark main panel.
            var panel = MakeRect(_canvas.transform, "Money Panel");
            AnchorTopRight(panel, PanelW, PanelH, Margin);
            panel.gameObject.AddComponent<Image>().color = PanelBg;

            // "MONEY" caption across the top.
            AddText(panel, "MoneyLabel", "MONEY",
                new Vector2(0f, 0.56f), new Vector2(1f, 1f),
                new Vector2(10f, 2f),   new Vector2(-10f, -4f),
                12, GoldDim, TextAnchor.UpperCenter);

            // The single live-total amount.
            _amount = AddText(panel, "MoneyAmount", "$0",
                new Vector2(0f, 0f),  new Vector2(1f, 0.62f),
                new Vector2(10f, 4f), new Vector2(-10f, -2f),
                40, GoldBright, TextAnchor.MiddleCenter, bold: true);
        }

        // Pins a rect to the top-right corner, inset by `margin`, with the given size.
        static void AnchorTopRight(RectTransform rt, float width, float height, float margin)
        {
            rt.anchorMin = rt.anchorMax = Vector2.one;
            rt.pivot     = Vector2.one;
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-margin, -margin);
        }

        static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static Text AddText(
            RectTransform parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax,
            int fontSize, Color color, TextAnchor align, bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var txt = go.GetComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = fontSize;
            txt.text      = content;
            txt.color     = color;
            txt.alignment = align;

            if (bold)
                txt.fontStyle = FontStyle.Bold;

            return txt;
        }
    }
}
