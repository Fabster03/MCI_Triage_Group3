using System;
using System.Linq;
using MciTriage.Core;
using NUnit.Framework;
using static MciTriage.Tests.TestPatients;

namespace MciTriage.Tests
{
    public class ScenarioSessionTests
    {
        private ManualClock _clock;

        [SetUp]
        public void SetUp() => _clock = new ManualClock { Now = 1000 };

        private ScenarioSession Start(ScenarioDefinition scenario)
        {
            var s = new ScenarioSession(scenario, _clock);
            s.Start("tester");
            return s;
        }

        [Test]
        public void Actions_AreLoggedWithScenarioTime()
        {
            var s = Start(Scenario(300, Yellow()));
            _clock.Advance(5);
            Assert.AreEqual("20", s.Assess("Y", AssessmentType.RespiratoryRate));
            _clock.Advance(3);
            s.AssignTriage("Y", TriageCategory.Delayed);

            var events = s.Log.Events;
            Assert.AreEqual(TriageEventType.ScenarioStarted, events[0].Type);
            Assert.AreEqual("tester", events[0].Value);
            Assert.AreEqual(5, events[1].Time, 1e-9);
            Assert.AreEqual("RespiratoryRate", events[1].Detail);
            Assert.AreEqual(8, events[2].Time, 1e-9);
            Assert.AreEqual(TriageCategory.Delayed, s.GetAssignedCategory("Y"));
            Assert.IsTrue(s.AllPatientsTriaged);
        }

        [Test]
        public void TimeLimit_EndsSession_AndBlocksFurtherActions()
        {
            var s = Start(Scenario(60, Yellow()));
            _clock.Advance(61);
            Assert.IsFalse(s.Tick());
            Assert.AreEqual(SessionState.Ended, s.State);
            Assert.AreEqual(EndReasons.TimeLimit, s.EndReason);
            Assert.AreEqual(60, s.ElapsedSeconds, 1e-9);
            Assert.Throws<InvalidOperationException>(() => s.AssignTriage("Y", TriageCategory.Delayed));
        }

        [Test]
        public void ActionAfterTimeLimit_WithoutTick_IsRejected()
        {
            var s = Start(Scenario(60, Yellow()));
            _clock.Advance(70);
            Assert.Throws<InvalidOperationException>(() => s.Assess("Y", AssessmentType.Breathing));
            Assert.IsFalse(s.Log.Events.Any(e => e.Type == TriageEventType.AssessmentPerformed));
        }

        [Test]
        public void OpenAirway_RevealsBreathing()
        {
            var p = new PatientDefinition("A", "A", "", Findings(breathing: false, afterAirway: true, rr: 8));
            var s = Start(Scenario(300, p));
            Assert.AreEqual("no", s.Assess("A", AssessmentType.Breathing));
            Assert.AreEqual("0", s.Assess("A", AssessmentType.RespiratoryRate));
            Assert.AreEqual("yes", s.PerformIntervention("A", InterventionType.OpenAirway));
            Assert.AreEqual("yes", s.Assess("A", AssessmentType.Breathing));
            Assert.AreEqual("8", s.Assess("A", AssessmentType.RespiratoryRate));
            Assert.AreEqual(TriageCategory.Immediate, s.GetReferenceDecision("A").Category);
        }

        [Test]
        public void Deterioration_HappensWithoutIntervention()
        {
            var s = Start(Scenario(300, DeterioratingYellow()));
            Assert.AreEqual("yes", s.Assess("D", AssessmentType.RadialPulse));
            _clock.Advance(100);
            Assert.AreEqual("no", s.Assess("D", AssessmentType.RadialPulse));
            Assert.AreEqual(TriageCategory.Immediate, s.GetReferenceDecision("D").Category);
        }

        [Test]
        public void Deterioration_IsPreventedByEarlyIntervention()
        {
            var s = Start(Scenario(300, DeterioratingYellow()));
            _clock.Advance(50);
            s.PerformIntervention("D", InterventionType.ControlHemorrhage);
            _clock.Advance(100);
            Assert.AreEqual("yes", s.Assess("D", AssessmentType.RadialPulse));
            Assert.AreEqual(TriageCategory.Delayed, s.GetReferenceDecision("D").Category);
        }

        [Test]
        public void UnknownPatient_Throws()
        {
            var s = Start(Scenario(300, Yellow()));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => s.ApproachPatient("nope"));
        }

        [Test]
        public void ScenarioDefinition_RejectsDuplicateIds()
        {
            Assert.Throws<ArgumentException>(() => Scenario(300, Yellow("X"), Green("X")));
        }

        [Test]
        public void Csv_IsInvariantAndEscaped()
        {
            var log = new EventLog();
            log.Append(new TriageEvent(1.5, TriageEventType.AssessmentPerformed, "P1", "CapillaryRefill", "a,\"b\""));
            var lines = log.ToCsv().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(EventLog.CsvHeader, lines[0]);
            Assert.AreEqual("1.500,AssessmentPerformed,P1,CapillaryRefill,\"a,\"\"b\"\"\"", lines[1]);
        }
    }
}
