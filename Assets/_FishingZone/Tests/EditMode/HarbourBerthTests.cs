using FishingZone.World;
using NUnit.Framework;
using UnityEngine;

namespace FishingZone.Tests
{
    /// <summary>
    /// When a boat counts as berthed, tested on the rule alone. The berth is Ocean_01's: the middle
    /// of the quay face at (0, -740), facing out to sea (+Z), 24 m along the quay by 8 m out from
    /// it, with a 1.5 m/s limit.
    /// </summary>
    public class HarbourBerthTests
    {
        private static readonly Vector3 Quay = new Vector3(0f, 1f, -740f);
        private const float Length = 24f;
        private const float Depth = 8f;
        private const float MaxSpeed = 1.5f;

        private static BerthStatus At(float along, float outward, float speed, float heading = 0f)
        {
            Vector3 boat = Quay + Quaternion.Euler(0f, heading, 0f) * new Vector3(along, 0f, outward);
            return HarbourBerth.Evaluate(Quay, heading, Length, Depth, MaxSpeed, boat, speed);
        }

        [Test]
        public void WhereEveryVoyageStarts_IsBerthed()
        {
            // Berth_Start lies 7 m out from the quay face.
            Assert.AreEqual(BerthStatus.Berthed, At(0f, 7f, 0f));
        }

        [Test]
        public void InsideTheBerth_ButMoving_IsTooFast()
        {
            Assert.AreEqual(BerthStatus.TooFast, At(5f, 4f, 2f));
        }

        [Test]
        public void EveryEdgeAndCorner_CountsAsInside()
        {
            Assert.AreEqual(BerthStatus.Berthed, At(-12f, 8f, 0f), "seaward corner, west");
            Assert.AreEqual(BerthStatus.Berthed, At(12f, 8f, 0f), "seaward corner, east");
            Assert.AreEqual(BerthStatus.Berthed, At(-12f, 0f, 0f), "quay corner, west");
            Assert.AreEqual(BerthStatus.Berthed, At(12f, 0f, 0f), "quay corner, east");
            Assert.AreEqual(BerthStatus.Berthed, At(0f, 8f, MaxSpeed), "at the speed limit");
        }

        [Test]
        public void JustOutside_IsAway_HoweverSlow()
        {
            Assert.AreEqual(BerthStatus.Away, At(0f, 8.1f, 0f), "too far out from the quay");
            Assert.AreEqual(BerthStatus.Away, At(12.1f, 4f, 0f), "past the end of the berth");
            Assert.AreEqual(BerthStatus.Away, At(-12.1f, 4f, 0f), "past the other end");
            Assert.AreEqual(BerthStatus.Away, At(0f, -0.1f, 0f), "behind the quay face");
        }

        [Test]
        public void FarAway_IsAway()
        {
            Assert.AreEqual(BerthStatus.Away,
                HarbourBerth.Evaluate(Quay, 0f, Length, Depth, MaxSpeed, new Vector3(-150f, 1f, -250f), 0f), "at North");
            Assert.AreEqual(BerthStatus.Away,
                HarbourBerth.Evaluate(Quay, 0f, Length, Depth, MaxSpeed, new Vector3(0f, 1f, -700f), 0f), "harbour, off the quay");
        }

        [Test]
        public void JustOverTheLimit_IsTooFast()
        {
            Assert.AreEqual(BerthStatus.TooFast, At(0f, 4f, MaxSpeed + 0.01f));
        }

        [Test]
        public void TheRectangle_TurnsWithTheBerth()
        {
            // A quay facing east: "out" is +X and "along" is Z.
            Assert.AreEqual(BerthStatus.Berthed,
                HarbourBerth.Evaluate(Quay, 90f, Length, Depth, MaxSpeed, Quay + new Vector3(6f, 0f, 10f), 0f), "out from an east-facing quay");
            Assert.AreEqual(BerthStatus.Away,
                HarbourBerth.Evaluate(Quay, 90f, Length, Depth, MaxSpeed, Quay + new Vector3(-6f, 0f, 0f), 0f), "behind an east-facing quay");
            Assert.AreEqual(BerthStatus.Away,
                HarbourBerth.Evaluate(Quay, 90f, Length, Depth, MaxSpeed, Quay + new Vector3(4f, 0f, 13f), 0f), "past its end, along Z");
        }

        [Test]
        public void Height_IsIgnored()
        {
            Vector3 boat = Quay + new Vector3(2f, 25f, 3f);
            Assert.AreEqual(BerthStatus.Berthed, HarbourBerth.Evaluate(Quay, 0f, Length, Depth, MaxSpeed, boat, 0.5f));
        }

        [Test]
        public void NoBerthInTheScene_MeansNoneExist()
        {
            // Edit mode runs no OnEnable, so nothing has registered: the station falls back to
            // ending a voyage anywhere, as scenes without a berth always did.
            Assert.IsFalse(HarbourBerth.AnyExist);
            Assert.AreEqual(BerthStatus.Away, HarbourBerth.StatusOf(Quay, 0f));
        }
    }
}
