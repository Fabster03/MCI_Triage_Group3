namespace MciTriage.Core
{
    /// <summary>
    /// Immutable snapshot of a patient's observable findings at one point in time.
    /// The first block is what START evaluates; the second block is shown to the
    /// trainee for realism but is not part of the START algorithm.
    /// </summary>
    public sealed class PatientFindings
    {
        // --- START-relevant -------------------------------------------------
        public bool CanWalk { get; }
        public bool IsBreathing { get; }

        /// <summary>Only relevant if <see cref="IsBreathing"/> is false.</summary>
        public bool BreathesAfterAirwayRepositioning { get; }

        /// <summary>Breaths per minute while breathing (spontaneously or after airway opening).</summary>
        public int RespiratoryRate { get; }

        public bool HasRadialPulse { get; }
        public float CapillaryRefillSeconds { get; }
        public bool FollowsCommands { get; }

        // --- Additional vitals (display only) ------------------------------
        public int HeartRate { get; }
        public int OxygenSaturation { get; }

        public PatientFindings(
            bool canWalk,
            bool isBreathing,
            bool breathesAfterAirwayRepositioning,
            int respiratoryRate,
            bool hasRadialPulse,
            float capillaryRefillSeconds,
            bool followsCommands,
            int heartRate = 0,
            int oxygenSaturation = 0)
        {
            CanWalk = canWalk;
            IsBreathing = isBreathing;
            BreathesAfterAirwayRepositioning = breathesAfterAirwayRepositioning;
            RespiratoryRate = respiratoryRate;
            HasRadialPulse = hasRadialPulse;
            CapillaryRefillSeconds = capillaryRefillSeconds;
            FollowsCommands = followsCommands;
            HeartRate = heartRate;
            OxygenSaturation = oxygenSaturation;
        }
    }
}
