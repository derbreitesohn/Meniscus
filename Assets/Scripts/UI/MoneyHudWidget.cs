using System.Collections;
using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    [DisallowMultipleComponent]
    public class MoneyHudWidget : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] EconomyManager economyManager;

        Canvas _canvas;
        RectTransform _panelRect;
        Text _roundAmount;
        Text _bankAmount;

        int _displayedRound;
        int _displayedBank;

        Coroutine _roundAnim;
        Coroutine _bankAnim;

        static readonly Color PanelBg     = new Color(0.085f, 0.045f, 0.018f, 0.92f);
        static readonly Color BorderCol   = new Color(0.72f,  0.54f,  0.17f,  0.78f);
        static readonly Color GoldBright  = new Color(0.97f,  0.83f,  0.31f,  1f);
        static readonly Color GoldDim     = new Color(0.55f,  0.41f,  0.13f,  1f);
        static readonly Color RedFlash    = new Color(0.95f,  0.22f,  0.13f,  1f);

        const float PanelW = 280f;
        const float PanelH = 100f;
        const float PanelX = 24f;
        const float PanelY = 24f;

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
                economyManager.MoneyAwarded     += OnMoneyAwarded;
                economyManager.RoundEarningsWiped += OnRoundEarningsWiped;
                economyManager.EarningsBanked   += OnEarningsBanked;
            }

            if (gameManager != null)
                gameManager.RoundStarted += OnRoundStarted;
        }

        void Unsubscribe()
        {
            if (economyManager != null)
            {
                economyManager.MoneyAwarded     -= OnMoneyAwarded;
                economyManager.RoundEarningsWiped -= OnRoundEarningsWiped;
                economyManager.EarningsBanked   -= OnEarningsBanked;
            }

            if (gameManager != null)
                gameManager.RoundStarted -= OnRoundStarted;
        }

        void OnRoundStarted(int _)
        {
            StopCounterAnims();
            _displayedRound = 0;
            _displayedBank  = economyManager == null ? 0 : economyManager.PlayerTotalBankedCash;
            SetAmount(_roundAmount, 0,             GoldBright);
            SetAmount(_bankAmount,  _displayedBank, GoldBright);
        }

        void OnMoneyAwarded(int payout, int newRoundTotal)
        {
            if (_roundAnim != null) StopCoroutine(_roundAnim);
            _roundAnim = StartCoroutine(GainRoutine(payout, newRoundTotal));
        }

        void OnRoundEarningsWiped()
        {
            if (_roundAnim != null) StopCoroutine(_roundAnim);
            _roundAnim = StartCoroutine(BustRoutine());
        }

        void OnEarningsBanked(int earned, int newBankTotal)
        {
            StopCounterAnims();
            _bankAnim = StartCoroutine(BankRoutine(newBankTotal));
        }

        // ─── animations ───────────────────────────────────────────────────────────

        IEnumerator GainRoutine(int payout, int to)
        {
            var from = _displayedRound;
            StartCoroutine(PunchScale(_roundAmount.rectTransform, 1.38f, 0.40f));
            StartCoroutine(FloatGain(payout));

            const float dur = 0.50f;
            for (var t = 0f; t < dur; t += Time.deltaTime)
            {
                var p = Mathf.SmoothStep(0f, 1f, t / dur);
                _displayedRound = Mathf.RoundToInt(Mathf.Lerp(from, to, p));
                SetAmount(_roundAmount, _displayedRound, GoldBright);
                yield return null;
            }

            _displayedRound = to;
            SetAmount(_roundAmount, to, GoldBright);
            _roundAnim = null;
        }

        IEnumerator BustRoutine()
        {
            var from = _displayedRound;
            _roundAmount.color = RedFlash;

            const float dur = 0.45f;
            for (var t = 0f; t < dur; t += Time.deltaTime)
            {
                var p = Mathf.SmoothStep(0f, 1f, t / dur);
                _displayedRound = Mathf.RoundToInt(Mathf.Lerp(from, 0, p));
                _roundAmount.text  = Dollars(_displayedRound);
                _roundAmount.color = Color.Lerp(RedFlash, GoldDim, p);
                yield return null;
            }

            _displayedRound = 0;
            SetAmount(_roundAmount, 0, GoldDim);
            _roundAnim = null;
        }

        IEnumerator BankRoutine(int newBankTotal)
        {
            // Round counter drains to zero
            var fromRound = _displayedRound;
            const float drainDur = 0.35f;
            for (var t = 0f; t < drainDur; t += Time.deltaTime)
            {
                var p = Mathf.SmoothStep(0f, 1f, t / drainDur);
                _displayedRound = Mathf.RoundToInt(Mathf.Lerp(fromRound, 0, p));
                SetAmount(_roundAmount, _displayedRound, Color.Lerp(GoldBright, GoldDim, p));
                yield return null;
            }

            _displayedRound = 0;
            SetAmount(_roundAmount, 0, GoldDim);

            // Bank counter rolls up
            StartCoroutine(PunchScale(_bankAmount.rectTransform, 1.28f, 0.42f));
            var fromBank = _displayedBank;
            const float fillDur = 0.55f;
            for (var t = 0f; t < fillDur; t += Time.deltaTime)
            {
                var p = Mathf.SmoothStep(0f, 1f, t / fillDur);
                _displayedBank = Mathf.RoundToInt(Mathf.Lerp(fromBank, newBankTotal, p));
                SetAmount(_bankAmount, _displayedBank, GoldBright);
                yield return null;
            }

            _displayedBank = newBankTotal;
            SetAmount(_bankAmount, newBankTotal, GoldBright);
            _bankAnim = null;
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

        IEnumerator FloatGain(int amount)
        {
            var go = new GameObject("FloatGain", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_canvas.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(160f, 44f);

            // Spawn just above the THIS HAND section
            rt.anchoredPosition = new Vector2(
                PanelX + PanelW * 0.25f,
                PanelY + PanelH + 6f);

            var txt = go.GetComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 30;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text      = $"+${amount}";
            txt.color     = GoldBright;

            var basePos = rt.anchoredPosition;
            const float dur = 1.3f;

            for (var t = 0f; t < dur; t += Time.deltaTime)
            {
                var p     = t / dur;
                var alpha = p < 0.25f ? 1f : Mathf.SmoothStep(1f, 0f, (p - 0.25f) / 0.75f);
                rt.anchoredPosition = basePos + new Vector2(0f, Mathf.Lerp(0f, 72f, p));
                txt.color = new Color(GoldBright.r, GoldBright.g, GoldBright.b, alpha);
                yield return null;
            }

            Destroy(go);
        }

        // ─── helpers ──────────────────────────────────────────────────────────────

        void StopCounterAnims()
        {
            if (_roundAnim != null) { StopCoroutine(_roundAnim); _roundAnim = null; }
            if (_bankAnim  != null) { StopCoroutine(_bankAnim);  _bankAnim  = null; }
        }

        void SnapToEconomy()
        {
            _displayedRound = economyManager == null ? 0 : economyManager.CurrentRoundEarnings;
            _displayedBank  = economyManager == null ? 0 : economyManager.PlayerTotalBankedCash;
            SetAmount(_roundAmount, _displayedRound, GoldBright);
            SetAmount(_bankAmount,  _displayedBank,  GoldBright);
        }

        static void SetAmount(Text label, int amount, Color color)
        {
            if (label == null) return;
            label.text  = Dollars(amount);
            label.color = color;
        }

        static string Dollars(int n) => $"${n}";

        // ─── UI construction ──────────────────────────────────────────────────────

        void BuildUI()
        {
            _canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Money HUD Canvas");
            _canvas.sortingOrder = 1;

            // 2-px gold border behind the panel
            var border = MakeRect(_canvas.transform, "Money Border");
            border.anchorMin = border.anchorMax = Vector2.zero;
            border.pivot     = Vector2.zero;
            border.sizeDelta = new Vector2(PanelW + 4f, PanelH + 4f);
            border.anchoredPosition = new Vector2(PanelX - 2f, PanelY - 2f);
            border.gameObject.AddComponent<Image>().color = BorderCol;

            // Dark main panel
            var panel = MakeRect(_canvas.transform, "Money Panel");
            _panelRect = panel;
            panel.anchorMin = panel.anchorMax = Vector2.zero;
            panel.pivot     = Vector2.zero;
            panel.sizeDelta = new Vector2(PanelW, PanelH);
            panel.anchoredPosition = new Vector2(PanelX, PanelY);
            panel.gameObject.AddComponent<Image>().color = PanelBg;

            // Section labels — "THIS HAND" (left) and "BANK" (right)
            AddText(panel, "HandLabel", "THIS HAND",
                new Vector2(0f,   0.52f), new Vector2(0.5f, 1f),
                new Vector2(8f,   2f),    new Vector2(-4f, -2f),
                10, GoldDim, TextAnchor.UpperCenter);

            AddText(panel, "BankLabel", "BANK",
                new Vector2(0.5f, 0.52f), new Vector2(1f, 1f),
                new Vector2(4f,   2f),    new Vector2(-8f, -2f),
                10, GoldDim, TextAnchor.UpperCenter);

            // Dollar amounts
            _roundAmount = AddText(panel, "HandAmount", "$0",
                new Vector2(0f,   0f), new Vector2(0.5f, 0.65f),
                new Vector2(8f,   4f), new Vector2(-4f,  -2f),
                36, GoldBright, TextAnchor.MiddleCenter, bold: true);

            _bankAmount = AddText(panel, "BankAmount", "$0",
                new Vector2(0.5f, 0f), new Vector2(1f, 0.65f),
                new Vector2(4f,   4f), new Vector2(-8f, -2f),
                24, GoldBright, TextAnchor.MiddleCenter, bold: true);

            // Vertical divider
            var div = MakeRect(panel, "Divider");
            div.anchorMin = new Vector2(0.5f, 0.08f);
            div.anchorMax = new Vector2(0.5f, 0.92f);
            div.sizeDelta = new Vector2(1f, 0f);
            div.anchoredPosition = Vector2.zero;
            div.gameObject.AddComponent<Image>().color = new Color(0.72f, 0.54f, 0.17f, 0.42f);
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
            rt.anchorMin  = anchorMin;
            rt.anchorMax  = anchorMax;
            rt.offsetMin  = offsetMin;
            rt.offsetMax  = offsetMax;

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
