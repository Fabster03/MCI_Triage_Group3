using System.Collections;
using System.Linq;
using MciTriage.Core;
using MciTriage.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MciTriage.Tests
{
    /// <summary>
    /// Loads MainScene and plays one short run through the UI buttons
    /// (no headset needed). Verifies that scene, UI and logic are wired together.
    /// </summary>
    public class MainSceneSmokeTests
    {
        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            LogAssert.ignoreFailingMessages = true; // no XR headset in CI/batch mode
            yield return SceneManager.LoadSceneAsync("MainScene");
            yield return null;
            yield return null;
        }

        /// <summary>Finds an active button by its visible label.</summary>
        private static Button FindButton(string label)
        {
            var button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text == label);
            Assert.IsNotNull(button, $"Button '{label}' not found.");
            return button;
        }

        [UnityTest]
        public IEnumerator Board_Patients_Panel_AndReport_Work()
        {
            var controller = Object.FindFirstObjectByType<ScenarioController>();
            Assert.IsNotNull(controller, "ScenarioController missing - run 'MCI Triage > Build Placeholder Incident'.");
            var avatars = Object.FindObjectsByType<PatientAvatar>(FindObjectsSortMode.None);
            Assert.AreEqual(5, avatars.Length);
            Assert.IsFalse(controller.IsRunning, "Scenario must wait for START.");

            // START on the board.
            FindButton("START").onClick.Invoke();
            yield return null;
            Assert.IsTrue(controller.IsRunning);

            // Examine P03 (bus driver, RR 36) and tag him red.
            var p03 = avatars.Single(a => a.PatientId == "P03");
            p03.OpenPanel();
            yield return null;
            FindButton("Ask to walk").onClick.Invoke();
            FindButton("Check breathing").onClick.Invoke();
            FindButton("Count respiratory rate").onClick.Invoke();
            yield return null;
            var texts = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Select(t => t.text).ToList();
            CollectionAssert.Contains(texts, "36 /min");
            FindButton("RED").onClick.Invoke();
            yield return null;
            Assert.AreEqual(TriageCategory.Immediate, controller.Session.GetAssignedCategory("P03"));

            // Non-walking patients lie down at runtime.
            yield return new WaitForSeconds(0.8f);
            var body = p03.transform.Find("Body");
            Assert.Greater(Mathf.Abs(body.localEulerAngles.z), 45f);

            // Tag P01 (walking wounded) green via its own panel; P03's panel closes.
            var p01 = avatars.Single(a => a.PatientId == "P01");
            p01.OpenPanel();
            yield return null;
            FindButton("GREEN").onClick.Invoke();
            Assert.AreEqual(TriageCategory.Minor, controller.Session.GetAssignedCategory("P01"));

            // FINISH → report on the board.
            FindButton("FINISH").onClick.Invoke();
            yield return null;
            Assert.IsFalse(controller.IsRunning);
            Assert.IsNotNull(controller.LastReport);
            Assert.AreEqual(2, controller.LastReport.CorrectCount);
            Assert.IsTrue(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.StartsWith("Scenario tunnel_bus_crash")));
        }
    }
}
