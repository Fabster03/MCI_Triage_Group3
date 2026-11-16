using System.Linq;
using MciTriage.Core;
using NUnit.Framework;
using static MciTriage.Tests.TestPatients;

namespace MciTriage.Tests
{
    public class TriageEvaluatorTests
    {
        private ManualClock _clock;
        private ScenarioSession _session;

        private void Begin(params PatientDefinition[] patients)
        {
            _clock = new ManualClock();
            _session = new ScenarioSession(Scenario(300, patients), _clock);
            _session.Start("p1");
        }

        private SessionReport Finish()
        {
            if (_session.IsRunning) _session.End();
            return TriageEvaluator.Evaluate(_session.Scenario, _session.Log.Events);
        }

        private PatientResult Result(SessionReport r, string id) => r.Patients.Single(p => p.PatientId == id);

        /// <summary>Performs exactly the checks the START path requires for a stable yellow patient.</summary>
        private void FullStartCheck(string id)
        {
            _session.Assess(id, AssessmentType.Ambulation);
            _session.Assess(id, AssessmentType.Breathing);
            _session.Assess(id, AssessmentType.RespiratoryRate);
            _session.Assess(id, AssessmentType.RadialPulse);
            _session.Assess(id, AssessmentType.MentalStatus);
        }

        [Test]
        public void CorrectUnderAndOverTriage_AreClassified()
        {
            Begin(Yellow("Y"), RedRespiratory("R"), Green("G"));
            _session.AssignTriage("Y", TriageCategory.Delayed);   // correct
            _session.AssignTriage("R", TriageCategory.Delayed);   // under (critical)
            _session.AssignTriage("G", TriageCategory.Immediate); // over
            var r = Finish();

            Assert.AreEqual(TriageOutcome.Correct, Result(r, "Y").Outcome);
            Assert.AreEqual(TriageOutcome.UnderTriage, Result(r, "R").Outcome);
            Assert.IsTrue(Result(r, "R").CriticalUnderTriage);
            Assert.AreEqual(TriageOutcome.OverTriage, Result(r, "G").Outcome);

            Assert.AreEqual(1.0 / 3, r.Accuracy, 1e-9);
            Assert.AreEqual(1.0 / 3, r.UnderTriageRate, 1e-9);
            Assert.AreEqual(1.0 / 3, r.OverTriageRate, 1e-9);
            CollectionAssert.AreEqual(new[] { "Y", "R", "G" }, r.TriageOrder);
        }

        [Test]
        public void LivingPatientTaggedBlack_IsUnderTriage()
        {
            Begin(Yellow("Y"));
            _session.AssignTriage("Y", TriageCategory.Expectant);
            Assert.AreEqual(TriageOutcome.UnderTriage, Result(Finish(), "Y").Outcome);
        }

        [Test]
        public void UntriagedPatients_CountAgainstAccuracy_NotAgainstRates()
        {
            Begin(Yellow("Y"), Black("B"));
            _session.AssignTriage("Y", TriageCategory.Delayed);
            var r = Finish();
            Assert.AreEqual(TriageOutcome.NotTriaged, Result(r, "B").Outcome);
            Assert.AreEqual(0.5, r.Accuracy, 1e-9);
            Assert.AreEqual(0, r.UnderTriageRate, 1e-9);
            Assert.AreEqual(1, r.TriagedCount);
        }

        [Test]
        public void LastAssignmentCounts()
        {
            Begin(RedRespiratory("R"));
            _session.AssignTriage("R", TriageCategory.Delayed);
            _session.AssignTriage("R", TriageCategory.Immediate);
            var p = Result(Finish(), "R");
            Assert.AreEqual(TriageOutcome.Correct, p.Outcome);
            Assert.AreEqual(2, p.AssignmentCount);
        }

        [Test]
        public void ProtocolAdherence_RequiresAllPathSteps_BeforeTagging()
        {
            Begin(Yellow("Full"), Yellow("Partial"));
            FullStartCheck("Full");
            _session.AssignTriage("Full", TriageCategory.Delayed);

            _session.Assess("Partial", AssessmentType.Breathing);
            _session.Assess("Partial", AssessmentType.HeartRate);
            _session.AssignTriage("Partial", TriageCategory.Delayed);
            _session.Assess("Partial", AssessmentType.MentalStatus); // after tagging: does not count

            var r = Finish();
            Assert.IsTrue(Result(r, "Full").ProtocolAdherent);
            Assert.AreEqual(0, Result(r, "Full").UnnecessaryAssessmentCount);

            var partial = Result(r, "Partial");
            Assert.IsFalse(partial.ProtocolAdherent);
            CollectionAssert.AreEquivalent(
                new[] { StartStep.Ambulation, StartStep.RespiratoryRate, StartStep.Perfusion, StartStep.MentalStatus },
                partial.MissingSteps);
            Assert.AreEqual(1, partial.UnnecessaryAssessmentCount); // heart rate
            Assert.AreEqual(0.5, r.ProtocolAdherenceRate, 1e-9);
        }

        [Test]
        public void AirwayIntervention_CoversAirwayStep()
        {
            var p = new PatientDefinition("A", "A", "", Findings(breathing: false, afterAirway: true));
            Begin(p);
            _session.Assess("A", AssessmentType.Ambulation);
            _session.Assess("A", AssessmentType.Breathing);
            _session.PerformIntervention("A", InterventionType.OpenAirway);
            _session.AssignTriage("A", TriageCategory.Immediate);
            var result = Result(Finish(), "A");
            Assert.AreEqual(TriageOutcome.Correct, result.Outcome);
            Assert.IsTrue(result.ProtocolAdherent);
        }

        [Test]
        public void DecisionTime_IsMeasuredFromFirstContact()
        {
            Begin(Yellow("Y"));
            _clock.Advance(10);
            _session.ApproachPatient("Y");
            _clock.Advance(25);
            _session.AssignTriage("Y", TriageCategory.Delayed);
            var p = Result(Finish(), "Y");
            Assert.AreEqual(25, p.DecisionTimeSeconds.Value, 1e-9);
            Assert.IsFalse(p.ExceededTargetTime);
        }

        [Test]
        public void Reference_IsTakenAtDecisionTime_AndMissedRetriageIsFlagged()
        {
            Begin(DeterioratingYellow("D"));
            _clock.Advance(20);
            _session.AssignTriage("D", TriageCategory.Delayed); // correct at t=20
            _clock.Advance(200);                                // deteriorates at t=100
            var p = Result(Finish(), "D");
            Assert.AreEqual(TriageOutcome.Correct, p.Outcome);
            Assert.AreEqual(TriageCategory.Immediate, p.ReferenceAtEnd.Category);
            Assert.IsTrue(p.OutdatedAtEnd);
        }

        [Test]
        public void Report_SummaryText_ContainsKeyNumbers()
        {
            Begin(Yellow("Y"));
            _session.AssignTriage("Y", TriageCategory.Delayed);
            var text = Finish().ToSummaryText();
            StringAssert.Contains("Accuracy 100", text);
            StringAssert.Contains("Y", text);
        }
    }
}
