using System;
using System.Collections.Generic;
using System.Linq;

namespace MciTriage.Core
{
    /// <summary>Source of scenario time. Injected so the core stays deterministic and testable.</summary>
    public interface IClock
    {
        /// <summary>Monotonic time in seconds (absolute; the session subtracts its start time).</summary>
        double Now { get; }
    }

    /// <summary>Clock that only advances when told to – for tests and replays.</summary>
    public sealed class ManualClock : IClock
    {
        public double Now { get; set; }
        public void Advance(double seconds) => Now += seconds;
    }

    /// <summary>One incident: briefing, time limit and the fixed set of casualties.</summary>
    public sealed class ScenarioDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Briefing { get; }

        /// <summary>Time limit in seconds; &lt;= 0 means unlimited.</summary>
        public double TimeLimitSeconds { get; }

        public IReadOnlyList<PatientDefinition> Patients { get; }

        public ScenarioDefinition(string id, string displayName, string briefing,
            double timeLimitSeconds, IEnumerable<PatientDefinition> patients)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Scenario id required.", nameof(id));
            Id = id;
            DisplayName = displayName ?? id;
            Briefing = briefing ?? string.Empty;
            TimeLimitSeconds = timeLimitSeconds;
            Patients = (patients ?? throw new ArgumentNullException(nameof(patients))).ToList();

            if (Patients.Count == 0) throw new ArgumentException("Scenario needs at least one patient.", nameof(patients));
            var duplicate = Patients.GroupBy(p => p.Id).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new ArgumentException($"Duplicate patient id '{duplicate.Key}'.", nameof(patients));
        }

        public PatientDefinition GetPatient(string patientId)
        {
            var patient = Patients.FirstOrDefault(p => p.Id == patientId);
            if (patient == null) throw new KeyNotFoundException($"Unknown patient id '{patientId}'.");
            return patient;
        }
    }
}
