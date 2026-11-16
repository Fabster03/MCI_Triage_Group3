namespace MciTriage.Core
{
    /// <summary>
    /// START triage categories. <see cref="None"/> means "not yet triaged".
    /// </summary>
    public enum TriageCategory
    {
        None = 0,

        /// <summary>Green / T3 – walking wounded, minor injuries.</summary>
        Minor = 1,

        /// <summary>Yellow / T2 – serious but treatment can be delayed.</summary>
        Delayed = 2,

        /// <summary>Red / T1 – life-threatening, needs immediate treatment.</summary>
        Immediate = 3,

        /// <summary>Black / T4 – deceased or not survivable with available resources.</summary>
        Expectant = 4,
    }

    public static class TriageCategoryExtensions
    {
        /// <summary>
        /// Urgency rank used to classify under-/over-triage.
        /// Higher = more urgent. Expectant ranks lowest because labelling a
        /// living patient "black" withholds all care (worst possible under-triage).
        /// </summary>
        public static int UrgencyRank(this TriageCategory category)
        {
            switch (category)
            {
                case TriageCategory.Immediate: return 3;
                case TriageCategory.Delayed: return 2;
                case TriageCategory.Minor: return 1;
                case TriageCategory.Expectant: return 0;
                default: return -1;
            }
        }

        public static string ColorName(this TriageCategory category)
        {
            switch (category)
            {
                case TriageCategory.Immediate: return "Red";
                case TriageCategory.Delayed: return "Yellow";
                case TriageCategory.Minor: return "Green";
                case TriageCategory.Expectant: return "Black";
                default: return "Untriaged";
            }
        }
    }
}
