using MciTriage.Core;
using NUnit.Framework;
using static MciTriage.Tests.TestPatients;

namespace MciTriage.Tests
{
    public class StartTriageEngineTests
    {
        [Test]
        public void Walking_IsMinor_AndOnlyAmbulationIsChecked()
        {
            var d = StartTriageEngine.Classify(Findings(walk: true, rr: 40, pulse: false));
            Assert.AreEqual(TriageCategory.Minor, d.Category);
            CollectionAssert.AreEqual(new[] { StartStep.Ambulation }, d.Path);
        }

        [Test]
        public void NotBreathing_BreathesAfterAirway_IsImmediate()
        {
            var d = StartTriageEngine.Classify(Findings(breathing: false, afterAirway: true));
            Assert.AreEqual(TriageCategory.Immediate, d.Category);
            CollectionAssert.AreEqual(new[] { StartStep.Ambulation, StartStep.Breathing, StartStep.AirwayRepositioning }, d.Path);
        }

        [Test]
        public void NotBreathing_NoBreathingAfterAirway_IsExpectant()
        {
            Assert.AreEqual(TriageCategory.Expectant,
                StartTriageEngine.Classify(Findings(breathing: false, afterAirway: false)).Category);
        }

        [TestCase(30, TriageCategory.Delayed)]
        [TestCase(31, TriageCategory.Immediate)]
        public void RespiratoryRate_Threshold(int rr, TriageCategory expected)
        {
            Assert.AreEqual(expected, StartTriageEngine.Classify(Findings(rr: rr)).Category);
        }

        [Test]
        public void NoRadialPulse_IsImmediate()
        {
            Assert.AreEqual(TriageCategory.Immediate, StartTriageEngine.Classify(Findings(pulse: false)).Category);
        }

        [TestCase(2.0f, TriageCategory.Delayed)]
        [TestCase(2.1f, TriageCategory.Immediate)]
        public void CapillaryRefill_Threshold(float crt, TriageCategory expected)
        {
            Assert.AreEqual(expected, StartTriageEngine.Classify(Findings(crt: crt)).Category);
        }

        [Test]
        public void DoesNotFollowCommands_IsImmediate()
        {
            Assert.AreEqual(TriageCategory.Immediate, StartTriageEngine.Classify(Findings(commands: false)).Category);
        }

        [Test]
        public void AllStable_IsDelayed_WithFullPath()
        {
            var d = StartTriageEngine.Classify(Findings());
            Assert.AreEqual(TriageCategory.Delayed, d.Category);
            CollectionAssert.AreEqual(new[]
            {
                StartStep.Ambulation, StartStep.Breathing, StartStep.RespiratoryRate,
                StartStep.Perfusion, StartStep.MentalStatus,
            }, d.Path);
        }

        [Test]
        public void Perfusion_IsCoveredByPulseOrCapillaryRefill()
        {
            Assert.IsTrue(StartTriageEngine.IsCoveredBy(StartStep.Perfusion, AssessmentType.RadialPulse));
            Assert.IsTrue(StartTriageEngine.IsCoveredBy(StartStep.Perfusion, AssessmentType.CapillaryRefill));
            Assert.IsFalse(StartTriageEngine.IsCoveredBy(StartStep.Perfusion, AssessmentType.HeartRate));
        }

        [Test]
        public void UrgencyRank_OrdersCategories()
        {
            Assert.Greater(TriageCategory.Immediate.UrgencyRank(), TriageCategory.Delayed.UrgencyRank());
            Assert.Greater(TriageCategory.Delayed.UrgencyRank(), TriageCategory.Minor.UrgencyRank());
            Assert.Greater(TriageCategory.Minor.UrgencyRank(), TriageCategory.Expectant.UrgencyRank());
        }
    }
}
