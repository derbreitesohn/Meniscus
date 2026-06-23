// =====================================================================================================
// DEV-ONLY runtime tuning overlay.
//
// A throwaway, reflection-driven IMGUI panel for trialling values at runtime — probability, camera,
// item heights, timings, liquid visuals, game-feel, and so on. It self-installs in any gameplay scene
// (no scene or Inspector setup) and is compiled only in the Editor / development builds.
//
// TO REMOVE LATER:
//   1. Delete the whole Assets/Scripts/Dev folder.
//   2. Revert the balance fields in GameConstants.cs from `static` back to `const` (see note there).
//   3. Delete the small `#if UNITY_EDITOR ...` guard block in PlayerController.HandleClick's caller.
// =====================================================================================================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Meniscus.Core;
using Meniscus.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Meniscus.Dev
{
    /// <summary>
    /// Reflection-driven runtime settings overlay for development tuning. Toggle with F1 (or the
    /// on-screen button). Lists every tunable field on a curated set of managers/controllers plus the
    /// static <see cref="GameConstants"/> balance values, and writes edits straight back so they take
    /// effect live. Development only — see file header for removal steps.
    /// </summary>
    [DisallowMultipleComponent]
    public class DevSettingsPanel : MonoBehaviour
    {
        // ---- self-install (mirrors GameFeelBootstrap) -------------------------------------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Only in gameplay scenes (those that own the glass), and never duplicate.
            if (UnityEngine.Object.FindAnyObjectByType<GlassManager>() == null)
                return;
            if (UnityEngine.Object.FindAnyObjectByType<DevSettingsPanel>() != null)
                return;

            var go = new GameObject("Dev Settings (DEV ONLY)");
            DontDestroyOnLoad(go);
            go.AddComponent<DevSettingsPanel>();
        }

        /// <summary>True while the panel is open and the pointer is over it. Gameplay input checks this
        /// so a click on the overlay does not also poke the table underneath. DEV ONLY.</summary>
        public static bool IsPointerOverPanel { get; private set; }

        // ---- config -----------------------------------------------------------------------------
        const Key ToggleKey = Key.F1;
        const float LabelWidth = 158f;

        // Section title + the type to reflect over. Types that are not UnityEngine.Objects (i.e.
        // GameConstants) are treated as static; the rest are resolved live from the scene.
        static readonly (string title, Type type)[] Sections =
        {
            ("Probability & Balance (GameConstants)", typeof(GameConstants)),
            ("Glass / Spill (live)",   typeof(GlassManager)),
            ("Game Flow",              typeof(GameManager)),
            ("Economy",                typeof(EconomyManager)),
            ("Camera",                 typeof(CameraController)),
            ("Coin Drop Presentation", typeof(CoinDropPresentationController)),
            ("Glass Visuals (liquid)", typeof(GlassVisualController)),
            ("Glass Fit",              typeof(GlassPresentationController)),
            ("Player Input",           typeof(PlayerController)),
            ("Danger Vignette",        typeof(DangerVignette)),
            ("Game Feel",              typeof(GameFeelDirector)),
        };

        // ---- state ------------------------------------------------------------------------------
        bool _open;
        Rect _window = new(12, 12, 500, 760);
        Vector2 _scroll;
        string _search = "";
        bool _built;
        readonly List<Group> _groups = new();
        readonly Dictionary<string, string> _buffers = new(); // per-control text edit buffers

        GUIStyle _label, _header, _tiny, _swatch;
        Texture2D _white;

        class Group
        {
            public string Title;
            public Type Type;
            public bool IsStatic;
            public bool Expanded = true;
            public UnityEngine.Object Target;
            public readonly List<Tunable> Tunables = new();
        }

        class Tunable
        {
            public FieldInfo Field;
            public string Label;
            public string Key;
            public bool ReadOnly;
            public object Default;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[ToggleKey].wasPressedThisFrame)
                _open = !_open;
        }

        void OnDestroy()
        {
            if (_white != null)
                Destroy(_white);
        }

        // ---- GUI --------------------------------------------------------------------------------
        void OnGUI()
        {
            EnsureStyles();

            if (!_open)
            {
                IsPointerOverPanel = false;
                if (GUI.Button(new Rect(12, 12, 152, 26), "⚙ Dev Settings (F1)"))
                    _open = true;
                return;
            }

            if (!_built)
                Build();

            _window.height = Mathf.Min(Screen.height - 24, 900);
            _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "DEV SETTINGS — runtime tuning");
            IsPointerOverPanel = _window.Contains(Event.current.mousePosition);
        }

        void DrawWindow(int id)
        {
            // Top toolbar: search + actions.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", _label, GUILayout.Width(48));
            GUI.SetNextControlName("dev_search");
            _search = GUILayout.TextField(_search ?? "", _tiny, GUILayout.MinWidth(120));
            if (GUILayout.Button("×", GUILayout.Width(24)))
            {
                _search = "";
                GUI.FocusControl(null);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Rescan", GUILayout.Width(62)))
                Build();
            if (GUILayout.Button("Reset all", GUILayout.Width(72)))
                ResetAll();
            if (GUILayout.Button("Close", GUILayout.Width(52)))
                _open = false;
            GUILayout.EndHorizontal();

            // Handy global knob: time scale (great for watching the coin-drop beats slowly).
            GUILayout.BeginHorizontal();
            GUILayout.Label("Time scale", _label, GUILayout.Width(LabelWidth));
            float ts = GUILayout.HorizontalSlider(Time.timeScale, 0f, 3f, GUILayout.MinWidth(90));
            if (!Mathf.Approximately(ts, Time.timeScale))
                Time.timeScale = ts;
            float tts = DrawFloat("dev_timescale", Time.timeScale);
            if (!Mathf.Approximately(tts, Time.timeScale))
                Time.timeScale = tts;
            if (GUILayout.Button("1×", GUILayout.Width(30)))
                Time.timeScale = 1f;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            string filter = (_search ?? "").Trim();
            bool filtering = filter.Length > 0;

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var g in _groups)
                DrawGroup(g, filter, filtering);
            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, _window.width, 20));
        }

        void DrawGroup(Group g, string filter, bool filtering)
        {
            List<Tunable> shown = g.Tunables;
            if (filtering)
            {
                bool groupMatch = g.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                shown = new List<Tunable>();
                foreach (var t in g.Tunables)
                    if (groupMatch || t.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        shown.Add(t);
                if (shown.Count == 0)
                    return;
            }

            bool live = g.IsStatic || g.Target != null;
            bool expanded = g.Expanded || filtering;

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            string head = (expanded ? "▾  " : "▸  ") + g.Title +
                          (live ? "" : "  (not in scene)") + "   [" + shown.Count + "]";
            if (GUILayout.Button(head, _header))
            {
                if (!filtering)
                    g.Expanded = !g.Expanded;
            }
            if (GUILayout.Button("↺", _header, GUILayout.Width(28)))
                ResetGroup(g);
            GUILayout.EndHorizontal();

            if (!expanded || !live)
                return;

            object target = g.IsStatic ? null : g.Target;
            foreach (var t in shown)
                DrawTunable(t, target);
        }

        void DrawTunable(Tunable tn, object target)
        {
            object val;
            try { val = tn.Field.GetValue(target); }
            catch { return; }

            Type ft = tn.Field.FieldType;

            GUILayout.BeginHorizontal();
            GUILayout.Label(tn.Label + (tn.ReadOnly ? "  (read-only)" : ""), _label, GUILayout.Width(LabelWidth));

            bool prevEnabled = GUI.enabled;
            GUI.enabled = !tn.ReadOnly;

            if (ft == typeof(bool))
            {
                bool b = (bool)val;
                bool nb = GUILayout.Toggle(b, b ? " on" : " off");
                if (nb != b) Set(tn, target, nb);
            }
            else if (ft == typeof(float))
            {
                float cur = (float)val;
                var (lo, hi) = RangeFor(tn, cur);
                float sv = GUILayout.HorizontalSlider(cur, lo, hi, GUILayout.MinWidth(90));
                if (!Mathf.Approximately(sv, cur)) { Set(tn, target, sv); cur = sv; }
                float tv = DrawFloat(tn.Key, cur);
                if (!Mathf.Approximately(tv, cur)) Set(tn, target, tv);
            }
            else if (ft == typeof(int))
            {
                int cur = (int)val;
                var (lo, hi) = RangeFor(tn, cur);
                int sv = Mathf.RoundToInt(GUILayout.HorizontalSlider(cur, lo, hi, GUILayout.MinWidth(90)));
                if (sv != cur) { Set(tn, target, sv); cur = sv; }
                int tv = DrawInt(tn.Key, cur);
                if (tv != cur) Set(tn, target, tv);
            }
            else if (ft.IsEnum)
            {
                DrawEnum(tn, target, val, ft);
            }
            else if (ft == typeof(string))
            {
                string s = (string)val ?? "";
                string ns = DrawString(tn.Key, s);
                if (ns != s) Set(tn, target, ns);
            }
            else if (ft == typeof(Vector3))
            {
                var v = (Vector3)val;
                var nv = new Vector3(DrawFloat(tn.Key + ".x", v.x), DrawFloat(tn.Key + ".y", v.y), DrawFloat(tn.Key + ".z", v.z));
                if (nv != v) Set(tn, target, nv);
            }
            else if (ft == typeof(Vector2))
            {
                var v = (Vector2)val;
                var nv = new Vector2(DrawFloat(tn.Key + ".x", v.x), DrawFloat(tn.Key + ".y", v.y));
                if (nv != v) Set(tn, target, nv);
            }
            else if (ft == typeof(Color))
            {
                var c = (Color)val;
                var old = GUI.color;
                GUI.color = c;
                GUILayout.Label(GUIContent.none, _swatch, GUILayout.Width(16), GUILayout.Height(14));
                GUI.color = old;
                var nc = new Color(
                    DrawFloat(tn.Key + ".r", c.r), DrawFloat(tn.Key + ".g", c.g),
                    DrawFloat(tn.Key + ".b", c.b), DrawFloat(tn.Key + ".a", c.a));
                if (nc != c) Set(tn, target, nc);
            }

            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();
        }

        void DrawEnum(Tunable tn, object target, object val, Type ft)
        {
            var values = Enum.GetValues(ft);
            var names = Enum.GetNames(ft);
            int idx = Array.IndexOf(values, val);
            if (idx < 0) idx = 0;

            if (GUILayout.Button("◀", GUILayout.Width(26)))
                Set(tn, target, values.GetValue((idx - 1 + names.Length) % names.Length));
            GUILayout.Label(names[idx], _label, GUILayout.MinWidth(80));
            if (GUILayout.Button("▶", GUILayout.Width(26)))
                Set(tn, target, values.GetValue((idx + 1) % names.Length));
        }

        // ---- text fields with a focus-aware edit buffer -----------------------------------------
        // While the field has keyboard focus we show the raw buffer (so half-typed "1." survives a
        // repaint); otherwise we show the live value formatted (so external changes stay visible).
        float DrawFloat(string key, float value)
        {
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            string display = focused && _buffers.TryGetValue(key, out var b)
                ? b : value.ToString("0.####", CultureInfo.InvariantCulture);
            string edited = GUILayout.TextField(display, _tiny, GUILayout.Width(54));
            if (focused)
            {
                _buffers[key] = edited;
                if (float.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    return p;
            }
            else
            {
                _buffers.Remove(key);
            }
            return value;
        }

        int DrawInt(string key, int value)
        {
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            string display = focused && _buffers.TryGetValue(key, out var b)
                ? b : value.ToString(CultureInfo.InvariantCulture);
            string edited = GUILayout.TextField(display, _tiny, GUILayout.Width(54));
            if (focused)
            {
                _buffers[key] = edited;
                if (int.TryParse(edited, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                    return p;
            }
            else
            {
                _buffers.Remove(key);
            }
            return value;
        }

        string DrawString(string key, string value)
        {
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            string display = focused && _buffers.TryGetValue(key, out var b) ? b : value;
            string edited = GUILayout.TextField(display ?? "", _tiny, GUILayout.MinWidth(90));
            if (focused)
            {
                _buffers[key] = edited;
                return edited;
            }
            _buffers.Remove(key);
            return value;
        }

        // ---- model build / reset ----------------------------------------------------------------
        void Build()
        {
            _groups.Clear();
            _buffers.Clear();
            GUI.FocusControl(null);

            foreach (var (title, type) in Sections)
            {
                var g = new Group
                {
                    Title = title,
                    Type = type,
                    IsStatic = !typeof(UnityEngine.Object).IsAssignableFrom(type),
                };
                if (!g.IsStatic)
                    g.Target = UnityEngine.Object.FindAnyObjectByType(type);

                Collect(g);
                if (g.Tunables.Count > 0)
                    _groups.Add(g);
            }
            _built = true;
        }

        void Collect(Group g)
        {
            var flags = BindingFlags.DeclaredOnly |
                        (g.IsStatic
                            ? BindingFlags.Static | BindingFlags.Public
                            : BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var f in g.Type.GetFields(flags))
            {
                if (f.Name.IndexOf('<') >= 0) continue;          // compiler-generated backing fields
                if (!IsSupported(f.FieldType)) continue;

                if (!g.IsStatic)
                {
                    bool serialized = f.IsPublic || f.IsDefined(typeof(SerializeField), true);
                    if (!serialized) continue;
                }

                object def;
                try { def = f.GetValue(g.IsStatic ? null : g.Target); }
                catch { continue; }

                g.Tunables.Add(new Tunable
                {
                    Field = f,
                    Label = Prettify(f.Name),
                    Key = g.Title + "::" + f.Name,
                    ReadOnly = f.IsLiteral || f.IsInitOnly,
                    Default = def,
                });
            }
        }

        void ResetAll()
        {
            foreach (var g in _groups)
                ResetGroup(g);
        }

        void ResetGroup(Group g)
        {
            object target = g.IsStatic ? null : g.Target;
            foreach (var t in g.Tunables)
                Set(t, target, t.Default);

            string prefix = g.Title + "::";
            var keys = new List<string>(_buffers.Keys);
            foreach (var k in keys)
                if (k.StartsWith(prefix, StringComparison.Ordinal))
                    _buffers.Remove(k);
            GUI.FocusControl(null);
        }

        void Set(Tunable tn, object target, object value)
        {
            if (tn.ReadOnly)
                return;
            try { tn.Field.SetValue(target, value); }
            catch (Exception e) { Debug.LogWarning($"[DevSettings] Could not set {tn.Label}: {e.Message}"); }
        }

        // ---- helpers ----------------------------------------------------------------------------
        static bool IsSupported(Type t) =>
            t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(string) ||
            t.IsEnum || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Color);

        // Picks a sensible slider span. The neighbouring text field always allows exact / out-of-range
        // entry, so this only needs to be "good enough to drag".
        (float lo, float hi) RangeFor(Tunable tn, float cur)
        {
            string n = tn.Field.Name.ToLowerInvariant();
            if (Has(n, "probability", "chance", "threshold", "penalty", "relief"))
                return Widen(0f, 100f, cur);
            if (n.Contains("angle"))
                return Widen(-180f, 180f, cur);
            if (Has(n, "alpha", "fraction", "factor", "onset", "inset"))
                return Widen(0f, 1f, cur);

            float def = tn.Default is float df ? df : tn.Default is int di ? di : cur;
            float mag = Mathf.Max(Mathf.Abs(def), Mathf.Abs(cur), 0.01f);
            bool neg = def < 0f || cur < 0f || n.Contains("offset");
            return Widen(neg ? -mag * 2f : 0f, mag * 4f, cur);
        }

        static (float, float) Widen(float lo, float hi, float cur)
        {
            if (cur < lo) lo = cur - Mathf.Abs(cur) * 0.1f - 0.01f;
            if (cur > hi) hi = cur * 1.1f + 0.01f;
            if (hi <= lo) hi = lo + 1f;
            return (lo, hi);
        }

        static bool Has(string n, params string[] keys)
        {
            foreach (var k in keys)
                if (n.Contains(k)) return true;
            return false;
        }

        static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool boundary = i > 0 && char.IsUpper(c) &&
                                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (boundary) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }

        void EnsureStyles()
        {
            if (_label != null) return;

            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            _tiny = new GUIStyle(GUI.skin.textField) { fontSize = 11 };
            _header = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _swatch = new GUIStyle(GUI.skin.box);
            _swatch.normal.background = _white;
        }
    }
}
#endif
