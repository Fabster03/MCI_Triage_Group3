using System.Linq;
using MciTriage.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MciTriage.Runtime
{
    /// <summary>
    /// Command board near the start position: briefing, START/FINISH button,
    /// timer and – after the run – the results summary (placeholder for the dashboard).
    /// </summary>
    public class ScenarioBoard : MonoBehaviour
    {
        [SerializeField] private ScenarioController controller;

        private const float Width = 1400;
        private const float Height = 1000;

        private Text _status;
        private Text _results;
        private Text _buttonLabel;

        public void Configure(ScenarioController scenarioController) => controller = scenarioController;

        private void Start()
        {
            var canvas = UiKit.CreateWorldCanvas("BoardCanvas", transform, new Vector2(Width, Height), 0.0012f);
            var t = canvas.transform;
            var data = controller.ScenarioAsset;

            UiKit.Panel(t, 0, 0, Width, Height, UiKit.PanelColor);
            UiKit.Label(t, 40, 25, Width - 80, 70, data != null ? data.displayName : "Scenario", 52, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Label(t, 40, 105, Width - 80, 130, data != null ? data.briefing : "", 30, TextAnchor.UpperLeft, FontStyle.Normal, UiKit.MutedText);

            var button = UiKit.Button(t, 40, 250, 360, 90, "START", 40, OnButton, new Color(0.15f, 0.45f, 0.75f));
            _buttonLabel = button.GetComponentInChildren<Text>();
            _status = UiKit.Label(t, 430, 250, Width - 470, 90, "", 36, TextAnchor.MiddleLeft, FontStyle.Bold);

            UiKit.Panel(t, 40, 360, Width - 80, 2, UiKit.MutedText);
            _results = UiKit.Label(t, 40, 375, Width - 80, Height - 400, "", 26, TextAnchor.UpperLeft);

            controller.ReportReady += ShowReport;
        }

        private void OnDestroy()
        {
            if (controller != null) controller.ReportReady -= ShowReport;
        }

        private void OnButton()
        {
            if (controller.IsRunning)
            {
                controller.FinishScenario();
            }
            else
            {
                _results.text = "";
                controller.StartScenario();
            }
        }

        private void Update()
        {
            var session = controller.Session;
            if (session == null)
            {
                _status.text = "Read the briefing, then press START.";
                _buttonLabel.text = "START";
                return;
            }

            int triaged = session.Scenario.Patients.Count(p => session.GetAssignedCategory(p.Id) != TriageCategory.None);
            if (session.IsRunning)
            {
                _status.text = $"Time left {UiKit.FormatTime(session.RemainingSeconds)}   |   tagged {triaged}/{session.Scenario.Patients.Count}";
                _buttonLabel.text = "FINISH";
            }
            else
            {
                _status.text = $"Ended ({session.EndReason}) after {UiKit.FormatTime(session.ElapsedSeconds)}";
                _buttonLabel.text = "RESTART";
            }
        }

        private void ShowReport(SessionReport report) => _results.text = report.ToSummaryText();
    }
}
