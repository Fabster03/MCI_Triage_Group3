using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MciTriage.Core
{
    public enum TriageOutcome
    {
        NotTriaged,
        Correct,

        /// <summary>Assigned a less urgent category than the reference (dangerous).</summary>
        UnderTriage,

        /// <summary>Assigned a more urgent category than the reference (wastes resources).</summary>
        OverTriage,
    }

    public sealed class EvaluationOptions
    {
        /// <summary>Target time on a single patient; START aims for well under a minute.</summary>
        public double TargetSecondsPerPatient = 60;
    }

    public sealed class PatientResult
    {
        public string PatientId;
        public string DisplayName;

        public TriageCategory Assigned;

        /// <summary>Reference category at the moment of the (final) assignment, or at scenario end if never triaged.</summary>
        public StartDecision ReferenceAtDecision;

        /// <summary>Reference category at scenario end – differs if the patient changed state after being tagged.</summary>
        public StartDecision ReferenceAtEnd;

        public TriageOutcome Outcome;

        /// <summary>Under-triage of a patient whose reference category is Immediate.</summary>
        public bool CriticalUnderTriage;

        /// <summary>Patient changed category after being tagged and was not re-triaged.</summary>
        public bool OutdatedAtEnd;

        public double? FirstContactTime;
        public double? FinalAssignmentTime;

        /// <summary>Time from first contact to final assignment.</summary>
        public double? DecisionTimeSeconds;

        public bool ExceededTargetTime;
        public int AssignmentCount;

        /// <summary>START steps on the reference path that were not checked before the final assignment.</summary>
        public List<StartStep> MissingSteps = new List<StartStep>();

        /// <summary>All required steps were checked before tagging.</summary>
        public bool ProtocolAdherent;

        public int AssessmentCount;

        /// <summary>Assessments not needed on the START path (incl. repeated checks).</summary>
        public int UnnecessaryAssessmentCount;

        public List<InterventionType> Interventions = new List<InterventionType>();
    }

    public sealed class SessionReport
    {
        public string ScenarioId;
        public string ParticipantId;
        public string EndReason;
        public double TotalDurationSeconds;

        public List<PatientResult> Patients = new List<PatientResult>();

        /// <summary>Patient ids in order of their first triage assignment.</summary>
        public List<string> TriageOrder = new List<string>();

        public int PatientCount => Patients.Count;
        public int TriagedCount => Patients.Count(p => p.Outcome != TriageOutcome.NotTriaged);
        public int CorrectCount => Count(TriageOutcome.Correct);
        public int UnderTriageCount => Count(TriageOutcome.UnderTriage);
        public int OverTriageCount => Count(TriageOutcome.OverTriage);
        public int CriticalUnderTriageCount => Patients.Count(p => p.CriticalUnderTriage);

        /// <summary>Correct / all patients (untriaged patients count as incorrect).</summary>
        public double Accuracy => Ratio(CorrectCount, PatientCount);

        /// <summary>Under-triaged / triaged patients.</summary>
        public double UnderTriageRate => Ratio(UnderTriageCount, TriagedCount);

        /// <summary>Over-triaged / triaged patients.</summary>
        public double OverTriageRate => Ratio(OverTriageCount, TriagedCount);

        /// <summary>Protocol-adherent / triaged patients.</summary>
        public double ProtocolAdherenceRate => Ratio(Patients.Count(p => p.Outcome != TriageOutcome.NotTriaged && p.ProtocolAdherent), TriagedCount);

        public double? MeanDecisionTimeSeconds
        {
            get
            {
                var times = Patients.Where(p => p.DecisionTimeSeconds.HasValue).Select(p => p.DecisionTimeSeconds.Value).ToList();
                return times.Count == 0 ? (double?)null : times.Average();
            }
        }

        private int Count(TriageOutcome o) => Patients.Count(p => p.Outcome == o);
        private static double Ratio(int a, int b) => b == 0 ? 0 : (double)a / b;

        public string ToSummaryText()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"Scenario {ScenarioId} | participant '{ParticipantId}' | ended: {EndReason} after {TotalDurationSeconds.ToString("0.0", ci)} s");
            sb.AppendLine($"Accuracy {Accuracy.ToString("P0", ci)} ({CorrectCount}/{PatientCount}) | " +
                          $"under-triage {UnderTriageRate.ToString("P0", ci)} (critical: {CriticalUnderTriageCount}) | " +
                          $"over-triage {OverTriageRate.ToString("P0", ci)} | " +
                          $"protocol adherence {ProtocolAdherenceRate.ToString("P0", ci)}");
            sb.AppendLine($"Mean decision time: {(MeanDecisionTimeSeconds.HasValue ? MeanDecisionTimeSeconds.Value.ToString("0.0", ci) + " s" : "-")} | " +
                          $"order: {string.Join(" > ", TriageOrder)}");
            foreach (var p in Patients)
            {
                sb.Append($"  {p.PatientId,-6} assigned {p.Assigned.ColorName(),-9} reference {p.ReferenceAtDecision.Category.ColorName(),-6} -> {p.Outcome}");
                if (p.CriticalUnderTriage) sb.Append(" [CRITICAL]");
                if (p.OutdatedAtEnd) sb.Append($" [now {p.ReferenceAtEnd.Category.ColorName()}]");
                if (p.DecisionTimeSeconds.HasValue) sb.Append($" | {p.DecisionTimeSeconds.Value.ToString("0.0", ci)} s");
                if (p.MissingSteps.Count > 0) sb.Append($" | missing: {string.Join(", ", p.MissingSteps)}");
                sb.AppendLine($" | ({p.ReferenceAtDecision.Rationale})");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Scores a run purely from the event log, so any saved log can be re-evaluated
    /// later (e.g. after a scoring rule changes).
    /// </summary>
    public static class TriageEvaluator
    {
        public static SessionReport Evaluate(ScenarioDefinition scenario, IReadOnlyList<TriageEvent> events,
            EvaluationOptions options = null)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (events == null) throw new ArgumentNullException(nameof(events));
            options = options ?? new EvaluationOptions();

            var started = events.FirstOrDefault(e => e.Type == TriageEventType.ScenarioStarted);
            var ended = events.LastOrDefault(e => e.Type == TriageEventType.ScenarioEnded);
            double endTime = ended?.Time ?? (events.Count > 0 ? events.Max(e => e.Time) : 0);

            var report = new SessionReport
            {
                ScenarioId = scenario.Id,
                ParticipantId = started?.Value ?? string.Empty,
                EndReason = ended?.Detail ?? "NotEnded",
                TotalDurationSeconds = endTime,
            };

            foreach (var patient in scenario.Patients)
                report.Patients.Add(EvaluatePatient(patient, events.Where(e => e.PatientId == patient.Id).ToList(), endTime, options));

            report.TriageOrder = events
                .Where(e => e.Type == TriageEventType.TriageAssigned)
                .Select(e => e.PatientId)
                .Distinct()
                .ToList();

            return report;
        }

        private static PatientResult EvaluatePatient(PatientDefinition patient, List<TriageEvent> events,
            double endTime, EvaluationOptions options)
        {
            var result = new PatientResult { PatientId = patient.Id, DisplayName = patient.DisplayName };

            var interventionTimes = new Dictionary<InterventionType, double>();
            foreach (var e in events.Where(e => e.Type == TriageEventType.InterventionPerformed))
            {
                if (Enum.TryParse(e.Detail, out InterventionType type) && !interventionTimes.ContainsKey(type))
                {
                    interventionTimes[type] = e.Time;
                    result.Interventions.Add(type);
                }
            }

            var assignments = events.Where(e => e.Type == TriageEventType.TriageAssigned).ToList();
            var final = assignments.LastOrDefault();
            result.AssignmentCount = assignments.Count;
            result.FirstContactTime = events.Count > 0 ? events.Min(e => e.Time) : (double?)null;
            result.ReferenceAtEnd = StartTriageEngine.Classify(patient.GetFindingsAt(endTime, interventionTimes));

            double decisionTime = final?.Time ?? endTime;
            result.ReferenceAtDecision = StartTriageEngine.Classify(patient.GetFindingsAt(decisionTime, interventionTimes));

            // Information gathering before the final decision.
            var actionsBefore = final == null ? events : events.Take(events.IndexOf(final)).ToList();
            var assessments = actionsBefore
                .Where(e => e.Type == TriageEventType.AssessmentPerformed)
                .Select(e => Enum.TryParse(e.Detail, out AssessmentType a) ? a : (AssessmentType?)null)
                .Where(a => a.HasValue).Select(a => a.Value).ToList();
            var interventions = actionsBefore
                .Where(e => e.Type == TriageEventType.InterventionPerformed)
                .Select(e => Enum.TryParse(e.Detail, out InterventionType i) ? i : InterventionType.None)
                .ToList();

            result.AssessmentCount = assessments.Count;
            result.MissingSteps = result.ReferenceAtDecision.Path
                .Where(step => !assessments.Any(a => StartTriageEngine.IsCoveredBy(step, a)) &&
                               !interventions.Any(i => StartTriageEngine.IsCoveredBy(step, i)))
                .ToList();
            result.ProtocolAdherent = result.MissingSteps.Count == 0;

            // Count assessments that were not needed for the reference path or repeated.
            var usedSteps = new HashSet<StartStep>();
            foreach (var a in assessments)
            {
                var step = result.ReferenceAtDecision.Path.Cast<StartStep?>()
                    .FirstOrDefault(s => StartTriageEngine.IsCoveredBy(s.Value, a) && !usedSteps.Contains(s.Value));
                if (step.HasValue) usedSteps.Add(step.Value);
                else result.UnnecessaryAssessmentCount++;
            }

            if (final == null)
            {
                result.Assigned = TriageCategory.None;
                result.Outcome = TriageOutcome.NotTriaged;
                return result;
            }

            result.Assigned = Enum.TryParse(final.Detail, out TriageCategory assigned) ? assigned : TriageCategory.None;
            result.FinalAssignmentTime = final.Time;
            result.DecisionTimeSeconds = final.Time - result.FirstContactTime;
            result.ExceededTargetTime = result.DecisionTimeSeconds > options.TargetSecondsPerPatient;

            int assignedRank = result.Assigned.UrgencyRank();
            int referenceRank = result.ReferenceAtDecision.Category.UrgencyRank();
            result.Outcome = assignedRank == referenceRank ? TriageOutcome.Correct
                : assignedRank < referenceRank ? TriageOutcome.UnderTriage
                : TriageOutcome.OverTriage;
            result.CriticalUnderTriage = result.Outcome == TriageOutcome.UnderTriage &&
                                         result.ReferenceAtDecision.Category == TriageCategory.Immediate;
            result.OutdatedAtEnd = result.ReferenceAtEnd.Category != result.ReferenceAtDecision.Category &&
                                   result.ReferenceAtEnd.Category != result.Assigned;

            return result;
        }
    }
}
