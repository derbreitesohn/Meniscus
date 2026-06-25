using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// The "looking through the spyglass" overlay shown only while the Spyglass reveal plays: a circular lens
    /// vignette (with a brass rim) that irises over the screen and a big '% SPILL' gauge reading the exact
    /// odds the next pour faces. Driven by <see cref="Gameplay.ItemUsePresentationController"/>, which irises
    /// the scope in (<see cref="SetScopeAmount"/>), feeds the number (<see cref="SetOdds"/>), then irises out
    /// and calls <see cref="Hide"/>. It is a one-shot reveal — nothing lingers on the HUD after the item is
    /// used. Self-contained and built procedurally (no authored assets); mirrors <see cref="CoinSelectionPreview"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpyglassScopeView : MonoBehaviour
    {
        public static SpyglassScopeView Create() =>
            new GameObject("Spyglass Scope View").AddComponent<SpyglassScopeView>();

        static readonly Color CalmOddsColor = new(1f, 0.84f, 0.33f, 1f);   // gold — low danger
        static readonly Color HotOddsColor = new(1f, 0.30f, 0.18f, 1f);    // hot red — near-certain spill
        static readonly Color LensRimColor = new(0.78f, 0.58f, 0.28f, 1f); // brass lens edge

        // Normalised lens geometry baked into the procedural sprites (distance from texture centre, 0..~1).
        const float HoleInner = 0.32f;       // transparent inside this radius (you see through the lens)
        const float HoleOuter = 0.46f;       // fully black beyond this (the surround)
        const float RingHalfWidth = 0.022f;  // half-thickness of the brass rim, centred on HoleInner

        // Lens container scale: wide open (hole off-screen, no vignette) vs scope engaged (lens frames centre).
        const float LensOpenScale = 3.4f;
        const float LensScopeScale = 1f;
        const float LensSquareSize = 2600f;  // square that covers the 1920×1080 reference screen at scope scale
        const float GaugeOffsetY = -190f;     // gauge sits in the lower part of the lens

        Canvas canvas;
        RectTransform lens;        // holds the vignette + rim; scaled for the iris
        Image vignetteImage;
        Image lensRimImage;
        RectTransform gaugePanel;
        Text oddsText;

        void OnEnable() => BuildUi();

        /// <summary>Open the scope overlay for a reveal (lens starts off the eye; iris in via <see cref="SetScopeAmount"/>).</summary>
        public void Show()
        {
            SetVisible(true);

            gaugePanel.anchoredPosition = new Vector2(0f, GaugeOffsetY);
            SetScopeAmount(0f);
        }

        /// <summary>Tear the overlay down at the end of the reveal — nothing lingers after the item is used.</summary>
        public void Hide() => SetVisible(false);

        /// <summary>0 = scope off the eye (lens wide / faded out), 1 = scope fully engaged over the view.</summary>
        public void SetScopeAmount(float amount01)
        {
            if (lens == null)
                return;

            var a = Mathf.Clamp01(amount01);

            var scale = Mathf.Lerp(LensOpenScale, LensScopeScale, a);
            lens.localScale = new Vector3(scale, scale, 1f);

            SetImageAlpha(vignetteImage, a);
            SetImageAlpha(lensRimImage, a);
            SetTextAlpha(oddsText, a);
        }

        /// <summary>Set the gauge to a spill chance (0..100 %) tinted by danger (0..1, amber→red).</summary>
        public void SetOdds(float spillChance, float danger01)
        {
            if (oddsText == null)
                return;

            var color = Color.Lerp(CalmOddsColor, HotOddsColor, Mathf.Clamp01(danger01));
            color.a = oddsText.color.a;   // preserve the current fade
            oddsText.color = color;
            oddsText.text = $"{Mathf.RoundToInt(spillChance)}%\n<size=34>SPILL</size>";
        }

        void SetVisible(bool visible)
        {
            if (canvas != null && canvas.enabled != visible)
                canvas.enabled = visible;
        }

        void BuildUi()
        {
            if (canvas != null)
                return;

            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Spyglass Scope Canvas");
            canvas.sortingOrder = 200;   // above the table HUD and the payout preview
            canvas.GetComponent<RectTransform>();

            // The lens: a black mask with a transparent circular hole, plus a brass rim, scaled for the iris.
            var lensObject = new GameObject("Lens", typeof(RectTransform));
            lensObject.transform.SetParent(canvas.transform, false);
            lens = lensObject.GetComponent<RectTransform>();
            lens.anchorMin = lens.anchorMax = lens.pivot = new Vector2(0.5f, 0.5f);
            lens.sizeDelta = new Vector2(LensSquareSize, LensSquareSize);

            vignetteImage = CreateLensImage(lens, "Vignette", BuildVignetteSprite(), Color.black);
            lensRimImage = CreateLensImage(lens, "Lens Rim", BuildRingSprite(), LensRimColor);

            // The gauge: the big % SPILL read, centred in the lower part of the lens.
            var gaugeObject = new GameObject("Odds Gauge", typeof(RectTransform));
            gaugeObject.transform.SetParent(canvas.transform, false);
            gaugePanel = gaugeObject.GetComponent<RectTransform>();
            gaugePanel.anchorMin = gaugePanel.anchorMax = gaugePanel.pivot = new Vector2(0.5f, 0.5f);
            gaugePanel.sizeDelta = new Vector2(520f, 240f);
            gaugePanel.anchoredPosition = new Vector2(0f, GaugeOffsetY);

            oddsText = RuntimeUiFactory.CreateText(
                gaugePanel, "Odds", "", Vector2.zero, new Vector2(520f, 240f),
                120, CalmOddsColor, TextAnchor.MiddleCenter, true);
            oddsText.supportRichText = true;
            oddsText.horizontalOverflow = HorizontalWrapMode.Overflow;
            oddsText.verticalOverflow = VerticalWrapMode.Overflow;
            oddsText.raycastTarget = false;
            AddReadableOutline(oddsText);

            canvas.enabled = false;
        }

        // A lens layer (vignette / rim): full square, centred, never a raycast target so it never blocks the
        // desk's own click raycast (input is gated by the GameManager lock, not the UI).
        static Image CreateLensImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);

            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(LensSquareSize, LensSquareSize);

            var image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void SetImageAlpha(Image image, float alpha)
        {
            if (image == null)
                return;

            var c = image.color;
            c.a = alpha;
            image.color = c;
        }

        static void SetTextAlpha(Text text, float alpha)
        {
            if (text == null)
                return;

            var c = text.color;
            c.a = alpha;
            text.color = c;
        }

        static void AddReadableOutline(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
        }

        // A black mask: transparent inside HoleInner, ramping to opaque black by HoleOuter. Scaling the lens
        // container irises the hole over the screen.
        static Sprite BuildVignetteSprite()
        {
            const int size = 256;
            var tex = NewTexture(size);
            var center = (size - 1) * 0.5f;
            var maxRadius = size * 0.5f;
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - center) / maxRadius;
                    var dy = (y - center) / maxRadius;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoleInner, HoleOuter, r));
                    pixels[y * size + x] = new Color(0f, 0f, 0f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return ToSprite(tex, size);
        }

        // A thin white ring centred on HoleInner (tinted brass via the Image colour), so the lens reads as a
        // real optic edge rather than a bare hole.
        static Sprite BuildRingSprite()
        {
            const int size = 256;
            var tex = NewTexture(size);
            var center = (size - 1) * 0.5f;
            var maxRadius = size * 0.5f;
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - center) / maxRadius;
                    var dy = (y - center) / maxRadius;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var band = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(Mathf.Abs(r - HoleInner) / RingHalfWidth));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, band);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return ToSprite(tex, size);
        }

        static Texture2D NewTexture(int size) =>
            new(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

        static Sprite ToSprite(Texture2D tex, int size) =>
            Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
