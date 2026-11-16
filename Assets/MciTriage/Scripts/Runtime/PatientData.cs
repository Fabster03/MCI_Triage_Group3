using System;
using System.Collections.Generic;
using System.Linq;
using MciTriage.Core;
using UnityEngine;

namespace MciTriage.Runtime
{
    /// <summary>Inspector-editable version of <see cref="PatientFindings"/>.</summary>
    [Serializable]
    public class FindingsData
    {
        [Header("START")]
        public bool canWalk;
        public bool isBreathing = true;
        [Tooltip("Only relevant if not breathing.")]
        public bool breathesAfterAirwayRepositioning;
        [Tooltip("Breaths per minute while breathing.")]
        public int respiratoryRate = 16;
        public bool hasRadialPulse = true;
        [Tooltip("Capillary refill time in seconds.")]
        public float capillaryRefillSeconds = 1.5f;
        public bool followsCommands = true;

        [Header("Additional vitals (display only)")]
        public int heartRate = 80;
        public int oxygenSaturation = 98;

        public PatientFindings ToFindings() => new PatientFindings(
            canWalk, isBreathing, breathesAfterAirwayRepositioning, respiratoryRate,
            hasRadialPulse, capillaryRefillSeconds, followsCommands, heartRate, oxygenSaturation);
    }

    [Serializable]
    public class StateChangeData
    {
        [Tooltip("Scenario time in seconds at which the patient changes state.")]
        public float atSeconds = 120f;
        [Tooltip("If this intervention was performed before 'atSeconds', the change does not happen.")]
        public InterventionType preventedBy = InterventionType.None;
        public FindingsData findings = new FindingsData();

        public PatientStateChange ToStateChange() =>
            new PatientStateChange(atSeconds, findings.ToFindings(), preventedBy);
    }

    /// <summary>One casualty, authored as an asset (Create > MCI Triage > Patient).</summary>
    [CreateAssetMenu(fileName = "Patient", menuName = "MCI Triage/Patient")]
    public class PatientData : ScriptableObject
    {
        public string patientId = "P01";
        public string displayName = "Patient 1";
        [TextArea(3, 8)]
        [Tooltip("What the trainee sees at first glance (visible injuries, position, behaviour).")]
        public string description;

        public FindingsData initialFindings = new FindingsData();
        public List<StateChangeData> stateChanges = new List<StateChangeData>();

        public PatientDefinition ToDefinition() => new PatientDefinition(
            patientId, displayName, description, initialFindings.ToFindings(),
            stateChanges.Select(c => c.ToStateChange()));

        /// <summary>Reference category of the initial state – shown in the inspector to help authoring.</summary>
        public StartDecision InitialReference => StartTriageEngine.Classify(initialFindings.ToFindings());
    }
}
