using System;
using System.Collections.Generic;

namespace MciTriage.Core
{
    /// <summary>Decision nodes of the START algorithm.</summary>
    public enum StartStep
    {
        Ambulation,
        Breathing,
        AirwayRepositioning,
        RespiratoryRate,
        Perfusion,
        MentalStatus,
    }

    /// <summary>Result of a START classification including the decision path.</summary>
    public sealed class StartDecision
    {
        public TriageCategory Category { get; }

        /// <summary>The START nodes that had to be evaluated to reach the category, in order.</summary>
        public IReadOnlyList<StartStep> Path { get; }

        /// <summary>Short human-readable reason, e.g. for the debriefing.</summary>
        public string Rationale { get; }

        public StartDecision(TriageCategory category, IReadOnlyList<StartStep> path, string rationale)
        {
            Category = category;
            Path = path;
            Rationale = rationale;
        }
    }

    /// <summary>
    /// Deterministic reference implementation of START
    /// (Simple Triage And Rapid Treatment):
    /// <code>
    /// Walking?                        yes -> MINOR (green)
    /// Breathing?                      no  -> open airway: breathing? yes -> IMMEDIATE, no -> EXPECTANT
    /// Respiratory rate > 30/min?      yes -> IMMEDIATE
    /// Radial pulse absent OR CRT > 2s yes -> IMMEDIATE
    /// Follows simple commands?        no  -> IMMEDIATE
    /// otherwise                           -> DELAYED (yellow)
    /// </code>
    /// </summary>
    public static class StartTriageEngine
    {
        public const int RespiratoryRateThreshold = 30;
        public const float CapillaryRefillThresholdSeconds = 2f;

        public static StartDecision Classify(PatientFindings findings)
        {
            if (findings == null) throw new ArgumentNullException(nameof(findings));

            var path = new List<StartStep> { StartStep.Ambulation };
            if (findings.CanWalk)
                return new StartDecision(TriageCategory.Minor, path, "Able to walk.");

            path.Add(StartStep.Breathing);
            if (!findings.IsBreathing)
            {
                path.Add(StartStep.AirwayRepositioning);
                return findings.BreathesAfterAirwayRepositioning
                    ? new StartDecision(TriageCategory.Immediate, path, "Breathing only after airway repositioning.")
                    : new StartDecision(TriageCategory.Expectant, path, "Not breathing after airway repositioning.");
            }

            path.Add(StartStep.RespiratoryRate);
            if (findings.RespiratoryRate > RespiratoryRateThreshold)
                return new StartDecision(TriageCategory.Immediate, path,
                    FormattableString.Invariant(
                        $"Respiratory rate {findings.RespiratoryRate}/min > {RespiratoryRateThreshold}/min."));

            path.Add(StartStep.Perfusion);
            if (!findings.HasRadialPulse)
                return new StartDecision(TriageCategory.Immediate, path, "Radial pulse absent.");
            if (findings.CapillaryRefillSeconds > CapillaryRefillThresholdSeconds)
                return new StartDecision(TriageCategory.Immediate, path,
                    FormattableString.Invariant(
                        $"Capillary refill {findings.CapillaryRefillSeconds:0.#} s > {CapillaryRefillThresholdSeconds:0} s."));

            path.Add(StartStep.MentalStatus);
            if (!findings.FollowsCommands)
                return new StartDecision(TriageCategory.Immediate, path, "Does not follow simple commands.");

            return new StartDecision(TriageCategory.Delayed, path,
                "Breathing <= 30/min, adequate perfusion, follows commands, unable to walk.");
        }

        /// <summary>
        /// Whether a performed assessment/intervention counts as having evaluated a START step.
        /// Perfusion can be checked via radial pulse or capillary refill.
        /// </summary>
        public static bool IsCoveredBy(StartStep step, AssessmentType assessment)
        {
            switch (step)
            {
                case StartStep.Ambulation: return assessment == AssessmentType.Ambulation;
                case StartStep.Breathing: return assessment == AssessmentType.Breathing;
                case StartStep.RespiratoryRate: return assessment == AssessmentType.RespiratoryRate;
                case StartStep.Perfusion:
                    return assessment == AssessmentType.RadialPulse || assessment == AssessmentType.CapillaryRefill;
                case StartStep.MentalStatus: return assessment == AssessmentType.MentalStatus;
                default: return false;
            }
        }

        public static bool IsCoveredBy(StartStep step, InterventionType intervention)
        {
            return step == StartStep.AirwayRepositioning && intervention == InterventionType.OpenAirway;
        }
    }
}
