using System.Collections.Generic;
using System.IO;
using System.Linq;
using MciTriage.Runtime;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MciTriage.EditorTools
{
    /// <summary>
    /// Menu "MCI Triage > Build Placeholder Incident": creates the incident in the open scene –
    /// scenario controller, command board, two vehicles and one capsule per patient – placed
    /// inside the tunnel in front of the XR rig. Re-running replaces the previous build
    /// (everything lives under the root object "MCI_Incident"), so objects can be moved by hand
    /// afterwards, but manual changes are lost on rebuild.
    /// </summary>
    public static class PlaceholderSceneBuilder
    {
        private const string RootName = "MCI_Incident";
        private const string ScenarioPath = "Assets/MciTriage/Data/TunnelBusCrash.asset";
        private const string MaterialFolder = "Assets/MciTriage/Art/Placeholders";
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";

        // Patient positions in the incident frame: (along tunnel from crash centre, lateral), metres.
        private static readonly Dictionary<string, Vector2> PatientSpots = new Dictionary<string, Vector2>
        {
            { "P01", new Vector2(-3.0f, 2.2f) },  // walking wounded, closest to the trainee
            { "P02", new Vector2(4.0f, 1.0f) },   // sitting next to the bus
            { "P03", new Vector2(2.2f, 2.6f) },   // bus driver on the road
            { "P04", new Vector2(8.0f, 1.6f) },   // further along the bus
            { "P05", new Vector2(-0.3f, 2.1f) },  // beside the car
        };

        [MenuItem("MCI Triage/Build Placeholder Incident")]
        public static void Build()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<ScenarioData>(ScenarioPath);
            if (scenario == null)
            {
                Debug.LogError($"[MCI] Scenario asset not found at {ScenarioPath}.");
                return;
            }

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var frame = ComputeFrame();
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build placeholder incident");

            // Scenario controller (started from the board, not automatically).
            var controllerGo = new GameObject("ScenarioController");
            controllerGo.transform.SetParent(root.transform, false);
            var controller = controllerGo.AddComponent<ScenarioController>();
            var so = new SerializedObject(controller);
            so.FindProperty("scenario").objectReferenceValue = scenario;
            so.FindProperty("startOnPlay").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Vehicles: coach and passenger car after a frontal collision.
            var vehicles = new GameObject("Vehicles");
            vehicles.transform.SetParent(root.transform, false);
            CreateVehicle(vehicles.transform, "Vehicle_Bus", frame, new Vector2(7.5f, -1.4f), 6f,
                new Vector3(2.55f, 3.2f, 11.5f), Mat("Mat_Bus", new Color(0.95f, 0.75f, 0.2f)));
            CreateVehicle(vehicles.transform, "Vehicle_Car", frame, new Vector2(0.4f, -0.6f), 28f,
                new Vector3(1.8f, 1.45f, 4.3f), Mat("Mat_Car", new Color(0.2f, 0.35f, 0.75f)));

            // Patients.
            var patientsRoot = new GameObject("Patients");
            patientsRoot.transform.SetParent(root.transform, false);
            var bodyMat = Mat("Mat_Patient", new Color(0.87f, 0.68f, 0.55f));
            var tagMat = Mat("Mat_TriageTag", Color.white);
            int fallbackIndex = 0;
            foreach (var patient in scenario.patients.Where(p => p != null))
            {
                var spot = PatientSpots.TryGetValue(patient.patientId, out var s)
                    ? s : new Vector2(-2f + 2.5f * fallbackIndex++, 3f);
                CreatePatient(patientsRoot.transform, patient, controller, frame, spot, bodyMat, tagMat);
            }

            // Command board in front of the trainee, slightly to the left.
            var board = new GameObject("ScenarioBoard");
            board.transform.SetParent(root.transform, false);
            board.transform.position = frame.PlayerStart + frame.Along * 2.6f - frame.Lateral * 1.3f + Vector3.up * 1.55f;
            var toBoard = board.transform.position - (frame.PlayerStart + Vector3.up * 1.6f);
            toBoard.y = 0;
            board.transform.rotation = Quaternion.LookRotation(toBoard.normalized);
            board.AddComponent<ScenarioBoard>().Configure(controller);

            EnsureEventSystem();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[MCI] Placeholder incident built. Tunnel axis {frame.Along}, crash centre {frame.Centre}, floor y={frame.FloorY:0.00}.");
        }

        /// <summary>Entry point for batch mode: opens MainScene, builds and saves.</summary>
        public static void BuildAndSaveMainScene()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Build();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ layout frame

        private struct Frame
        {
            public Vector3 Centre;     // crash centre on the road surface
            public Vector3 Along;      // unit vector along the tunnel, pointing away from the trainee
            public Vector3 Lateral;    // unit vector to the right of Along
            public Vector3 PlayerStart;
            public float FloorY;
            public float LateralScale; // < 1 if the tunnel is narrow

            public Vector3 At(Vector2 p) =>
                new Vector3(Centre.x, FloorY, Centre.z) + Along * p.x + Lateral * (p.y * LateralScale);
        }

        private static Frame ComputeFrame()
        {
            var origin = Object.FindFirstObjectByType<XROrigin>();
            var playerPos = origin != null ? origin.transform.position : Vector3.zero;
            var playerFwd = origin != null ? origin.transform.forward : Vector3.forward;
            playerFwd.y = 0;
            playerFwd = playerFwd.sqrMagnitude > 1e-4f ? playerFwd.normalized : Vector3.forward;

            var tunnel = GameObject.Find("DasTunnelAsset");
            Bounds bounds;
            if (tunnel != null && tunnel.GetComponentsInChildren<Renderer>().Length > 0)
            {
                var renderers = tunnel.GetComponentsInChildren<Renderer>();
                bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            }
            else
            {
                Debug.LogWarning("[MCI] 'DasTunnelAsset' not found - using a default 10 x 60 m area.");
                bounds = new Bounds(playerPos + playerFwd * 20f, new Vector3(10f, 6f, 60f));
            }

            // Tunnel axis = longer horizontal extent, oriented away from the player.
            var axis = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
            float dot = Vector3.Dot(playerFwd, axis);
            var along = dot < 0 ? -axis : axis;
            var lateral = Vector3.Cross(Vector3.up, along).normalized;

            float length = Mathf.Abs(Vector3.Dot(bounds.size, axis));
            float width = Mathf.Abs(Vector3.Dot(bounds.size, lateral));

            // Crash centre ~11 m ahead of the player, but kept inside the tunnel.
            var centre = playerPos + along * 11f;
            float alongFromBoundsCentre = Vector3.Dot(centre - bounds.center, along);
            float maxAlong = Mathf.Max(0f, length / 2f - 12f);
            if (Mathf.Abs(alongFromBoundsCentre) > maxAlong)
                centre -= along * (alongFromBoundsCentre - Mathf.Sign(alongFromBoundsCentre) * maxAlong);
            // Laterally centred in the tunnel.
            centre -= lateral * Vector3.Dot(centre - bounds.center, lateral);

            float floorY = FindFloor(centre, bounds);

            return new Frame
            {
                Centre = centre,
                Along = along,
                Lateral = lateral,
                PlayerStart = new Vector3(playerPos.x, floorY, playerPos.z),
                FloorY = floorY,
                LateralScale = Mathf.Clamp01((width / 2f - 1.2f) / 3f),
            };
        }

        private static float FindFloor(Vector3 centre, Bounds bounds)
        {
            Physics.SyncTransforms();
            var start = new Vector3(centre.x, bounds.center.y, centre.z);
            var hits = Physics.RaycastAll(start, Vector3.down, bounds.size.y + 5f);
            if (hits.Length > 0) return hits.OrderBy(h => h.distance).First().point.y;
            return bounds.min.y;
        }

        // ------------------------------------------------------------------ objects

        private static void CreateVehicle(Transform parent, string name, Frame frame, Vector2 spot, float yawDegrees,
            Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = frame.At(spot) + Vector3.up * (size.y / 2f);
            go.transform.rotation = Quaternion.LookRotation(frame.Along) * Quaternion.Euler(0, yawDegrees, 0);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void CreatePatient(Transform parent, PatientData data, ScenarioController controller, Frame frame,
            Vector2 spot, Material bodyMat, Material tagMat)
        {
            var root = new GameObject("Patient_" + data.patientId);
            root.transform.SetParent(parent, false);
            root.transform.position = frame.At(spot);
            var toPlayer = frame.PlayerStart - root.transform.position;
            toPlayer.y = 0;
            root.transform.rotation = Quaternion.LookRotation(toPlayer.sqrMagnitude > 1e-4f ? toPlayer.normalized : -frame.Along);

            // Real character model available for this patient? Then use it instead of the capsule.
            var characterPrefab = PatientModelSetup.TryGetPrefab(data.patientId);
            if (characterPrefab != null)
            {
                var avatar = root.AddComponent<PatientAvatar>();
                PatientModelSetup.CreateCharacterBody(root.transform, avatar, controller, data, characterPrefab, tagMat);
                return;
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f);
            // Initial pose already in the editor: standing if the patient can walk, otherwise lying.
            bool standing = data.initialFindings.canWalk;
            body.transform.localPosition = new Vector3(0, standing ? 0.85f : 0.25f, 0);
            body.transform.localRotation = Quaternion.Euler(0, 0, standing ? 0f : 90f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;
            body.AddComponent<XRSimpleInteractable>();

            // Triage tag: small card on the chest, facing the trainee (compensates the body scale).
            var tag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tag.name = "TriageTag";
            Object.DestroyImmediate(tag.GetComponent<Collider>());
            tag.transform.SetParent(body.transform, false);
            tag.transform.localPosition = new Vector3(0, 0.25f, 0.52f);
            tag.transform.localScale = new Vector3(0.6f, 0.24f, 0.06f);
            var tagRenderer = tag.GetComponent<Renderer>();
            tagRenderer.sharedMaterial = tagMat;
            tagRenderer.enabled = false;

            root.AddComponent<PatientAvatar>().Configure(controller, data, body.transform,
                body.GetComponent<Renderer>(), tagRenderer);
        }

        private static void EnsureEventSystem()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>();
            if (existing != null)
            {
                if (existing.GetComponent<XRUIInputModule>() == null)
                    Debug.LogWarning("[MCI] Existing EventSystem has no XRUIInputModule - XR rays may not click the UI.");
                return;
            }
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        private static Material Mat(string name, Color color)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                Directory.CreateDirectory(MaterialFolder);
                AssetDatabase.Refresh();
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
