using System.Collections.Generic;
using FishingZone.World;
using NUnit.Framework;
using UnityEngine;

namespace FishingZone.Tests
{
    /// <summary>
    /// Where points of interest may appear, tested without a scene: routes, clearances and seeds are
    /// all built here. The stops are Ocean_01's harbour mouth, North and Reef.
    /// </summary>
    public class PoiPlacementTests
    {
        private static readonly List<Vector2> Stops = new List<Vector2>
        {
            new Vector2(0f, -650f),
            new Vector2(-150f, -250f),
            new Vector2(300f, -450f)
        };

        private static List<Vector2> Draw(int seed, int count)
        {
            var random = new System.Random(seed);
            var points = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                points.Add(PoiPlacement.DrawOnLegs(random, Stops, 0.3f, 0.7f, 60f));
            }

            return points;
        }

        [Test]
        public void SameSeed_GivesSameLayout()
        {
            CollectionAssert.AreEqual(Draw(12345, 50), Draw(12345, 50));
        }

        [Test]
        public void DifferentSeeds_GiveDifferentLayouts()
        {
            CollectionAssert.AreNotEqual(Draw(12345, 50), Draw(54321, 50));
        }

        [Test]
        public void Candidates_LieAlongALeg_WithinTheAllowedOffset()
        {
            foreach (Vector2 point in Draw(777, 500))
            {
                float best = float.MaxValue;
                float bestAlong = 0f;

                for (int a = 0; a < Stops.Count; a++)
                {
                    for (int b = a + 1; b < Stops.Count; b++)
                    {
                        Vector2 direction = Stops[b] - Stops[a];
                        float length = direction.magnitude;
                        direction /= length;
                        Vector2 relative = point - Stops[a];
                        float along = Vector2.Dot(relative, direction) / length;
                        float side = Mathf.Abs(direction.x * relative.y - direction.y * relative.x);

                        if (along >= 0.3f - 0.001f && along <= 0.7f + 0.001f && side < best)
                        {
                            best = side;
                            bestAlong = along;
                        }
                    }
                }

                Assert.LessOrEqual(best, 60f + 0.01f, $"{point} is not within 60 m of any leg between 30% and 70% (closest along {bestAlong:F2}).");
            }
        }

        [Test]
        public void EveryLeg_IsUsed()
        {
            int toNorth = 0, toReef = 0, northReef = 0;
            foreach (Vector2 point in Draw(99, 600))
            {
                // Midpoints of the three legs tell them apart well enough at these distances.
                float dHN = (point - (Stops[0] + Stops[1]) * 0.5f).sqrMagnitude;
                float dHR = (point - (Stops[0] + Stops[2]) * 0.5f).sqrMagnitude;
                float dNR = (point - (Stops[1] + Stops[2]) * 0.5f).sqrMagnitude;
                if (dHN <= dHR && dHN <= dNR) toNorth++;
                else if (dHR <= dNR) toReef++;
                else northReef++;
            }

            Assert.Greater(toNorth, 100);
            Assert.Greater(toReef, 100);
            Assert.Greater(northReef, 100);
        }

        [Test]
        public void IsClear_RefusesKeepClearCircles_AndTheWorldEdge()
        {
            var keepClear = new List<PoiKeepClear>
            {
                new PoiKeepClear(new Vector2(-150f, -250f), 120f),
                new PoiKeepClear(new Vector2(0f, -630f), 40f)
            };

            Assert.IsFalse(PoiPlacement.IsClear(new Vector2(-150f, -150f), keepClear, 950f), "inside ground clearance");
            Assert.IsFalse(PoiPlacement.IsClear(new Vector2(20f, -620f), keepClear, 950f), "on the buoy");
            Assert.IsFalse(PoiPlacement.IsClear(new Vector2(960f, 0f), keepClear, 950f), "outside the world");
            Assert.IsTrue(PoiPlacement.IsClear(new Vector2(75f, -350f), keepClear, 950f), "open sea on a route");
        }

        [Test]
        public void DrawOnLegs_AlwaysDrawsTheSameNumberOfTimes()
        {
            // Whatever a candidate turns out to be, the next draw from the same source must not move.
            var a = new System.Random(5);
            var b = new System.Random(5);
            PoiPlacement.DrawOnLegs(a, Stops, 0.3f, 0.7f, 60f);
            PoiPlacement.DrawOnLegs(b, Stops, 0.1f, 0.9f, 10f);
            Assert.AreEqual(a.Next(), b.Next());
        }

        [Test]
        public void LegCount_IsEveryPair()
        {
            Assert.AreEqual(0, PoiPlacement.LegCount(1));
            Assert.AreEqual(1, PoiPlacement.LegCount(2));
            Assert.AreEqual(3, PoiPlacement.LegCount(3));
            Assert.AreEqual(6, PoiPlacement.LegCount(4));
        }

        [Test]
        public void BirdHours_ExcludeNight()
        {
            TimeOfDayMask birds = TimeOfDayMask.Dawn | TimeOfDayMask.Day | TimeOfDayMask.Dusk;
            Assert.IsTrue(birds.Includes(TimeOfDayBand.Dawn));
            Assert.IsTrue(birds.Includes(TimeOfDayBand.Day));
            Assert.IsTrue(birds.Includes(TimeOfDayBand.Dusk));
            Assert.IsFalse(birds.Includes(TimeOfDayBand.Night));
        }
    }
}
