using System;
using System.Collections.Generic;
using System.Linq;

namespace MciTriage.Core
{
    /// <summary>
    /// Scripted change of a patient's findings at a fixed scenario time
    /// (deterministic deterioration). Can optionally be prevented by an
    /// intervention performed before <see cref="AtSeconds"/>.
    /// </summary>
    public sealed class PatientStateChange
    {
        public double AtSeconds { get; }
        public PatientFindings Findings { get; }
        public InterventionType PreventedBy { get; }

        public PatientStateChange(double atSeconds, PatientFindings findings,
            InterventionType preventedBy = InterventionType.None)
        {
            if (atSeconds < 0) throw new ArgumentOutOfRangeException(nameof(atSeconds));
            AtSeconds = atSeconds;
            Findings = findings ?? throw new ArgumentNullException(nameof(findings));
            PreventedBy = preventedBy;
        }
    }

    /// <summary>
    /// Static description of one casualty: identity, scene description and
    /// a deterministic timeline of findings.
    /// </summary>
    public sealed class PatientDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public PatientFindings InitialFindings { get; }
        public IReadOnlyList<PatientStateChange> StateChanges { get; }

        public PatientDefinition(string id, string displayName, string description,
            PatientFindings initialFindings, IEnumerable<PatientStateChange> stateChanges = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Patient id required.", nameof(id));
            Id = id;
            DisplayName = displayName ?? id;
            Description = description ?? string.Empty;
            InitialFindings = initialFindings ?? throw new ArgumentNullException(nameof(initialFindings));
            StateChanges = (stateChanges ?? Enumerable.Empty<PatientStateChange>())
                .OrderBy(c => c.AtSeconds)
                .ToList();
        }

        /// <summary>
        /// Findings at <paramref name="elapsedSeconds"/> scenario time.
        /// </summary>
        /// <param name="interventionTimes">
        /// Time at which each intervention was first performed on this patient.
        /// A state change is skipped if its preventing intervention happened at or before it.
        /// </param>
        public PatientFindings GetFindingsAt(double elapsedSeconds,
            IReadOnlyDictionary<InterventionType, double> interventionTimes = null)
        {
            var current = InitialFindings;
            foreach (var change in StateChanges)
            {
                if (change.AtSeconds > elapsedSeconds) break;

                if (change.PreventedBy != InterventionType.None &&
                    interventionTimes != null &&
                    interventionTimes.TryGetValue(change.PreventedBy, out var performedAt) &&
                    performedAt <= change.AtSeconds)
                {
                    continue;
                }

                current = change.Findings;
            }
            return current;
        }
    }
}
