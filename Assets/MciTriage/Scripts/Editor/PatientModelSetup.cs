using System.Collections.Generic;
using System.IO;
using System.Linq;
using MciTriage.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MciTriage.EditorTools
{
    /// <summary>
    /// Real character models for patients (instead of the placeholder capsules).
    /// For each entry in <see cref="Models"/> an FBX (mesh + rig + Mixamo animation) is turned into a prefab
    /// with URP materials and an Animator. Menu "MCI Triage > Apply Patient Models" swaps the capsule of
    /// those patients in the open scene; <see cref="PlaceholderSceneBuilder"/> uses the prefabs as well.
    /// </summary>
    public static class PatientModelSetup
    {
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";

        public sealed class ModelInfo
        {
            public string Folder;   // contains the FBX and a "textures" subfolder
            public string Fbx;      // file name of the FBX inside Folder (mesh + rig + first animation, e.g. getting up)
            public string IdleFbx;  // optional: Mixamo animation "without skin", loops after the first animation
            public string WalkFbx;  // optional: Mixamo walk exported with "In Place", played on "Ask to walk"
            public float HeadHeight; // target height of the head bone when standing (scales the model)
            // FBX material name -> (diffuse texture, normal map or null, alpha clip, double sided)
            public Dictionary<string, (string diffuse, string normal, bool alphaClip, bool doubleSided)> Materials;

            public string FbxPath => $"{Folder}/{Fbx}";
            public string PrefabPath => $"{Folder}/{Path.GetFileNameWithoutExtension(Fbx)}.prefab";
            public string ControllerPath => $"{Folder}/{Path.GetFileNameWithoutExtension(Fbx)}.controller";
        }

        public static readonly Dictionary<string, ModelInfo> Models = new Dictionary<string, ModelInfo>
        {
            {
                "P01", new ModelInfo
                {
                    Folder = "Assets/MciTriage/Art/Patients/P01",
                    Fbx = "testpatient001_standingup.fbx",
                    IdleFbx = "testpatient001_idle.fbx",
                    WalkFbx = "testpatient001_walking.fbx",
                    HeadHeight = 1.62f, // ~1.78 m tall man
                    Materials = new Dictionary<string, (string, string, bool, bool)>
                    {
                        { "testpatient001", ("old_lightskinned_male_diffuse", null, false, false) },
                        { "male_casualsuit04", ("male_casualsuit04_diffuse", "male_casualsuit04_normal", false, true) },
                        { "shoes05", ("shoes05_diffuse", null, false, false) },
                        { "short02", ("short02_diffuse", null, true, true) },
                        { "low-poly", ("brown_eye", null, false, false) },
                    },
                }
            },
        };

        // ------------------------------------------------------------------ menu / batch entry points

        [MenuItem("MCI Triage/Apply Patient Models")]
        public static void ApplyToOpenScene()
        {
            foreach (var pair in Models)
            {
                var root = GameObject.Find("MCI_Incident/Patients/Patient_" + pair.Key);
                if (root == null)
                {
                    Debug.LogWarning($"[MCI] Patient_{pair.Key} not found in the open scene - build the placeholder incident first.");
                    continue;
                }
                var avatar = root.GetComponent<PatientAvatar>();
                var prefab = GetOrCreatePrefab(pair.Value);
                SetupAnimator(pair.Value);
                var oldBody = root.transform.Find("Body");
                if (oldBody != null) Undo.DestroyObjectImmediate(oldBody.gameObject);
                CreateCharacterBody(root.transform, avatar, avatar.Controller, avatar.Patient, prefab,
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/MciTriage/Art/Placeholders/Mat_TriageTag.mat"));
                EditorSceneManager.MarkSceneDirty(root.scene);
                Debug.Log($"[MCI] Patient_{pair.Key}: placeholder replaced by {pair.Value.Fbx}.");
            }
        }

        /// <summary>Batch mode: opens MainScene, swaps the patient bodies, saves.</summary>
        public static void ApplyAndSaveMainScene()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ used by the scene builder

        public static GameObject TryGetPrefab(string patientId)
        {
            if (!Models.TryGetValue(patientId, out var info)) return null;
            var prefab = GetOrCreatePrefab(info);
            SetupAnimator(info);
            return prefab;
        }

        // ------------------------------------------------------------------ animator

        /// <summary>
        /// (Re)builds the states of the patient's animator controller (asset GUID stays the same):
        /// StandUp (default, once) -> Idle (loop) &lt;-> Walk (loop, bool parameter "Walking").
        /// </summary>
        private static void SetupAnimator(ModelInfo info)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(info.ControllerPath);
            var first = ImportClip(info.FbxPath, "StandUp", loop: false);
            var idle = info.IdleFbx != null ? ImportClip($"{info.Folder}/{info.IdleFbx}", "Idle", true) : null;
            var walk = info.WalkFbx != null ? ImportClip($"{info.Folder}/{info.WalkFbx}", "Walk", true) : null;

            foreach (var p in controller.parameters.ToArray()) controller.RemoveParameter(p);
            if (walk != null) controller.AddParameter(PatientAvatar.WalkingParameter, AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;
            foreach (var st in sm.states.ToArray()) sm.RemoveState(st.state);

            var standUp = sm.AddState("StandUp", new Vector3(300, 0, 0));
            standUp.motion = first;
            sm.defaultState = standUp;
            if (idle == null) return;

            var idleState = sm.AddState("Idle", new Vector3(300, 120, 0));
            idleState.motion = idle;
            var toIdle = standUp.AddTransition(idleState);
            toIdle.hasExitTime = true;
            toIdle.exitTime = 0.97f;
            toIdle.duration = 0.4f;

            if (walk == null) return;
            var walkState = sm.AddState("Walk", new Vector3(600, 120, 0));
            walkState.motion = walk;
            var toWalk = idleState.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.25f;
            toWalk.AddCondition(AnimatorConditionMode.If, 0, PatientAvatar.WalkingParameter);
            var back = walkState.AddTransition(idleState);
            back.hasExitTime = false;
            back.duration = 0.3f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0, PatientAvatar.WalkingParameter);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static AnimationClip ImportClip(string path, string name, bool loop)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            if (path.Contains("_idle") || path.Contains("_walk"))
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.motionNodeName = "";
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = name;
                c.loopTime = loop;
                c.loopPose = loop;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return LoadClip(path);
        }

        private static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));

        /// <summary>Creates the "Body" child from a character prefab plus a triage tag on the chest.</summary>
        public static void CreateCharacterBody(Transform patientRoot, PatientAvatar avatar, ScenarioController controller,
            PatientData data, GameObject prefab, Material tagMat)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, patientRoot);
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(body, "Create patient character");
            body.AddComponent<XRSimpleInteractable>();

            var animator = body.GetComponent<Animator>();
            var clip = animator.runtimeAnimatorController.animationClips[0];

            // Place the tag in the final (standing) pose, attached to the chest bone so it follows the animation.
            clip.SampleAnimation(body, clip.length);
            var chest = FindBone(body.transform, "Spine2");
            if (chest == null) chest = FindBone(body.transform, "Spine1");
            if (chest == null) chest = body.transform;
            var tag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tag.name = "TriageTag";
            Object.DestroyImmediate(tag.GetComponent<Collider>());
            tag.transform.position = chest.position + body.transform.forward * 0.14f;
            tag.transform.rotation = body.transform.rotation;
            tag.transform.SetParent(chest, true);
            float s = chest.lossyScale.x;
            tag.transform.localScale = new Vector3(0.26f, 0.17f, 0.02f) / s;
            var tagRenderer = tag.GetComponent<Renderer>();
            tagRenderer.sharedMaterial = tagMat;
            tagRenderer.enabled = false;

            // Show the start pose in the editor (matches what is shown before START).
            clip.SampleAnimation(body, 0f);

            var bodyRenderer = body.GetComponentInChildren<SkinnedMeshRenderer>();
            avatar.Configure(controller, data, body.transform, bodyRenderer, tagRenderer);
            avatar.ConfigureCharacter(animator);
            EditorUtility.SetDirty(avatar);
        }

        // ------------------------------------------------------------------ asset creation

        private static GameObject GetOrCreatePrefab(ModelInfo info)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(info.PrefabPath);
            if (existing != null) return existing;

            ConfigureImporter(info);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(info.FbxPath);
            var clip = AssetDatabase.LoadAllAssetsAtPath(info.FbxPath).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview"));

            // Animator controller: a single state playing the clip once.
            var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(info.ControllerPath, clip);
            controller.layers[0].stateMachine.defaultState.name = "Animation";

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Scale so that the head bone ends up at the target height when standing.
            clip.SampleAnimation(instance, clip.length);
            var head = FindBone(instance.transform, "Head");
            float scale = 1f;
            if (head != null && head.position.y > 0.1f)
                scale = Mathf.Clamp(info.HeadHeight / (head.position.y - instance.transform.position.y), 0.5f, 2f);
            instance.transform.localScale = Vector3.one * scale;

            // Collider for grabbing (local units, compensating the scale).
            var col = instance.AddComponent<CapsuleCollider>();
            col.height = 1.8f / scale;
            col.radius = 0.3f / scale;
            col.center = new Vector3(0, 0.9f / scale, 0);

            clip.SampleAnimation(instance, 0f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, info.PrefabPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"[MCI] Created {info.PrefabPath} (scale {scale:0.00}, clip '{clip.name}' {clip.length:0.0}s).");
            return prefab;
        }

        private static void ConfigureImporter(ModelInfo info)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(info.FbxPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = false;
                c.name = "StandUp";
            }
            importer.clipAnimations = clips;

            foreach (var pair in info.Materials)
            {
                var mat = GetOrCreateMaterial(info, pair.Key, pair.Value.diffuse, pair.Value.normal, pair.Value.alphaClip, pair.Value.doubleSided);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), mat);
            }
            importer.SaveAndReimport();
        }

        private static Material GetOrCreateMaterial(ModelInfo info, string name, string diffuse, string normal, bool alphaClip, bool doubleSided)
        {
            var folder = info.Folder + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(info.Folder, "Materials");
            var path = $"{folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Smoothness", 0.25f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{info.Folder}/textures/{diffuse}.png");
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            if (normal != null)
            {
                var normalPath = $"{info.Folder}/textures/{normal}.png";
                var texImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
                if (texImporter.textureType != TextureImporterType.NormalMap)
                {
                    texImporter.textureType = TextureImporterType.NormalMap;
                    texImporter.SaveAndReimport();
                }
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                mat.EnableKeyword("_NORMALMAP");
            }
            if (alphaClip)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            if (doubleSided) mat.SetFloat("_Cull", 0f); // hair cards, open clothing edges
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Transform FindBone(Transform root, string suffix) =>
            root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith(":" + suffix) || t.name == suffix);
    }
}
