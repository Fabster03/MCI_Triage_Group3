using System;
using System.IO;
using MciTriage.Core;
using UnityEngine;
using UnityEngine.Events;

namespace MciTriage.Runtime
{
    /// <summary>Scenario time from Unity (respects Time.timeScale / pause).</summary>
    public sealed class UnityClock : IClock
    {
        public double Now => Time.timeAsDouble;
    }

    /// <summary>
    /// Scene entry point. Owns the <see cref="ScenarioSession"/>, drives the timer,
    /// and on end evaluates the run and writes log + report to disk.
    /// UI / VR interaction scripts should only talk to this component.
    /// </summary>
    public class ScenarioController : MonoBehaviour
    {
        [SerializeField] private ScenarioData scenario;
        [SerializeField] private string participantId = "";
        [SerializeField] private bool startOnPlay = true;
        [SerializeField] private bool writeFilesOnEnd = true;
        [SerializeField] private float targetSecondsPerPatient = 60f;

        [Header("Events (for UI wiring)")]
        public UnityEvent onScenarioStarted;
        public UnityEvent onScenarioEnded;

        public ScenarioSession Session { get; private set; }
        public SessionReport LastReport { get; private set; }
        public string LastLogDirectory { get; private set; }

        /// <summary>Every logged event – e.g. for a live HUD.</summary>
        public event Action<TriageEvent> EventLogged;
        public event Action<SessionReport> ReportReady;

        public bool IsRunning => Session != null && Session.IsRunning;

        private void Start()
        {
            if (startOnPlay) StartScenario();
        }

        private void Update()
        {
            Session?.Tick();
        }

        public void StartScenario()
        {
            if (scenario == null)
            {
                Debug.LogError("[MCI] No ScenarioData assigned.", this);
                return;
            }

            Session = new ScenarioSession(scenario.ToDefinition(), new UnityClock());
            Session.EventLogged += e =>
            {
                Debug.Log($"[MCI] {e}");
                EventLogged?.Invoke(e);
            };
            Session.Ended += OnSessionEnded;
            Session.Start(participantId);
            onScenarioStarted?.Invoke();
        }

        public void FinishScenario()
        {
            if (IsRunning) Session.End(EndReasons.Completed);
        }

        // --- Forwarders for interaction scripts. Return null/false if the scenario is not running.

        public void ApproachPatient(string patientId)
        {
            if (Guard()) Session.ApproachPatient(patientId);
        }

        public string Assess(string patientId, AssessmentType assessment) =>
            Guard() ? Session.Assess(patientId, assessment) : null;

        public string PerformIntervention(string patientId, InterventionType intervention) =>
            Guard() ? Session.PerformIntervention(patientId, intervention) : null;

        public bool AssignTriage(string patientId, TriageCategory category)
        {
            if (!Guard()) return false;
            Session.AssignTriage(patientId, category);
            return true;
        }

        private bool Guard() => Session != null && Session.Tick();

        private void OnSessionEnded(ScenarioSession session)
        {
            LastReport = TriageEvaluator.Evaluate(session.Scenario, session.Log.Events,
                new EvaluationOptions { TargetSecondsPerPatient = targetSecondsPerPatient });
            Debug.Log("[MCI] Scenario ended.\n" + LastReport.ToSummaryText());

            if (writeFilesOnEnd) WriteFiles(session, LastReport);

            ReportReady?.Invoke(LastReport);
            onScenarioEnded?.Invoke();
        }

        private void WriteFiles(ScenarioSession session, SessionReport report)
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "MciTriageLogs");
                Directory.CreateDirectory(dir);
                var participant = string.IsNullOrWhiteSpace(session.ParticipantId) ? "anon" : session.ParticipantId;
                var stem = $"{DateTime.Now:yyyyMMdd_HHmmss}_{session.Scenario.Id}_{participant}";
                File.WriteAllText(Path.Combine(dir, stem + "_events.csv"), session.Log.ToCsv());
                File.WriteAllText(Path.Combine(dir, stem + "_report.txt"), report.ToSummaryText());
                LastLogDirectory = dir;
                Debug.Log($"[MCI] Log written to {dir}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCI] Could not write log files: {ex}");
            }
        }
    }
}
