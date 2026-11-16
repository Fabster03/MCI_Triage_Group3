using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MciTriage.Core
{
    public enum SessionState
    {
        NotStarted,
        Running,
        Ended,
    }

    public static class EndReasons
    {
        public const string Completed = "Completed";
        public const string TimeLimit = "TimeLimit";
        public const string Aborted = "Aborted";
    }

    /// <summary>
    /// Runtime state of one scenario run. All trainee actions go through this class,
    /// which reads patient findings at the current scenario time and writes every
    /// action to the <see cref="EventLog"/>. No Unity dependency.
    /// </summary>
    public sealed class ScenarioSession
    {
        private readonly IClock _clock;
        private readonly Dictionary<string, PatientRuntime> _patients;
        private double _startTime;
        private double _endElapsed;

        public ScenarioDefinition Scenario { get; }
        public EventLog Log { get; }
        public SessionState State { get; private set; } = SessionState.NotStarted;
        public string ParticipantId { get; private set; } = string.Empty;
        public string EndReason { get; private set; } = string.Empty;

        public bool IsRunning => State == SessionState.Running;

        public double ElapsedSeconds
        {
            get
            {
                switch (State)
                {
                    case SessionState.Running: return _clock.Now - _startTime;
                    case SessionState.Ended: return _endElapsed;
                    default: return 0;
                }
            }
        }

        public bool HasTimeLimit => Scenario.TimeLimitSeconds > 0;

        public double RemainingSeconds =>
            HasTimeLimit ? Math.Max(0, Scenario.TimeLimitSeconds - ElapsedSeconds) : double.PositiveInfinity;

        public bool AllPatientsTriaged => _patients.Values.All(p => p.Assigned != TriageCategory.None);

        public event Action<TriageEvent> EventLogged;
        public event Action<ScenarioSession> Ended;

        public ScenarioSession(ScenarioDefinition scenario, IClock clock, EventLog log = null)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Log = log ?? new EventLog();
            _patients = scenario.Patients.ToDictionary(p => p.Id, p => new PatientRuntime(p));
        }

        // ------------------------------------------------------------------ lifecycle

        public void Start(string participantId = "")
        {
            if (State != SessionState.NotStarted) throw new InvalidOperationException("Session already started.");
            ParticipantId = participantId ?? string.Empty;
            _startTime = _clock.Now;
            State = SessionState.Running;
            Write(TriageEventType.ScenarioStarted, "", Scenario.Id, ParticipantId);
        }

        /// <summary>Call once per frame. Ends the session when the time limit is reached.</summary>
        /// <returns>True if the session is still running.</returns>
        public bool Tick()
        {
            if (!IsRunning) return false;
            if (HasTimeLimit && ElapsedSeconds >= Scenario.TimeLimitSeconds)
            {
                EndAt(Scenario.TimeLimitSeconds, EndReasons.TimeLimit);
                return false;
            }
            return true;
        }

        public void End(string reason = EndReasons.Completed)
        {
            EnsureRunning();
            EndAt(ElapsedSeconds, reason);
        }

        private void EndAt(double elapsed, string reason)
        {
            _endElapsed = elapsed;
            EndReason = reason;
            Log.Append(new TriageEvent(elapsed, TriageEventType.ScenarioEnded, "", reason));
            EventLogged?.Invoke(Log.Events[Log.Events.Count - 1]);
            State = SessionState.Ended;
            Ended?.Invoke(this);
        }

        // ------------------------------------------------------------------ trainee actions

        /// <summary>Trainee turns to / reaches a patient (used for time-on-patient measures).</summary>
        public void ApproachPatient(string patientId)
        {
            EnsureRunning();
            GetRuntime(patientId);
            Write(TriageEventType.PatientApproached, patientId);
        }

        /// <summary>Performs an observation and returns the observed value as display text.</summary>
        public string Assess(string patientId, AssessmentType assessment)
        {
            EnsureRunning();
            var runtime = GetRuntime(patientId);
            var findings = CurrentFindings(runtime);
            var value = Observe(runtime, findings, assessment);
            Write(TriageEventType.AssessmentPerformed, patientId, assessment.ToString(), value);
            return value;
        }

        /// <summary>
        /// Performs an intervention. For <see cref="InterventionType.OpenAirway"/> the
        /// return value says whether the patient breathes afterwards ("yes"/"no").
        /// </summary>
        public string PerformIntervention(string patientId, InterventionType intervention)
        {
            EnsureRunning();
            if (intervention == InterventionType.None) throw new ArgumentException("No intervention given.", nameof(intervention));
            var runtime = GetRuntime(patientId);
            var now = ElapsedSeconds;

            string value;
            if (intervention == InterventionType.OpenAirway)
            {
                var findings = CurrentFindings(runtime);
                runtime.AirwayOpened = true;
                value = YesNo(findings.IsBreathing || findings.BreathesAfterAirwayRepositioning);
            }
            else
            {
                value = "done";
            }

            if (!runtime.InterventionTimes.ContainsKey(intervention))
                runtime.InterventionTimes[intervention] = now;

            Write(TriageEventType.InterventionPerformed, patientId, intervention.ToString(), value);
            return value;
        }

        /// <summary>Assigns (or re-assigns) a triage tag. The last assignment counts for scoring.</summary>
        public void AssignTriage(string patientId, TriageCategory category)
        {
            EnsureRunning();
            if (category == TriageCategory.None) throw new ArgumentException("Cannot assign 'None'.", nameof(category));
            var runtime = GetRuntime(patientId);
            runtime.Assigned = category;
            Write(TriageEventType.TriageAssigned, patientId, category.ToString());
        }

        // ------------------------------------------------------------------ queries

        public TriageCategory GetAssignedCategory(string patientId) => GetRuntime(patientId).Assigned;

        /// <summary>True once the airway of this patient has been repositioned (affects visible breathing).</summary>
        public bool IsAirwayOpened(string patientId) => GetRuntime(patientId).AirwayOpened;

        /// <summary>Ground-truth findings right now (for UI such as avatars/animations, not for the trainee's card).</summary>
        public PatientFindings GetCurrentFindings(string patientId) => CurrentFindings(GetRuntime(patientId));

        /// <summary>Reference START category right now.</summary>
        public StartDecision GetReferenceDecision(string patientId) =>
            StartTriageEngine.Classify(GetCurrentFindings(patientId));

        // ------------------------------------------------------------------ internals

        private PatientFindings CurrentFindings(PatientRuntime runtime) =>
            runtime.Definition.GetFindingsAt(ElapsedSeconds, runtime.InterventionTimes);

        private static string Observe(PatientRuntime runtime, PatientFindings f, AssessmentType assessment)
        {
            bool breathingNow = f.IsBreathing || (runtime.AirwayOpened && f.BreathesAfterAirwayRepositioning);
            var ci = CultureInfo.InvariantCulture;
            switch (assessment)
            {
                case AssessmentType.Ambulation: return YesNo(f.CanWalk);
                case AssessmentType.Breathing: return YesNo(breathingNow);
                case AssessmentType.RespiratoryRate: return (breathingNow ? f.RespiratoryRate : 0).ToString(ci);
                case AssessmentType.RadialPulse: return YesNo(f.HasRadialPulse);
                case AssessmentType.CapillaryRefill: return f.CapillaryRefillSeconds.ToString("0.0", ci);
                case AssessmentType.MentalStatus: return YesNo(f.FollowsCommands);
                case AssessmentType.HeartRate: return f.HeartRate.ToString(ci);
                case AssessmentType.OxygenSaturation: return f.OxygenSaturation.ToString(ci);
                default: throw new ArgumentOutOfRangeException(nameof(assessment), assessment, null);
            }
        }

        private static string YesNo(bool b) => b ? "yes" : "no";

        private void Write(TriageEventType type, string patientId, string detail = "", string value = "")
        {
            var e = new TriageEvent(ElapsedSeconds, type, patientId, detail, value);
            Log.Append(e);
            EventLogged?.Invoke(e);
        }

        private void EnsureRunning()
        {
            // Tick first so no action can be logged after the time limit has passed.
            if (!Tick()) throw new InvalidOperationException($"Session is not running (state: {State}).");
        }

        private PatientRuntime GetRuntime(string patientId)
        {
            if (patientId == null || !_patients.TryGetValue(patientId, out var runtime))
                throw new KeyNotFoundException($"Unknown patient id '{patientId}'.");
            return runtime;
        }

        private sealed class PatientRuntime
        {
            public readonly PatientDefinition Definition;
            public readonly Dictionary<InterventionType, double> InterventionTimes = new Dictionary<InterventionType, double>();
            public TriageCategory Assigned = TriageCategory.None;
            public bool AirwayOpened;

            public PatientRuntime(PatientDefinition definition) => Definition = definition;
        }
    }
}
