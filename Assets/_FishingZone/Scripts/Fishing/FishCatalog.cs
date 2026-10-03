using System.Collections.Generic;
using FishingZone.Core;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// Every fish there is, by id. The one place an id is turned back into a fish.
    ///
    /// A catch travels as a number, and every peer has to name it. Until now that meant searching
    /// whatever lists the scene happened to hold, which stops working the moment a fish can come from
    /// somewhere other than a list on a ground — an event, a rare table, a saved record of something
    /// landed weeks ago. The catalog answers the question without needing to know where the fish came
    /// from.
    ///
    /// Strict on purpose. An id is identity on the wire, so a duplicate would make two fish
    /// indistinguishable and a zero would make a fish read as nothing at all. Problems are reported
    /// loudly when the catalog is first used, and a duplicated id resolves to neither fish rather than
    /// to whichever happened to come first: being unable to name a catch is a visible mistake, naming
    /// it wrongly is a silent one.
    ///
    /// Registered with the persistent services by Bootstrap, so every peer — server and clients —
    /// resolves ids against the same list.
    /// </summary>
    [CreateAssetMenu(fileName = "FishCatalog", menuName = "Fishing Zone/Fish/Fish Catalog")]
    public class FishCatalog : ScriptableObject
    {
        [SerializeField]
        private FishDefinition[] _fish;

        private Dictionary<int, FishDefinition> _byId;

        public IReadOnlyList<FishDefinition> Fish => _fish;

        /// <summary>
        /// Builds the index now and reports any problems, rather than at the first catch. Called when
        /// the catalog is registered, so a broken catalog is said at startup.
        /// </summary>
        public void Initialize()
        {
            _byId = null;
            EnsureIndex();
        }

        /// <summary>The fish with this id, if exactly one valid fish in the catalog has it.</summary>
        public bool TryGet(int id, out FishDefinition fish)
        {
            EnsureIndex();
            return _byId.TryGetValue(id, out fish);
        }

        /// <summary>
        /// Whether this exact fish can be named through the catalog. Not merely its id: a stray
        /// definition sharing an id with a catalogued one is not the catalogued fish.
        /// </summary>
        public bool Resolves(FishDefinition fish)
        {
            return fish != null && TryGet(fish.Id, out FishDefinition found) && found == fish;
        }

        /// <summary>
        /// Everything wrong with this catalog, as sentences. Empty means clean. Pure: logs nothing and
        /// changes nothing, so it can be asked by tools and tests as often as they like.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var seen = new Dictionary<int, FishDefinition>();

            if (_fish == null)
            {
                return problems;
            }

            for (int i = 0; i < _fish.Length; i++)
            {
                FishDefinition fish = _fish[i];
                if (fish == null)
                {
                    problems.Add($"Entry {i} is empty.");
                    continue;
                }

                if (!fish.IsValid)
                {
                    problems.Add($"'{fish.name}' (entry {i}) is not a usable fish: it needs a non-zero Id and a Display Name.");
                    continue;
                }

                if (seen.TryGetValue(fish.Id, out FishDefinition other))
                {
                    problems.Add(other == fish
                        ? $"'{fish.name}' is listed more than once."
                        : $"Fish id {fish.Id} is used by both '{other.name}' and '{fish.name}'; neither can be named until one is given a new id.");
                    continue;
                }

                seen.Add(fish.Id, fish);
            }

            return problems;
        }

        /// <summary>Says once, at the first lookup, everything Validate found.</summary>
        private void EnsureIndex()
        {
            if (_byId != null)
            {
                return;
            }

            _byId = new Dictionary<int, FishDefinition>();
            var ambiguous = new HashSet<int>();

            if (_fish != null)
            {
                for (int i = 0; i < _fish.Length; i++)
                {
                    FishDefinition fish = _fish[i];
                    if (fish == null || !fish.IsValid || ambiguous.Contains(fish.Id))
                    {
                        continue;
                    }

                    if (_byId.TryGetValue(fish.Id, out FishDefinition other) && other != fish)
                    {
                        _byId.Remove(fish.Id);
                        ambiguous.Add(fish.Id);
                        continue;
                    }

                    _byId[fish.Id] = fish;
                }
            }

            foreach (string problem in Validate())
            {
                GameLog.Error(LogCategory.Fish, $"Fish catalog '{name}': {problem}");
            }
        }

        /// <summary>Edited in the Inspector: forget the index so the next lookup sees the change.</summary>
        private void OnValidate()
        {
            _byId = null;
        }

        private void OnEnable()
        {
            _byId = null;
        }
    }
}
