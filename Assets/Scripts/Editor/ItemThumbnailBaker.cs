using System.IO;
using Meniscus.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Meniscus.Editor
{
    /// <summary>
    /// Bakes a small "screenshot" of each desk item model into a sprite shown in the buy menu. Run
    /// Tools ▸ Meniscus ▸ Bake Item Thumbnails (after Wire Item Models) and it renders every wired model —
    /// with its material override applied, so the painted look matches the desk — to a PNG under
    /// <c>Assets/Art/Generated/ItemThumbnails</c>, imports it as a Sprite, and drops it into that item's
    /// <c>icon</c> slot in the <see cref="GameManager"/>'s <c>ItemModelLibrary</c>. The render is done with
    /// <see cref="PreviewRenderUtility"/> so it works under URP and needs no play mode. Idempotent: re-running
    /// overwrites the PNGs in place. Items with no model are skipped (their menu row simply shows no picture).
    /// </summary>
    public static class ItemThumbnailBaker
    {
        const string ThumbnailFolderRel = "Assets/Art/Generated/ItemThumbnails";
        const int ThumbnailSize = 256;

        // Matches BookShopView's parchment page so the (opaque) thumbnail sits seamlessly on the page.
        static readonly Color PageBackground = new(0.91f, 0.84f, 0.66f, 1f);

        [MenuItem("Tools/Meniscus/Bake Item Thumbnails")]
        public static void BakeItemThumbnails()
        {
            var gameManager = Object.FindAnyObjectByType<GameManager>();

            if (gameManager == null)
            {
                Debug.LogError(
                    "[Meniscus] Bake Item Thumbnails: no GameManager found. Open Assets/Scenes/Saloon.unity " +
                    "and try again.");
                return;
            }

            var serialized = new SerializedObject(gameManager);
            var entries = serialized.FindProperty("itemModels")?.FindPropertyRelative("entries");

            if (entries == null || entries.arraySize == 0)
            {
                Debug.LogError(
                    "[Meniscus] Bake Item Thumbnails: the item model library is empty. Run " +
                    "Tools ▸ Meniscus ▸ Wire Item Models first, then bake.");
                return;
            }

            EnsureFolder();

            var baked = 0;

            try
            {
                for (var i = 0; i < entries.arraySize; i++)
                {
                    var element = entries.GetArrayElementAtIndex(i);
                    var itemId = element.FindPropertyRelative("itemId").stringValue;
                    var model = element.FindPropertyRelative("model").objectReferenceValue as GameObject;

                    if (string.IsNullOrEmpty(itemId) || model == null)
                        continue;

                    var material = element.FindPropertyRelative("material").objectReferenceValue as Material;
                    var assetPath = $"{ThumbnailFolderRel}/{itemId}.png";

                    EditorUtility.DisplayProgressBar(
                        "Baking item thumbnails", $"{itemId}", (i + 1f) / entries.arraySize);

                    var sprite = RenderToSprite(model, material, assetPath);

                    if (sprite != null)
                    {
                        element.FindPropertyRelative("icon").objectReferenceValue = sprite;
                        baked++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(gameManager);
            var scene = gameManager.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                $"[Meniscus] Baked {baked} item thumbnail(s) into '{ThumbnailFolderRel}' and assigned them on " +
                $"'{gameManager.name}'. They now appear in the buy menu.");
        }

        /// <summary>Convenience: wire every item's model, then bake the thumbnails, in one go.</summary>
        [MenuItem("Tools/Meniscus/Set Up Item Visuals (Wire + Bake)")]
        public static void SetUpItemVisuals()
        {
            ItemModelWiring.WireItemModels();
            BakeItemThumbnails();
        }

        // Renders the model (with the optional material override) to a PNG, imports it as a Sprite, and
        // returns the loaded sprite. Returns null if the render or import fails.
        static Sprite RenderToSprite(GameObject model, Material materialOverride, string assetPath)
        {
            var preview = new PreviewRenderUtility();

            try
            {
                var instance = Object.Instantiate(model);
                instance.hideFlags = HideFlags.HideAndDontSave;

                if (materialOverride != null)
                {
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                        renderer.sharedMaterial = materialOverride;
                }

                preview.AddSingleGO(instance);

                var bounds = WorldBounds(instance);
                FrameCamera(preview, bounds);
                LightRig(preview);

                preview.BeginStaticPreview(new Rect(0, 0, ThumbnailSize, ThumbnailSize));
                preview.Render(true, true);
                var texture = preview.EndStaticPreview();

                Object.DestroyImmediate(instance);

                if (texture == null)
                {
                    Debug.LogWarning($"[Meniscus] Bake Item Thumbnails: render produced no texture for '{assetPath}'.");
                    return null;
                }

                File.WriteAllBytes(ToAbsolute(assetPath), texture.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath);
                ApplySpriteImport(assetPath);

                return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }
            finally
            {
                preview.Cleanup();
            }
        }

        static void FrameCamera(PreviewRenderUtility preview, Bounds bounds)
        {
            var camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PageBackground;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;

            // Three-quarter view so coins/glass/spyglass all read with depth.
            var rotation = Quaternion.Euler(18f, 130f, 0f);
            var radius = Mathf.Max(0.0001f, bounds.extents.magnitude);
            var distance = radius / Mathf.Sin(Mathf.Deg2Rad * camera.fieldOfView * 0.5f) * 1.25f;

            camera.transform.rotation = rotation;
            camera.transform.position = bounds.center - rotation * Vector3.forward * distance;
        }

        static void LightRig(PreviewRenderUtility preview)
        {
            preview.ambientColor = new Color(0.45f, 0.45f, 0.45f, 1f);

            if (preview.lights.Length > 0)
            {
                preview.lights[0].intensity = 1.25f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            }

            if (preview.lights.Length > 1)
            {
                preview.lights[1].intensity = 0.6f;
                preview.lights[1].transform.rotation = Quaternion.Euler(-20f, -120f, 0f);
            }
        }

        static Bounds WorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        static void ApplySpriteImport(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(ThumbnailFolderRel))
                return;

            Directory.CreateDirectory(ToAbsolute(ThumbnailFolderRel));
            AssetDatabase.Refresh();
        }

        // Maps an "Assets/..." path to an absolute path (Application.dataPath ends with "/Assets").
        static string ToAbsolute(string assetPath)
        {
            var withoutAssets = assetPath.Substring("Assets".Length).TrimStart('/', '\\');
            return Path.Combine(Application.dataPath, withoutAssets);
        }
    }
}
