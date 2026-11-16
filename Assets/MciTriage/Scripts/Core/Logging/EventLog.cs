using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MciTriage.Core
{
    public enum TriageEventType
    {
        ScenarioStarted,
        ScenarioEnded,
        PatientApproached,
        AssessmentPerformed,
        InterventionPerformed,
        TriageAssigned,
    }

    /// <summary>
    /// One entry of the central event log. Kept deliberately flat so it maps
    /// 1:1 to a CSV row for later analysis (R / Python / Excel).
    /// </summary>
    public sealed class TriageEvent
    {
        /// <summary>Scenario time in seconds since <see cref="TriageEventType.ScenarioStarted"/>.</summary>
        public double Time { get; }
        public TriageEventType Type { get; }

        /// <summary>Empty for scenario-level events.</summary>
        public string PatientId { get; }

        /// <summary>What was done: assessment / intervention / category name, scenario id, end reason.</summary>
        public string Detail { get; }

        /// <summary>Observed result, e.g. "34" for a respiratory rate or "yes"/"no".</summary>
        public string Value { get; }

        public TriageEvent(double time, TriageEventType type, string patientId = "", string detail = "", string value = "")
        {
            Time = time;
            Type = type;
            PatientId = patientId ?? string.Empty;
            Detail = detail ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public override string ToString() =>
            FormattableString.Invariant($"[{Time,7:0.00}s] {Type} {PatientId} {Detail} {Value}").TrimEnd();
    }

    /// <summary>Append-only central event log for one scenario run.</summary>
    public sealed class EventLog
    {
        private readonly List<TriageEvent> _events = new List<TriageEvent>();

        public IReadOnlyList<TriageEvent> Events => _events;

        /// <summary>Raised after each appended event (e.g. for live UI or network sync).</summary>
        public event Action<TriageEvent> EventAdded;

        public void Append(TriageEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            _events.Add(e);
            EventAdded?.Invoke(e);
        }

        public const string CsvHeader = "time_s,type,patient_id,detail,value";

        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CsvHeader);
            foreach (var e in _events)
            {
                sb.Append(e.Time.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Type).Append(',')
                  .Append(Escape(e.PatientId)).Append(',')
                  .Append(Escape(e.Detail)).Append(',')
                  .Append(Escape(e.Value)).AppendLine();
            }
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
