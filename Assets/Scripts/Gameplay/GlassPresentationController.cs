using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Owns which glass variant sits on the table. Spawns the active <see cref="GlassTypeDefinition"/>'s
    /// model under an anchor and re-fits the water surface to it. One "normal" type is enough today; add
    /// more <see cref="GlassTypeDefinition"/> assets to <see cref="glassTypes"/> and call
    /// <see cref="SetGlassType(string)"/> to switch — no code change needed to add variants.
    /// </summary>
    [DisallowMultipleComponent]
    public class GlassPresentationController : MonoBehaviour
    {
        [Tooltip("Where the glass model is parented. Defaults to this object's transform.")]
        [SerializeField] Transform glassAnchor;
        [Tooltip("Water surface driver re-fitted to the active glass. Auto-found if left empty.")]
        [SerializeField] GlassVisualController waterController;
        [Tooltip("All glass variants the table can use. The first valid entry is the default 'normal' glass.")]
        [SerializeField] List<GlassTypeDefinition> glassTypes = new();
        [Tooltip("Id of the glass shown on start. Leave empty to use the first entry.")]
        [SerializeField] string defaultGlassTypeId;
        [Tooltip("Hide the anchor's own MeshRenderer (the placeholder cup) while a real glass model is shown.")]
        [SerializeField] bool hidePlaceholderRenderer = true;

        [Tooltip("A real glass already placed in the scene. When the matching type is selected it is just " +
                 "shown and driven by code (no runtime instantiation); other types still spawn their model.")]
        [SerializeField] Transform authoredGlass;
        [Tooltip("Which glass-type id the in-scene authored glass represents.")]
        [SerializeField] string authoredGlassTypeId = "standard";

        [Header("Liquid Auto-Fit")]
        [Tooltip("Measure the spawned glass model's bounds and fit the liquid body to its interior, instead " +
                 "of the glass-type's authored radius/floor (which can't know the real model size).")]
        [SerializeField] bool autoFitLiquidToGlass = true;
        [Tooltip("Liquid rim radius as a fraction of the measured glass radius, to sit just inside the wall.")]
        [SerializeField, Range(0.5f, 1f)] float glassInteriorRadiusFactor = 0.9f;
        [Tooltip("Liquid floor raised above the measured glass base by this fraction of the glass height.")]
        [SerializeField, Range(0f, 0.3f)] float glassFloorInset = 0.03f;

        [Header("Debug")]
        [Tooltip("Press this key in play mode to cycle through the glass types. Also available via the " +
                 "component's right-click context menu.")]
        [SerializeField] bool enableCycleKey = true;
        [SerializeField] Key cycleKey = Key.G;

        GameObject activeModel;
        GlassTypeDefinition activeType;
        int activeTypeIndex = -1;

        public GlassTypeDefinition ActiveType => activeType;
        public IReadOnlyList<GlassTypeDefinition> GlassTypes => glassTypes;

        void Awake()
        {
            if (glassAnchor == null)
                glassAnchor = transform;

            if (waterController == null)
                waterController = GetComponentInChildren<GlassVisualController>() ?? FindAnyObjectByType<GlassVisualController>();
        }

        void Start()
        {
            ApplyDefaultGlassType();
        }

        void Update()
        {
            if (!enableCycleKey || Keyboard.current == null)
                return;

            if (Keyboard.current[cycleKey].wasPressedThisFrame)
                CycleGlassType();
        }

        public void ApplyDefaultGlassType()
        {
            var index = IndexOfType(defaultGlassTypeId);

            if (index < 0)
                index = FirstValidIndex();

            if (index >= 0)
                SetGlassType(index);
        }

        /// <summary>Advances to the next valid glass type, wrapping around. Hooked to the cycle key and context menu.</summary>
        [ContextMenu("Cycle Glass Type")]
        public void CycleGlassType()
        {
            if (glassTypes.Count == 0)
                return;

            for (var step = 1; step <= glassTypes.Count; step++)
            {
                var next = (activeTypeIndex + step) % glassTypes.Count;

                if (glassTypes[next] != null)
                {
                    SetGlassType(next);
                    return;
                }
            }
        }

        public bool SetGlassType(string id)
        {
            var index = IndexOfType(id);

            if (index < 0)
                return false;

            SetGlassType(index);
            return true;
        }

        public void SetGlassType(int index)
        {
            if (index < 0 || index >= glassTypes.Count || glassTypes[index] == null)
                return;

            var type = glassTypes[index];
            activeTypeIndex = index;
            activeType = type;

            if (activeModel != null)
            {
                Destroy(activeModel);
                activeModel = null;
            }

            Transform glassForFit = null;

            // Prefer a glass already authored in the scene for its type: just show it and drive it via code,
            // instead of spawning a model at runtime. Other types still instantiate their prefab on demand.
            var useAuthored = authoredGlass != null
                              && !string.IsNullOrEmpty(authoredGlassTypeId)
                              && type.Id == authoredGlassTypeId;

            if (useAuthored)
            {
                authoredGlass.gameObject.SetActive(true);
                glassForFit = authoredGlass;
            }
            else if (type.ModelPrefab != null)
            {
                if (authoredGlass != null)
                    authoredGlass.gameObject.SetActive(false);

                // Instantiating a model slot wired to a non-GameObject sub-asset (e.g. an FBX's Mesh instead
                // of its GameObject root) throws; that must never bubble out of Start and leave the table
                // glass-less. On failure, drop back to the placeholder cup instead of crashing.
                try
                {
                    activeModel = Instantiate(type.ModelPrefab, glassAnchor);
                    activeModel.name = $"Glass Model ({type.DisplayName})";
                    activeModel.transform.localPosition = type.ModelLocalPosition;
                    activeModel.transform.localRotation = type.ModelLocalRotation;
                    activeModel.transform.localScale = type.ModelLocalScale;

                    var keptGlass = IsolateChild(activeModel.transform, type.ModelChildName);

                    // If the chosen glass sits at a lineup offset inside a multi-glass model, slide the whole
                    // model so that glass lands on the anchor (pure translation: safe even when already centred).
                    if (keptGlass != null)
                        activeModel.transform.position -= keptGlass.position - activeModel.transform.position;

                    // The liquid fits to this glass's measured interior (see ConfigureSurface call below).
                    glassForFit = keptGlass != null ? keptGlass : activeModel.transform;
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[GlassPresentationController] Could not instantiate the '{type.DisplayName}' glass " +
                        "model. The model slot is likely wired to a non-GameObject sub-asset (e.g. an FBX Mesh) " +
                        "instead of the FBX's GameObject root; re-assign it by dragging the .fbx onto the slot. " +
                        $"Keeping the placeholder cup. ({exception.GetType().Name}: {exception.Message})",
                        this);

                    if (activeModel != null)
                    {
                        Destroy(activeModel);
                        activeModel = null;
                    }

                    if (authoredGlass != null)
                        authoredGlass.gameObject.SetActive(true);
                }
            }

            // Hide the anchor's own placeholder renderer whenever a real glass (authored or model) is showing,
            // so a missing/empty model never leaves the table glass-less.
            var placeholder = glassAnchor != null ? glassAnchor.GetComponent<MeshRenderer>() : null;
            var showingRealGlass = useAuthored || activeModel != null;

            if (placeholder != null)
                placeholder.enabled = !(hidePlaceholderRenderer && showingRealGlass);

            if (waterController != null)
            {
                var fit = MeasureLiquidFit(glassForFit, waterController.transform, type);
                waterController.ConfigureSurface(
                    fit.SurfaceLocalY,
                    fit.RimRadius,
                    fit.FloorLocalY,
                    type.FillBottomRadiusScale);
            }
        }

        readonly struct LiquidFit
        {
            public readonly float SurfaceLocalY;
            public readonly float RimRadius;
            public readonly float FloorLocalY;

            public LiquidFit(float surfaceLocalY, float rimRadius, float floorLocalY)
            {
                SurfaceLocalY = surfaceLocalY;
                RimRadius = rimRadius;
                FloorLocalY = floorLocalY;
            }
        }

        // Fits the liquid body to the spawned glass model's measured interior so it fills the glass instead
        // of floating as a small vessel: keeps the glass-type's authored fill *level* (the surface), but
        // takes the rim radius and floor from the glass's real bounds (converted into the liquid's local
        // space). Falls back to the authored radius/floor when auto-fit is off or no glass renderer exists.
        LiquidFit MeasureLiquidFit(Transform glass, Transform liquidSpace, GlassTypeDefinition type)
        {
            var fallback = new LiquidFit(type.WaterSurfaceLocalY, type.WaterSurfaceRadius, type.FillBottomLocalY);

            if (!autoFitLiquidToGlass || glass == null || liquidSpace == null)
                return fallback;

            var renderers = glass.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return fallback;

            var world = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                world.Encapsulate(renderers[i].bounds);

            // Convert the world AABB's 8 corners into the liquid's local space (where surface heights and the
            // radius are expressed), then take the local extents.
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var center = world.center;
            var extents = world.extents;

            for (var sx = -1; sx <= 1; sx += 2)
                for (var sy = -1; sy <= 1; sy += 2)
                    for (var sz = -1; sz <= 1; sz += 2)
                    {
                        var corner = liquidSpace.InverseTransformPoint(
                            center + new Vector3(extents.x * sx, extents.y * sy, extents.z * sz));
                        min = Vector3.Min(min, corner);
                        max = Vector3.Max(max, corner);
                    }

            var height = max.y - min.y;
            var radius = 0.5f * Mathf.Max(max.x - min.x, max.z - min.z);

            if (height <= 1e-4f || radius <= 1e-4f)
                return fallback;

            var rimRadius = radius * Mathf.Clamp(glassInteriorRadiusFactor, 0.5f, 1f);
            var floorLocalY = min.y + height * Mathf.Clamp(glassFloorInset, 0f, 0.3f);

            // Keep the authored fill level when it sits inside the measured glass; otherwise fall to ~80% full.
            var surfaceLocalY = type.WaterSurfaceLocalY;

            if (surfaceLocalY <= floorLocalY || surfaceLocalY > max.y)
                surfaceLocalY = min.y + height * 0.82f;

            Debug.Log(
                $"[GlassFit] '{type.DisplayName}' interior (liquid-local): y[{min.y:F3}..{max.y:F3}] " +
                $"radius {radius:F3} -> surface {surfaceLocalY:F3}, rim {rimRadius:F3}, floor {floorLocalY:F3}",
                this);

            return new LiquidFit(surfaceLocalY, rimRadius, floorLocalY);
        }

        // When several glasses share one model (like Glas_Types), keep only the named child visible and
        // switch the siblings off. No scale/rotation surgery, so each glass keeps its authored shape and
        // pose. Returns the kept glass (or null when nothing was isolated).
        static Transform IsolateChild(Transform modelRoot, string childName)
        {
            if (string.IsNullOrEmpty(childName))
                return null;

            var keep = FindDeepChild(modelRoot, childName);

            if (keep == null)
            {
                Debug.LogWarning($"[GlassPresentationController] Glass child '{childName}' not found in " +
                                 $"'{modelRoot.name}'; showing the whole model.");
                return null;
            }

            // Switch off every sibling at the kept glass's own level; ancestors stay on so it still renders.
            var parent = keep.parent;

            if (parent != null)
            {
                for (var i = 0; i < parent.childCount; i++)
                {
                    var sibling = parent.GetChild(i);
                    sibling.gameObject.SetActive(sibling == keep);
                }
            }

            return keep;
        }

        static Transform FindDeepChild(Transform root, string childName)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);

                if (child.name == childName)
                    return child;

                var found = FindDeepChild(child, childName);

                if (found != null)
                    return found;
            }

            return null;
        }

        int IndexOfType(string id)
        {
            if (string.IsNullOrEmpty(id))
                return -1;

            for (var i = 0; i < glassTypes.Count; i++)
                if (glassTypes[i] != null && glassTypes[i].Id == id)
                    return i;

            return -1;
        }

        int FirstValidIndex()
        {
            for (var i = 0; i < glassTypes.Count; i++)
                if (glassTypes[i] != null)
                    return i;

            return -1;
        }
    }
}
