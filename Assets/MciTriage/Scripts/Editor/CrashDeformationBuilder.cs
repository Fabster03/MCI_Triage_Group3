using System.Collections.Generic;
using System.Linq;
using Deform;
using Deform.Masking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MciTriage.EditorTools
{
    /// <summary>
    /// Menu "MCI Triage > Apply Crash Deformation": dents bus and car at their contact point
    /// using the Deform package, and removes the old placeholder cubes.
    ///
    /// Per vehicle a group "CrashDeformers/&lt;vehicle&gt;" is created at the impact point with
    ///   1. Crumple (3D Perlin noise)             – wrinkled sheet metal
    ///   2. Dent   (scale along crash direction) – pushes the front inwards (bounded)
    ///   3. Mask   (sphere mask)                  – limits both to the impact zone
    /// Every mesh of the vehicle near the impact gets a <see cref="Deformable"/> using these deformers.
    /// The deformer objects can be moved / tuned in the inspector afterwards; re-running the menu
    /// recreates them with the defaults below.
    /// </summary>
    public static class CrashDeformationBuilder
    {
        private const string VehiclesPath = "MCI_Incident/Vehicles";
        private const string DeformersName = "CrashDeformers";
        private static readonly string[] PlaceholderNames = { "Vehicle_Bus", "Vehicle_Car" };

        private struct CrashSettings
        {
            public float DentDepth;     // max. inward displacement at the impact point (m)
            public float CrumpleRadius; // radius of the crumple zone (m)
            public float CrumpleAmount; // noise magnitude (m)
        }

        // The car takes most of the damage, the bus front less.
        private static readonly CrashSettings CarCrash = new CrashSettings
            { DentDepth = 0.6f, CrumpleRadius = 1.1f, CrumpleAmount = 0.09f };
        private static readonly CrashSettings BusCrash = new CrashSettings
            { DentDepth = 0.4f, CrumpleRadius = 1.3f, CrumpleAmount = 0.06f };

        /// <summary>Distance between the closest points of car and bus after the crash (m).</summary>
        private const float VehicleGap = 0.6f;

        /// <summary>
        /// Moves the car straight backwards (along its own axis) until its closest point is
        /// <see cref="VehicleGap"/> away from the bus. Does nothing if the gap is already large enough,
        /// so re-running keeps a position that was adjusted by hand.
        /// </summary>
        private static void SeparateVehicles(Transform car, Transform bus)
        {
            var busBox = LocalBounds(bus);
            var carPoints = WorldVertices(car).ToList();
            var back = -DominantAxis(car, LocalCentreWorld(bus, busBox) - WorldBounds(car).center);

            float Gap(float offset)
            {
                float min = float.MaxValue;
                // Sub-sample for speed; the car has ~100k vertices.
                for (int i = 0; i < carPoints.Count; i += 7)
                {
                    var w = carPoints[i] + back * offset;
                    var local = bus.InverseTransformPoint(w);
                    float d = Vector3.Distance(w, bus.TransformPoint(busBox.ClosestPoint(local)));
                    if (d < min) min = d;
                }
                return min;
            }

            float current = Gap(0f);
            if (current >= VehicleGap - 0.05f)
            {
                Debug.Log($"[MCI] Vehicles already {current:0.00} m apart - car not moved.");
                return;
            }

            float move = 0f;
            while (move < 5f && Gap(move) < VehicleGap) move += 0.05f;

            Undo.RecordObject(car, "Separate vehicles");
            car.position += back * move;
            Debug.Log($"[MCI] Car moved {move:0.00} m backwards (gap {Gap(move):0.00} m).");
        }

        private static Vector3 LocalCentreWorld(Transform t, Bounds localBox) => t.TransformPoint(localBox.center);

        private const string BakedMeshFolder = "Assets/MciTriage/Art/Vehicles/BakedMeshes";
        private const string StaticSuffix = "_Static";

        [MenuItem("MCI Triage/Apply Crash Deformation")]
        public static void Apply()
        {
            var vehiclesGo = GameObject.Find(VehiclesPath);
            if (vehiclesGo == null)
            {
                Debug.LogError($"[MCI] '{VehiclesPath}' not found.");
                return;
            }
            var vehicles = vehiclesGo.transform;

            // 1. Remove placeholder cubes and earlier crash setups.
            foreach (Transform child in vehicles.Cast<Transform>().ToList())
            {
                if (PlaceholderNames.Contains(child.name) || child.name == DeformersName)
                    Undo.DestroyObjectImmediate(child.gameObject);
            }
            foreach (var d in vehicles.GetComponentsInChildren<Deformable>(true))
                Undo.DestroyObjectImmediate(d);

            // 2. Identify bus (largest) and car.
            var models = vehicles.Cast<Transform>()
                .Where(t => t.GetComponentsInChildren<Renderer>().Length > 0)
                .OrderByDescending(t => WorldBounds(t).size.sqrMagnitude)
                .ToList();
            if (models.Count < 2)
            {
                Debug.LogError("[MCI] Expected two vehicle models (bus and car) under MCI_Incident/Vehicles.");
                return;
            }
            var bus = models[0];
            var car = models[1];

            // Rigged parts (skinned meshes) cannot be deformed directly -> bake them to static meshes.
            BakeSkinnedMeshes(bus);
            BakeSkinnedMeshes(car);

            // Vehicles bounced apart after the impact: keep a small gap so the dents are visible.
            SeparateVehicles(car, bus);

            // 3. Impact points from the real geometry: the part of each vehicle that touches
            //    (or sticks into) the oriented bounding box of the other vehicle.
            var busBox = LocalBounds(bus);
            var carBox = LocalBounds(car);
            var carImpact = ImpactPoint(car, bus, busBox);
            var busImpact = ImpactPoint(bus, car, carBox);

            var deformersRoot = new GameObject(DeformersName);
            Undo.RegisterCreatedObjectUndo(deformersRoot, "Crash deformation");
            deformersRoot.transform.SetParent(vehicles, false);

            int carMeshes = SetUpVehicle(car, carImpact, CarCrash, deformersRoot.transform);
            int busMeshes = SetUpVehicle(bus, busImpact, BusCrash, deformersRoot.transform);

            EditorSceneManager.MarkSceneDirty(vehicles.gameObject.scene);
            Debug.Log($"[MCI] Crash deformation applied: car '{car.name}' at {carImpact} ({carMeshes} meshes), " +
                      $"bus '{bus.name}' at {busImpact} ({busMeshes} meshes).");
        }

        private static readonly string[] ExcludedParts = { "wheel", "tyre", "tire", "rotor", "caliper", "brake", "shadow" };

        /// <summary>
        /// Not crumpled: wheel parts (would look torn off) and skinned meshes (Deform edits the
        /// bind-pose mesh, which tears rigged models apart – they are baked to static meshes first).
        /// </summary>
        private static bool IsExcluded(Renderer r)
        {
            if (r is SkinnedMeshRenderer) return true;
            var n = r.name.ToLowerInvariant();
            return ExcludedParts.Any(n.Contains);
        }

        /// <summary>
        /// Replaces every active SkinnedMeshRenderer by a static copy of its current pose
        /// (child "&lt;name&gt;_Static" with MeshFilter + MeshRenderer, mesh saved as asset).
        /// The skinned renderer is only disabled, so the original stays in the scene.
        /// </summary>
        private static void BakeSkinnedMeshes(Transform vehicle)
        {
            foreach (var smr in vehicle.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var existing = smr.transform.Find(smr.name + StaticSuffix);
                if (!smr.enabled && existing != null) continue; // already baked

                var baked = new Mesh { name = smr.name + "_Baked" };
                smr.BakeMesh(baked);
                baked.RecalculateBounds();

                // BakeMesh may or may not include the transform scale – match the rendered size.
                float worldSize = smr.bounds.size.magnitude;
                float bakedSize = baked.bounds.size.magnitude;
                float lossy = smr.transform.lossyScale.x;
                float localScale = bakedSize > 1e-6f ? worldSize / bakedSize / lossy : 1f;

                if (!AssetDatabase.IsValidFolder(BakedMeshFolder))
                {
                    System.IO.Directory.CreateDirectory(BakedMeshFolder);
                    AssetDatabase.Refresh();
                }
                var path = $"{BakedMeshFolder}/{vehicle.name}_{smr.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(baked, path);

                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
                var go = new GameObject(smr.name + StaticSuffix);
                Undo.RegisterCreatedObjectUndo(go, "Bake skinned mesh");
                go.transform.SetParent(smr.transform, false);
                go.transform.localScale = Vector3.one * localScale;
                go.AddComponent<MeshFilter>().sharedMesh = baked;
                go.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;

                Undo.RecordObject(smr, "Disable skinned mesh");
                smr.enabled = false;
                Debug.Log($"[MCI] Baked skinned mesh '{smr.name}' to static mesh (scale {localScale:0.###}).");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Vertices of the vehicle body (without fake shadow planes, which are larger than the vehicle).</summary>
        private static IEnumerable<Vector3> WorldVertices(Transform vehicle) =>
            MeshTargets(vehicle)
                .Where(t => !t.renderer.name.ToLowerInvariant().Contains("shadow"))
                .SelectMany(t =>
            {
                var m = t.renderer.transform.localToWorldMatrix;
                return t.mesh.vertices.Select(v => m.MultiplyPoint3x4(v));
            });

        /// <summary>Bounding box in the vehicle's own (rotated) space.</summary>
        private static Bounds LocalBounds(Transform vehicle)
        {
            var points = WorldVertices(vehicle).Select(vehicle.InverseTransformPoint).ToList();
            var b = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            return b;
        }

        /// <summary>Average of the vehicle's vertices that are inside / closest to the other vehicle's box.</summary>
        private static Vector3 ImpactPoint(Transform vehicle, Transform other, Bounds otherLocalBox)
        {
            var samples = WorldVertices(vehicle)
                .Select(w => (w, d: Vector3.Distance(w, other.TransformPoint(otherLocalBox.ClosestPoint(other.InverseTransformPoint(w))))))
                .ToList();
            float min = samples.Min(s => s.d);
            var hits = samples.Where(s => s.d <= min + 0.05f).Select(s => s.w).ToList();
            var sum = Vector3.zero;
            foreach (var h in hits) sum += h;
            return sum / hits.Count;
        }

        /// <summary>Batch-mode entry: opens MainScene, applies and saves.</summary>
        public static void ApplyAndSaveMainScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity", OpenSceneMode.Single);
            Apply();
            EditorSceneManager.SaveScene(scene);
        }

        private static int SetUpVehicle(Transform vehicle, Vector3 impact, CrashSettings s, Transform parent)
        {
            var targets = MeshTargets(vehicle).Where(t => !IsExcluded(t.renderer)).ToList();
            var centre = WorldBounds(vehicle).center;
            // Centre the damage at least at mid height of the vehicle (bumper bottom would hide the dent).
            impact.y = Mathf.Max(impact.y, centre.y);

            // Crash direction = the vehicle's own axis (front/back/side) that points most towards the impact.
            var outward = DominantAxis(vehicle, impact - centre);

            var group = new GameObject(vehicle.name);
            group.transform.SetParent(parent, false);
            group.transform.SetPositionAndRotation(impact, Quaternion.LookRotation(outward));

            var crumple = new GameObject("1 Crumple").AddComponent<PerlinNoiseDeformer>();
            crumple.transform.SetParent(group.transform, false);
            // 3D noise depends only on the vertex position, so duplicated vertices at hard edges / UV seams
            // move identically (Normal mode tears low-poly meshes such as the bus apart).
            crumple.Mode = NoiseMode._3D;
            crumple.Factor = 1f;
            crumple.MagnitudeScalar = s.CrumpleAmount;
            crumple.FrequencyScalar = 3f;
            crumple.OffsetSpeedScalar = 0f; // static, no animated noise

            // Dent = compression along the crash direction around a pivot inside the vehicle.
            // A vertex at the impact point moves DentDepth towards the pivot; the effect is bounded
            // (no exploding vertices) and limited to the crash zone by the mask below.
            float pivotDepth = s.CrumpleRadius * 1.2f;
            var dent = new GameObject("2 Dent").AddComponent<ScaleDeformer>();
            dent.transform.SetParent(group.transform, false);
            dent.transform.localPosition = new Vector3(0, 0, -pivotDepth);
            dent.transform.localScale = new Vector3(1f, 1f, 1f - s.DentDepth / pivotDepth);

            // Mask: full effect within 40 % of the radius, fading out to the full radius.
            // Invert = true: Deform's sphere mask *protects* the inside by default; inverted it keeps
            // the deformation inside the sphere and restores the original mesh outside.
            var mask = new GameObject("3 Mask").AddComponent<SphereMask>();
            mask.transform.SetParent(group.transform, false);
            mask.Factor = 1f;
            mask.Invert = true;
            mask.OuterRadius = s.CrumpleRadius * 2f; // SphereMask expects diameters
            mask.InnerRadius = s.CrumpleRadius * 0.8f;

            int count = 0;
            float reach = s.CrumpleRadius + 0.5f;
            foreach (var (renderer, _) in targets)
            {
                if (renderer.bounds.SqrDistance(impact) > reach * reach) continue;
                var deformable = Undo.AddComponent<Deformable>(renderer.gameObject);
                deformable.UpdateMode = UpdateMode.Auto;
                // Keep the model's original normals: recalculating them smooths hard edges and
                // panel gaps across the whole mesh (visible glitches far away from the dent).
                deformable.NormalsRecalculation = NormalsRecalculation.None;
                deformable.AddDeformer(crumple);
                deformable.AddDeformer(dent);
                deformable.AddDeformer(mask);
                count++;
            }
            if (count == 0)
            {
                Object.DestroyImmediate(group);
                Debug.LogWarning($"[MCI] '{vehicle.name}': no deformable meshes near the impact - left undeformed.");
            }
            return count;
        }

        private static IEnumerable<(Renderer renderer, Mesh mesh)> MeshTargets(Transform root)
        {
            // Only renderers that are actually visible (disabled = replaced by a baked static copy).
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh != null && r != null && r.enabled)
                    yield return (r, mf.sharedMesh);
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr.sharedMesh != null && smr.enabled)
                    yield return (smr, smr.sharedMesh);
        }

        private static Vector3 DominantAxis(Transform vehicle, Vector3 direction)
        {
            direction.y = 0;
            var axes = new[] { vehicle.forward, -vehicle.forward, vehicle.right, -vehicle.right };
            var best = axes.OrderByDescending(a => Vector3.Dot(new Vector3(a.x, 0, a.z).normalized, direction)).First();
            best.y = 0;
            return best.normalized;
        }

        private static Bounds WorldBounds(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
