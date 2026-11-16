using System.Collections.Generic;
using System.Linq;
using MciTriage.Core;
using UnityEngine;

namespace MciTriage.Runtime
{
    /// <summary>One incident with its fixed set of casualties (Create > MCI Triage > Scenario).</summary>
    [CreateAssetMenu(fileName = "Scenario", menuName = "MCI Triage/Scenario")]
    public class ScenarioData : ScriptableObject
    {
        public string scenarioId = "scenario";
        public string displayName = "Scenario";
        [TextArea(3, 8)]
        public string briefing;
        [Tooltip("Time limit in seconds; 0 = unlimited.")]
        public float timeLimitSeconds = 300f;
        public List<PatientData> patients = new List<PatientData>();

        public ScenarioDefinition ToDefinition() => new ScenarioDefinition(
            scenarioId, displayName, briefing, timeLimitSeconds,
            patients.Where(p => p != null).Select(p => p.ToDefinition()));
    }
}
