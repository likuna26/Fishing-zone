using System;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Which region the boat is in, as this machine sees it.
    ///
    /// Not networked. The regions come with the scene and the hull's position is already
    /// replicated, so every peer can work it out and nothing needs sending. At a border two peers can
    /// disagree for a frame or two of interpolation; that only decides what someone reads. Anything
    /// the server decides by region should ask <see cref="RegionVolume"/> at the moment it decides,
    /// from its own copy of the boat, as the fishing stations already do for grounds.
    ///
    /// A change is a change of region id, never of name, so renaming a region cannot look like
    /// sailing into a new one.
    /// </summary>
    public class BoatRegionTracker : MonoBehaviour
    {
        /// <summary>The region the boat is in, or null over open sea.</summary>
        public RegionDefinition CurrentRegion { get; private set; }

        public int CurrentRegionId => CurrentRegion != null ? CurrentRegion.Id : RegionDefinition.NoRegion;

        /// <summary>
        /// Raised on this peer when the boat crosses into another region or out to open sea. Carries
        /// the new region, or null.
        /// </summary>
        public event Action<RegionDefinition> RegionChanged;

        /// <summary>
        /// Asked once the hull has moved for the frame, so the answer is about where the boat is now
        /// rather than where it was.
        /// </summary>
        private void LateUpdate()
        {
            RegionDefinition region = RegionVolume.FindRegion(transform.position);
            int id = region != null ? region.Id : RegionDefinition.NoRegion;

            if (id == CurrentRegionId)
            {
                return;
            }

            RegionDefinition previous = CurrentRegion;
            CurrentRegion = region;

            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsServer)
            {
                GameLog.Info(LogCategory.Flow, region != null
                    ? $"The boat entered {region.DisplayName} (region {region.Id})."
                    : $"The boat left {previous.DisplayName} (region {previous.Id}) for open sea.");
            }

            RegionChanged?.Invoke(region);
        }
    }
}
