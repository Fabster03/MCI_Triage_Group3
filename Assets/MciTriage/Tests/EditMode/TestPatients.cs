using MciTriage.Core;

namespace MciTriage.Tests
{
    /// <summary>Shared fixtures. One patient per START branch.</summary>
    internal static class TestPatients
    {
        public static PatientFindings Findings(
            bool walk = false, bool breathing = true, bool afterAirway = false, int rr = 20,
            bool pulse = true, float crt = 1.5f, bool commands = true) =>
            new PatientFindings(walk, breathing, afterAirway, rr, pulse, crt, commands, 90, 97);

        public static PatientDefinition Green(string id = "G") => new PatientDefinition(id, id, "", Findings(walk: true));
        public static PatientDefinition Yellow(string id = "Y") => new PatientDefinition(id, id, "", Findings());
        public static PatientDefinition RedRespiratory(string id = "R") => new PatientDefinition(id, id, "", Findings(rr: 35));
        public static PatientDefinition Black(string id = "B") =>
            new PatientDefinition(id, id, "", Findings(breathing: false, afterAirway: false, rr: 0, pulse: false));

        /// <summary>Yellow, becomes red (no radial pulse) at 100 s unless hemorrhage is controlled.</summary>
        public static PatientDefinition DeterioratingYellow(string id = "D") => new PatientDefinition(id, id, "", Findings(),
            new[] { new PatientStateChange(100, Findings(pulse: false, crt: 3.5f), InterventionType.ControlHemorrhage) });

        public static ScenarioDefinition Scenario(double timeLimit = 300, params PatientDefinition[] patients) =>
            new ScenarioDefinition("test", "Test", "", timeLimit, patients);
    }
}
