using System.Collections.Generic;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// A circle of sea something must stay out of: a fishing ground, a buoy, a rock, the boat.
    /// Flat, like every other "where is it" question about the sea in this project.
    /// </summary>
    public readonly struct PoiKeepClear
    {
        public readonly Vector2 Centre;
        public readonly float Radius;

        public PoiKeepClear(Vector2 centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }
    }

    /// <summary>
    /// Where a point of interest may appear, worked out without a scene, a clock or a network.
    ///
    /// Points appear along the legs between the places a crew actually sails — harbour, North,
    /// Reef — partway along and a little off the line, so they turn up on the way somewhere rather
    /// than as arbitrary marks on an empty sea. Everything here is a plain function of its inputs and
    /// of the random source it is handed, which is what lets a voyage's layout be replayed from its
    /// seed and lets the rules be tested on their own.
    ///
    /// The random source is a System.Random owned by the caller, never Unity's shared one, so nothing
    /// else drawing random numbers can move a voyage's layout.
    /// </summary>
    public static class PoiPlacement
    {
        /// <summary>A number in [min, max], drawn once from the source.</summary>
        public static float Range(System.Random random, float min, float max)
        {
            if (max < min)
            {
                max = min;
            }

            return min + (float)random.NextDouble() * (max - min);
        }

        /// <summary>How many legs a set of stops makes: one between every pair of them.</summary>
        public static int LegCount(int stopCount)
        {
            return stopCount < 2 ? 0 : stopCount * (stopCount - 1) / 2;
        }

        /// <summary>
        /// A candidate point on one leg between the stops: a leg chosen evenly, a distance along it
        /// between the two fractions, and a sideways offset of up to the given metres either side.
        ///
        /// Always draws exactly three numbers, whatever they turn out to be, so the next candidate
        /// drawn from the same source does not depend on whether this one was any good.
        /// </summary>
        public static Vector2 DrawOnLegs(System.Random random, IReadOnlyList<Vector2> stops,
            float minAlong, float maxAlong, float maxOffset)
        {
            int legs = LegCount(stops.Count);
            int leg = legs > 0 ? random.Next(legs) : 0;
            float along = Range(random, minAlong, maxAlong);
            float offset = Range(random, -maxOffset, maxOffset);

            if (legs == 0)
            {
                return stops.Count == 1 ? stops[0] : Vector2.zero;
            }

            LegEnds(stops.Count, leg, out int from, out int to);

            Vector2 start = stops[from];
            Vector2 end = stops[to];
            Vector2 direction = end - start;
            float length = direction.magnitude;

            if (length < 0.001f)
            {
                return start;
            }

            direction /= length;
            Vector2 side = new Vector2(-direction.y, direction.x);

            return start + direction * (length * along) + side * offset;
        }

        /// <summary>
        /// Whether a point is somewhere a point of interest may go: inside the world and outside
        /// every circle it must keep clear of.
        /// </summary>
        public static bool IsClear(Vector2 point, IReadOnlyList<PoiKeepClear> keepClear, float worldHalfExtent)
        {
            if (Mathf.Abs(point.x) > worldHalfExtent || Mathf.Abs(point.y) > worldHalfExtent)
            {
                return false;
            }

            if (keepClear == null)
            {
                return true;
            }

            for (int i = 0; i < keepClear.Count; i++)
            {
                PoiKeepClear circle = keepClear[i];
                if ((point - circle.Centre).sqrMagnitude < circle.Radius * circle.Radius)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Which two stops a leg joins, counting the pairs in order: 0–1, 0–2, …, 1–2, ….
        /// </summary>
        private static void LegEnds(int stopCount, int leg, out int from, out int to)
        {
            for (from = 0; from < stopCount - 1; from++)
            {
                int legsFromHere = stopCount - 1 - from;
                if (leg < legsFromHere)
                {
                    to = from + 1 + leg;
                    return;
                }

                leg -= legsFromHere;
            }

            from = 0;
            to = 1;
        }
    }
}
