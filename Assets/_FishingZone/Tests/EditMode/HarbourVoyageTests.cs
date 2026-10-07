using FishingZone.World;
using NUnit.Framework;

namespace FishingZone.Tests
{
    /// <summary>
    /// When a voyage begins and ends, tested on the rules alone. The harbour is Ocean_01's Harbour Waters
    /// (region 1); North Shallows is region 2 and open sea is no region at all.
    /// </summary>
    public class HarbourVoyageTests
    {
        private const int Harbour = 1;
        private const int NorthShallows = 2;
        private const int OpenSea = RegionDefinition.NoRegion;

        [Test]
        public void ANewHarbour_LiesBerthed()
        {
            Assert.AreEqual(VoyagePhase.Berthed, default(VoyagePhase));
        }

        [Test]
        public void StayingInTheHarbour_NeverDeparts()
        {
            // However much the boat moves about at the quay, it is still in the harbour's water.
            Assert.IsFalse(VoyageRules.ShouldDepart(VoyagePhase.Berthed, Harbour, Harbour));
        }

        [Test]
        public void LeavingForOpenSea_OrAnotherRegion_Departs()
        {
            Assert.IsTrue(VoyageRules.ShouldDepart(VoyagePhase.Berthed, Harbour, OpenSea), "open sea");
            Assert.IsTrue(VoyageRules.ShouldDepart(VoyagePhase.Berthed, Harbour, NorthShallows), "another region");
        }

        [Test]
        public void AVoyageUnderWay_DoesNotDepartAgain()
        {
            Assert.IsFalse(VoyageRules.ShouldDepart(VoyagePhase.UnderWay, Harbour, OpenSea), "still out");
            Assert.IsFalse(VoyageRules.ShouldDepart(VoyagePhase.UnderWay, Harbour, Harbour), "back in the harbour");
        }

        [Test]
        public void EachDeparture_IsTheNextVoyage()
        {
            Assert.AreEqual(1, VoyageRules.NextVoyageNumber(0), "the first voyage");
            Assert.AreEqual(2, VoyageRules.NextVoyageNumber(1), "the one after");
        }

        [Test]
        public void LeavingAndComingBack_StartsExactlyOneVoyage()
        {
            // Out, back into the harbour, and out again: nothing ends a voyage yet, so the second
            // departure from the harbour's water is not a second voyage.
            int[] path = { Harbour, Harbour, OpenSea, NorthShallows, Harbour, OpenSea };
            VoyagePhase phase = VoyagePhase.Berthed;
            int number = 0;
            int departures = 0;

            foreach (int region in path)
            {
                if (VoyageRules.ShouldDepart(phase, Harbour, region))
                {
                    phase = VoyagePhase.UnderWay;
                    number = VoyageRules.NextVoyageNumber(number);
                    departures++;
                }
            }

            Assert.AreEqual(1, departures);
            Assert.AreEqual(1, number);
            Assert.AreEqual(VoyagePhase.UnderWay, phase);
        }

        [Test]
        public void OnlyAVoyageUnderWay_LyingBerthed_CanEnd()
        {
            Assert.IsTrue(VoyageRules.CanEnd(VoyagePhase.UnderWay, BerthStatus.Berthed), "alongside and stopped");
            Assert.IsFalse(VoyageRules.CanEnd(VoyagePhase.UnderWay, BerthStatus.Away), "back in the harbour, off the quay");
            Assert.IsFalse(VoyageRules.CanEnd(VoyagePhase.UnderWay, BerthStatus.TooFast), "at the quay, still moving");
            Assert.IsFalse(VoyageRules.CanEnd(VoyagePhase.Berthed, BerthStatus.Berthed), "no voyage under way");
        }

        [Test]
        public void TwoVoyages_WithoutLeavingTheScene()
        {
            // Out, home into the harbour without berthing, alongside too fast, alongside and ended,
            // then out again: two departures, one ending, and the second voyage is voyage 2.
            VoyagePhase phase = VoyagePhase.Berthed;
            int number = 0;
            int departures = 0;
            int endings = 0;

            void Step(int region, BerthStatus berth)
            {
                if (VoyageRules.ShouldDepart(phase, Harbour, region))
                {
                    phase = VoyagePhase.UnderWay;
                    number = VoyageRules.NextVoyageNumber(number);
                    departures++;
                }
                else if (VoyageRules.CanEnd(phase, berth))
                {
                    phase = VoyagePhase.Berthed;
                    endings++;
                }
            }

            Step(Harbour, BerthStatus.Berthed);
            Step(OpenSea, BerthStatus.Away);
            Step(Harbour, BerthStatus.Away);
            Step(Harbour, BerthStatus.TooFast);
            Assert.AreEqual(VoyagePhase.UnderWay, phase, "home but not berthed");

            Step(Harbour, BerthStatus.Berthed);
            Assert.AreEqual(VoyagePhase.Berthed, phase, "berthed and ended");
            Assert.AreEqual(1, number, "an ended voyage keeps its number");

            Step(Harbour, BerthStatus.Berthed);
            Assert.AreEqual(1, endings, "ending twice is ending once");

            Step(NorthShallows, BerthStatus.Away);
            Assert.AreEqual(VoyagePhase.UnderWay, phase);
            Assert.AreEqual(2, number);
            Assert.AreEqual(2, departures);
        }
    }
}
