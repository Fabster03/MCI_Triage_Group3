using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MciTriage.EditorTools
{
    /// <summary>
    /// Fixes MakeHuman models rigged by Mixamo: the eye mesh ("low-poly") is partly weighted to the neck,
    /// so the eyes lag behind / stick out of the face when the head turns. On import of patient models
    /// the eye mesh is bound 100 % to the head bone.
    /// </summary>
    public class PatientModelPostprocessor : AssetPostprocessor
    {
        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith("Assets/MciTriage/Art/Patients/")) return;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.name.StartsWith("low-poly") || smr.sharedMesh == null) continue;
                int head = System.Array.FindIndex(smr.bones, b => b != null && b.name.EndsWith("Head"));
                if (head < 0) continue;

                var mesh = smr.sharedMesh;
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = head, weight0 = 1f }, mesh.vertexCount).ToArray();
            }
        }
    }
}
