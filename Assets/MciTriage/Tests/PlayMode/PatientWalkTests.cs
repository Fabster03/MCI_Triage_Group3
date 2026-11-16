using System.Collections;
using System.Linq;
using MciTriage.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MciTriage.Tests
{
    /// <summary>
    /// P01 (character model): gets up after START, then idles; "Ask to walk" closes the panel,
    /// the patient walks and returns to the same spot, then the panel reopens.
    /// </summary>
    public class PatientWalkTests
    {
        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            LogAssert.ignoreFailingMessages = true; // no XR headset in batch mode
            yield return SceneManager.LoadSceneAsync("MainScene");
            yield return null;
        }

        private static Button FindButton(string label) =>
            Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .First(b => b.GetComponentInChildren<Text>()?.text == label);

        [UnityTest]
        public IEnumerator P01_StandsUp_Idles_And_WalksOnRequest()
        {
            Time.timeScale = 4f; // the stand-up animation takes ~13 s
            try
            {
                var p01 = Object.FindObjectsByType<PatientAvatar>(FindObjectsSortMode.None).Single(a => a.PatientId == "P01");
                Assert.IsTrue(p01.HasCharacterModel, "P01 should use the character model.");
                var animator = p01.GetComponentInChildren<Animator>();

                FindButton("START").onClick.Invoke();
                yield return new WaitForSeconds(15f);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Expected Idle after standing up.");

                var startPos = p01.transform.position;
                var startRot = p01.transform.rotation;
                p01.OpenPanel();
                yield return null;
                FindButton("Ask to walk").onClick.Invoke();
                yield return null;
                Assert.IsTrue(p01.IsWalking);
                Assert.IsFalse(p01.GetComponent<PatientPanel>().IsOpen, "Panel closes while walking.");

                yield return new WaitForSeconds(1.5f);
                Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"));

                // The walk clip must play in place (the script moves the patient, not the animation).
                var hips = animator.GetComponentsInChildren<Transform>().First(t => t.name.EndsWith("Hips"));
                float maxOffset = 0f;
                for (int i = 0; i < 30; i++)
                {
                    var local = hips.localPosition;
                    maxOffset = Mathf.Max(maxOffset, new Vector2(local.x, local.z).magnitude);
                    yield return null;
                }
                Assert.Less(maxOffset, 0.3f, $"Walk animation drifts ({maxOffset:0.00} m, hips path {hips.parent.name}/{hips.name}, clip {animator.GetCurrentAnimatorClipInfo(0)[0].clip.name}).");
                Assert.Greater(Vector3.Distance(p01.transform.position, startPos), 0.3f, "Patient should move.");

                float timeout = 15f;
                while (p01.IsWalking && timeout > 0) { timeout -= Time.deltaTime; yield return null; }
                Assert.IsFalse(p01.IsWalking, "Walk did not finish.");
                Assert.Less(Vector3.Distance(p01.transform.position, startPos), 0.01f);
                Assert.Less(Quaternion.Angle(p01.transform.rotation, startRot), 1f);
                Assert.IsTrue(p01.GetComponent<PatientPanel>().IsOpen, "Panel reopens after walking.");
            }
            finally
            {
                Time.timeScale = 1f;
            }
        }
    }
}
