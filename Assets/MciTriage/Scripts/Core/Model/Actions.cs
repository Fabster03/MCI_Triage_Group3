namespace MciTriage.Core
{
    /// <summary>Observations a trainee can make on a patient.</summary>
    public enum AssessmentType
    {
        Ambulation,
        Breathing,
        RespiratoryRate,
        RadialPulse,
        CapillaryRefill,
        MentalStatus,

        // Not part of START – logged to analyse information-gathering behaviour.
        HeartRate,
        OxygenSaturation,
    }

    /// <summary>
    /// Life-saving interventions permitted during START triage.
    /// </summary>
    public enum InterventionType
    {
        None = 0,

        /// <summary>Reposition the airway of a non-breathing patient (START step).</summary>
        OpenAirway,

        /// <summary>Direct pressure / tourniquet on a major external bleed.</summary>
        ControlHemorrhage,
    }
}
