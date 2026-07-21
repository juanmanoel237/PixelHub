#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Laps.Core;
using Laps.Routing;
using Laps.Authoring;

namespace Laps.Editor
{
    /// <summary>
    /// Script d'éditeur Unity : génère la scène principale PixelHub en un clic.
    /// Menu : PixelHub → Create Main Scene
    /// </summary>
    public static class SceneBuilder
    {
        [MenuItem("PixelHub/🎬 Create Main Scene", priority = 0)]
        public static void CreateMainScene()
        {
            // ── 1. Créer / ouvrir une nouvelle scène vide ──────────
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 2. Caméra de base ──────────────────────────────────
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            // ── 3. Lumière directionnelle ──────────────────────────
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ── 4. GameObject racine PixelHub ──────────────────────
            var rootGo = new GameObject("=== PixelHub ===");

            // ── 5. ConfigManager ────────────────────────────────────
            var configGo = CreateChild(rootGo, "ConfigManager");
            configGo.AddComponent<ConfigManager>();

            // ── 6. RoutingEngine ────────────────────────────────────
            var routingGo = CreateChild(rootGo, "RoutingEngine");
            var routingEngine = routingGo.AddComponent<RoutingEngine>();

            // ── 7. ShowTimeline ────────────────────────────────────
            var timelineGo = CreateChild(rootGo, "ShowTimeline");
            var showTimeline = timelineGo.AddComponent<ShowTimeline>();

            // AudioSource pour la synchronisation musicale
            var audioSource = timelineGo.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            // Assigner l'AudioSource via SerializedObject pour respecter [SerializeField]
            var timelineSO = new SerializedObject(showTimeline);
            timelineSO.FindProperty("_audioSource").objectReferenceValue = audioSource;
            timelineSO.ApplyModifiedProperties();

            // ── 8. DebugPanel ──────────────────────────────────────
            var debugGo = CreateChild(rootGo, "DebugPanel");
            var debugPanel = debugGo.AddComponent<DebugPanel>();

            // ── 9. Bootstrapper ────────────────────────────────────
            var bootGo = CreateChild(rootGo, "Bootstrapper");
            var bootstrapper = bootGo.AddComponent<PixelHubBootstrapper>();

            // Câbler les références dans le Bootstrapper
            var bootSO = new SerializedObject(bootstrapper);
            bootSO.FindProperty("_configManager").objectReferenceValue  = configGo.GetComponent<ConfigManager>();
            bootSO.FindProperty("_routingEngine").objectReferenceValue  = routingEngine;
            bootSO.FindProperty("_showTimeline").objectReferenceValue   = showTimeline;
            bootSO.FindProperty("_debugPanel").objectReferenceValue     = debugPanel;
            bootSO.ApplyModifiedProperties();

            // ── 10. LED Preview Canvas (UI de prévisualisation) ────
            CreatePreviewCanvas(rootGo);

            // ── 11. Sauvegarder la scène ───────────────────────────
            string scenesPath = "Assets/Scenes";
            if (!System.IO.Directory.Exists(scenesPath))
                System.IO.Directory.CreateDirectory(scenesPath);

            string scenePath = $"{scenesPath}/Main.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            // Ajouter à Build Settings
            AddSceneToBuildSettings(scenePath);

            Debug.Log($"[SceneBuilder] ✅ Scène créée et sauvegardée : {scenePath}");
            EditorUtility.DisplayDialog(
                "PixelHub — Scène créée !",
                $"La scène a été créée avec succès.\n\nFichier : {scenePath}\n\n" +
                "Appuie sur ▶ Play pour démarrer.\n" +
                "Mode par défaut : Debug (fake state)\n\n" +
                "Pour tester les LEDs physiques, assure-toi d'être sur le même réseau que les contrôleurs.",
                "OK");
        }

        // ── Helpers ────────────────────────────────────────────────

        private static GameObject CreateChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static void CreatePreviewCanvas(GameObject root)
        {
            // Canvas en Screen Space — Overlay pour le debug
            var canvasGo = new GameObject("DebugCanvas");
            canvasGo.transform.SetParent(root.transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Panneau de stats (Text en bas à gauche)
            var statsGo = new GameObject("StatsText");
            statsGo.transform.SetParent(canvasGo.transform, false);
            var statsText = statsGo.AddComponent<UnityEngine.UI.Text>();
            statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statsText.fontSize = 13;
            statsText.color = new Color(0.2f, 1f, 0.4f); // Vert terminal
            statsText.text = "=== PixelHub Debug ===";
            var statsRect = statsGo.GetComponent<RectTransform>();
            statsRect.anchorMin = new Vector2(0, 0);
            statsRect.anchorMax = new Vector2(0.4f, 0.5f);
            statsRect.offsetMin = new Vector2(10, 10);
            statsRect.offsetMax = new Vector2(-10, -10);

            // MiniScreen (RawImage en haut à droite pour la prévisualisation)
            var miniGo = new GameObject("MiniScreen");
            miniGo.transform.SetParent(canvasGo.transform, false);
            var miniImage = miniGo.AddComponent<UnityEngine.UI.RawImage>();
            miniImage.color = Color.white;
            var miniRect = miniGo.GetComponent<RectTransform>();
            miniRect.anchorMin = new Vector2(0.7f, 0.5f);
            miniRect.anchorMax = new Vector2(1f, 1f);
            miniRect.offsetMin = new Vector2(10, 10);
            miniRect.offsetMax = new Vector2(-10, -10);

            // Brancher les refs UI sur DebugPanel
            // (via les propriétés SerializedObject)
            // Note: DebugPanel sera dans rootGo, on cherche le composant
            var debugPanel = root.GetComponentInChildren<DebugPanel>();
            if (debugPanel != null)
            {
                var so = new SerializedObject(debugPanel);
                so.FindProperty("_statsText").objectReferenceValue   = statsText;
                so.FindProperty("_miniScreen").objectReferenceValue  = miniImage;
                so.ApplyModifiedProperties();
            }
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == scenePath) return; // Déjà présente

            var newScenes = new EditorBuildSettingsScene[scenes.Length + 1];
            System.Array.Copy(scenes, newScenes, scenes.Length);
            newScenes[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = newScenes;
        }
    }
}
#endif
