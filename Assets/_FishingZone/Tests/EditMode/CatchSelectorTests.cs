using System.Collections.Generic;
using System.Text.RegularExpressions;
using FishingZone.Fishing;
using FishingZone.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishingZone.Tests
{
    /// <summary>
    /// The rules that decide what bites, tested without a scene, a clock or a network: everything is
    /// built in memory, and every roll is chosen by the test.
    /// </summary>
    public class CatchSelectorTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        // ---- Builders --------------------------------------------------------------------------

        private FishDefinition Fish(int id, string displayName)
        {
            var fish = ScriptableObject.CreateInstance<FishDefinition>();
            fish.name = displayName;
            var so = new SerializedObject(fish);
            so.FindProperty("_id").intValue = id;
            so.FindProperty("_displayName").stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
            _created.Add(fish);
            return fish;
        }

        private RegionDefinition Region(int id, string displayName)
        {
            var region = ScriptableObject.CreateInstance<RegionDefinition>();
            region.name = displayName;
            var so = new SerializedObject(region);
            so.FindProperty("_id").intValue = id;
            so.FindProperty("_displayName").stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
            _created.Add(region);
            return region;
        }

        private FishCatalog Catalog(params FishDefinition[] fish)
        {
            var catalog = ScriptableObject.CreateInstance<FishCatalog>();
            catalog.name = "TestCatalog";
            var so = new SerializedObject(catalog);
            SerializedProperty array = so.FindProperty("_fish");
            array.arraySize = fish.Length;
            for (int i = 0; i < fish.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = fish[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            _created.Add(catalog);
            return catalog;
        }

        private struct E
        {
            public FishDefinition Fish;
            public float Weight;
            public TimeOfDayMask Times;
            public RegionDefinition[] Regions;
            public bool Visible;
        }

        private static E Entry(FishDefinition fish, float weight = 1f, TimeOfDayMask times = TimeOfDayMask.Any,
            RegionDefinition[] regions = null, bool visible = true)
        {
            return new E { Fish = fish, Weight = weight, Times = times, Regions = regions, Visible = visible };
        }

        private FishSpawnTable Table(params E[] entries)
        {
            var table = ScriptableObject.CreateInstance<FishSpawnTable>();
            var so = new SerializedObject(table);
            SerializedProperty list = so.FindProperty("_entries");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_fish").objectReferenceValue = entries[i].Fish;
                element.FindPropertyRelative("_weight").floatValue = entries[i].Weight;
                element.FindPropertyRelative("_timesOfDay").intValue = (int)entries[i].Times;
                element.FindPropertyRelative("_visibleToLookout").boolValue = entries[i].Visible;
                SerializedProperty regions = element.FindPropertyRelative("_regions");
                RegionDefinition[] r = entries[i].Regions ?? new RegionDefinition[0];
                regions.arraySize = r.Length;
                for (int j = 0; j < r.Length; j++)
                {
                    regions.GetArrayElementAtIndex(j).objectReferenceValue = r[j];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            _created.Add(table);
            return table;
        }

        private static CatchContext At(TimeOfDayBand band, int regionId = RegionDefinition.NoRegion)
        {
            return new CatchContext(regionId, true, band);
        }

        // ---- Time of day ------------------------------------------------------------------------

        [Test]
        public void TimeOfDayMask_IncludesExactlyItsBands()
        {
            Assert.IsTrue(TimeOfDayMask.Night.Includes(TimeOfDayBand.Night));
            Assert.IsFalse(TimeOfDayMask.Night.Includes(TimeOfDayBand.Day));
            Assert.IsTrue((TimeOfDayMask.Dawn | TimeOfDayMask.Dusk).Includes(TimeOfDayBand.Dusk));
            Assert.IsFalse((TimeOfDayMask.Dawn | TimeOfDayMask.Dusk).Includes(TimeOfDayBand.Day));
            foreach (TimeOfDayBand band in System.Enum.GetValues(typeof(TimeOfDayBand)))
            {
                Assert.IsTrue(TimeOfDayMask.Any.Includes(band), band.ToString());
                Assert.IsFalse(TimeOfDayMask.None.Includes(band), band.ToString());
            }
        }

        [Test]
        public void NightOnlyEntry_IsChosenAtNight_AndNeverByDay()
        {
            FishDefinition owl = Fish(1, "Owlfish");
            FishCatalog catalog = Catalog(owl);
            var tables = new[] { Table(Entry(owl, 1f, TimeOfDayMask.Night)) };

            Assert.AreSame(owl, CatchSelector.Choose(tables, At(TimeOfDayBand.Night), catalog, 0.5f).Fish);
            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, 0.5f));
            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Dawn), catalog, 0.5f));
            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Dusk), catalog, 0.5f));
        }

        [Test]
        public void WithoutAClock_TimeRestrictionsAreIgnored()
        {
            FishDefinition owl = Fish(1, "Owlfish");
            FishCatalog catalog = Catalog(owl);
            var tables = new[] { Table(Entry(owl, 1f, TimeOfDayMask.Night)) };
            var noClock = new CatchContext(RegionDefinition.NoRegion, false, TimeOfDayBand.Day);

            Assert.AreSame(owl, CatchSelector.Choose(tables, noClock, catalog, 0.5f).Fish);
        }

        // ---- Regions ----------------------------------------------------------------------------

        [Test]
        public void RegionRestrictedEntry_IsChosenOnlyInThatRegion()
        {
            FishDefinition eel = Fish(1, "Reef Eel");
            RegionDefinition reef = Region(9003, "Reef Waters");
            FishCatalog catalog = Catalog(eel);
            var tables = new[] { Table(Entry(eel, 1f, TimeOfDayMask.Any, new[] { reef })) };

            Assert.AreSame(eel, CatchSelector.Choose(tables, At(TimeOfDayBand.Day, 9003), catalog, 0.5f).Fish);
            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Day, 9002), catalog, 0.5f));
            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Day, RegionDefinition.NoRegion), catalog, 0.5f));
        }

        [Test]
        public void RegionMatch_IsByIdNotByNameOrAsset()
        {
            FishDefinition eel = Fish(1, "Reef Eel");
            RegionDefinition reef = Region(9003, "Reef Waters");
            FishCatalog catalog = Catalog(eel);
            var tables = new[] { Table(Entry(eel, 1f, TimeOfDayMask.Any, new[] { reef })) };

            // Renamed: same id, so still the same region.
            var so = new SerializedObject(reef);
            so.FindProperty("_displayName").stringValue = "Coral Shallows";
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual("Coral Shallows", reef.DisplayName);
            Assert.AreSame(eel, CatchSelector.Choose(tables, At(TimeOfDayBand.Day, 9003), catalog, 0.5f).Fish);

            // A different asset carrying the same id and a different name is the same region.
            RegionDefinition twin = Region(9003, "Something Else Entirely");
            var viaTwin = new[] { Table(Entry(eel, 1f, TimeOfDayMask.Any, new[] { twin })) };
            Assert.AreSame(eel, CatchSelector.Choose(viaTwin, At(TimeOfDayBand.Day, reef.Id), catalog, 0.5f).Fish);

            // The same name with a different id is not.
            RegionDefinition impostor = Region(42, "Coral Shallows");
            var viaImpostor = new[] { Table(Entry(eel, 1f, TimeOfDayMask.Any, new[] { impostor })) };
            Assert.IsNull(CatchSelector.Choose(viaImpostor, At(TimeOfDayBand.Day, 9003), catalog, 0.5f));
        }

        // ---- Weighting --------------------------------------------------------------------------

        [Test]
        public void Weights_DivideTheRollInProportion()
        {
            FishDefinition common = Fish(1, "Common");
            FishDefinition rarer = Fish(2, "Rarer");
            FishCatalog catalog = Catalog(common, rarer);
            var tables = new[] { Table(Entry(common, 3f), Entry(rarer, 1f)) };

            const int steps = 10000;
            int rarerCount = 0;
            for (int i = 0; i < steps; i++)
            {
                if (CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, (i + 0.5f) / steps).Fish == rarer)
                {
                    rarerCount++;
                }
            }

            Assert.AreEqual(0.25, rarerCount / (double)steps, 0.001);
        }

        [Test]
        public void SameEntryAcrossTables_AddsItsWeight()
        {
            FishDefinition a = Fish(1, "A");
            FishDefinition b = Fish(2, "B");
            FishCatalog catalog = Catalog(a, b);
            // A appears in two tables at weight 1 each, B once at 2: an even split.
            var tables = new[] { Table(Entry(a, 1f), Entry(b, 2f)), Table(Entry(a, 1f)) };

            const int steps = 10000;
            int aCount = 0;
            for (int i = 0; i < steps; i++)
            {
                if (CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, (i + 0.5f) / steps).Fish == a)
                {
                    aCount++;
                }
            }

            Assert.AreEqual(0.5, aCount / (double)steps, 0.001);
        }

        [Test]
        public void SameRoll_GivesTheSameEntry_AndEdgesAreSafe()
        {
            FishDefinition a = Fish(1, "A");
            FishDefinition b = Fish(2, "B");
            FishCatalog catalog = Catalog(a, b);
            var tables = new[] { Table(Entry(a, 1f), Entry(b, 1f)) };

            for (int i = 0; i <= 20; i++)
            {
                float roll = i / 20f;
                FishSpawnEntry first = CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, roll);
                FishSpawnEntry second = CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, roll);
                Assert.AreSame(first, second, $"roll {roll}");
            }

            Assert.AreSame(a, CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, 0f).Fish);
            Assert.AreSame(b, CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, 1f).Fish);
            Assert.AreSame(b, CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, 7f).Fish);
            Assert.AreSame(a, CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, -3f).Fish);
        }

        // ---- Exclusions and dead water ------------------------------------------------------------

        [Test]
        public void NothingEligible_ReturnsNull_ButTheTablesStillCountAsConfigured()
        {
            FishDefinition owl = Fish(1, "Owlfish");
            FishCatalog catalog = Catalog(owl);
            var tables = new[] { Table(Entry(owl, 1f, TimeOfDayMask.Night)) };

            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, 0.5f));
            Assert.IsTrue(CatchSelector.HasEntries(tables));
            Assert.IsFalse(CatchSelector.HasEntries(new FishSpawnTable[0]));
            Assert.IsFalse(CatchSelector.HasEntries(null));
            Assert.IsFalse(CatchSelector.HasEntries(new[] { Table() }));
        }

        [Test]
        public void UnusableEntries_AreNeverChosen()
        {
            FishDefinition good = Fish(1, "Good");
            FishDefinition unnamed = Fish(2, "");
            FishDefinition zeroId = Fish(0, "Zero");
            FishDefinition uncatalogued = Fish(3, "Stray");
            FishCatalog catalog = Catalog(good, unnamed, zeroId);
            LogAssert.ignoreFailingMessages = true;

            var tables = new[]
            {
                Table(Entry(null, 5f), Entry(unnamed, 5f), Entry(zeroId, 5f), Entry(uncatalogued, 5f),
                    Entry(good, 0f), Entry(good, -1f))
            };
            for (int i = 0; i <= 10; i++)
            {
                Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, i / 10f));
            }

            var withGood = new[] { Table(Entry(uncatalogued, 100f), Entry(good, 1f)) };
            for (int i = 0; i <= 10; i++)
            {
                Assert.AreSame(good, CatchSelector.Choose(withGood, At(TimeOfDayBand.Day), catalog, i / 10f).Fish);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void WithoutACatalog_NothingIsChosen()
        {
            FishDefinition good = Fish(1, "Good");
            var tables = new[] { Table(Entry(good, 1f)) };

            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Day), null, 0.5f));
        }

        [Test]
        public void EveryChosenFish_ResolvesThroughTheCatalog()
        {
            FishDefinition a = Fish(1, "A");
            FishDefinition b = Fish(2, "B");
            FishDefinition c = Fish(3, "C");
            FishCatalog catalog = Catalog(a, b);
            var tables = new[] { Table(Entry(a, 1f), Entry(b, 2f), Entry(c, 50f)) };

            for (int i = 0; i <= 100; i++)
            {
                FishSpawnEntry chosen = CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, i / 100f);
                Assert.IsNotNull(chosen);
                Assert.IsTrue(catalog.TryGet(chosen.Fish.Id, out FishDefinition named));
                Assert.AreSame(chosen.Fish, named);
            }
        }

        // ---- Lookout visibility -----------------------------------------------------------------

        [Test]
        public void HiddenEntries_AreCaughtButNotAnnounced()
        {
            FishDefinition plain = Fish(1, "Plain");
            FishDefinition secret = Fish(2, "Secret");
            FishCatalog catalog = Catalog(plain, secret);
            var tables = new[] { Table(Entry(plain, 1f), Entry(secret, 1f, TimeOfDayMask.Any, null, false)) };

            var announced = new List<FishSpawnEntry>();
            CatchSelector.CollectEligible(tables, At(TimeOfDayBand.Day), catalog, true, announced);
            Assert.AreEqual(1, announced.Count);
            Assert.AreSame(plain, announced[0].Fish);

            var all = new List<FishSpawnEntry>();
            CatchSelector.CollectEligible(tables, At(TimeOfDayBand.Day), catalog, false, all);
            Assert.AreEqual(2, all.Count);

            Assert.AreSame(secret, CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, 0.99f).Fish);
        }

        // ---- Catalog validation -----------------------------------------------------------------

        [Test]
        public void Catalog_ReportsNullZeroInvalidAndDuplicateEntries()
        {
            FishDefinition a = Fish(1, "A");
            FishDefinition twinOfA = Fish(1, "Twin");
            FishDefinition zero = Fish(0, "Zero");
            FishDefinition unnamed = Fish(5, " ");
            FishCatalog catalog = Catalog(a, null, twinOfA, zero, unnamed, a);

            List<string> problems = catalog.Validate();

            Assert.AreEqual(5, problems.Count, string.Join("\n", problems));
            Assert.IsTrue(problems.Exists(p => p.Contains("Entry 1 is empty")));
            Assert.IsTrue(problems.Exists(p => p.Contains("Fish id 1 is used by both")));
            Assert.IsTrue(problems.Exists(p => p.Contains("'Zero'")));
            Assert.IsTrue(problems.Exists(p => p.Contains("'Unnamed'") || p.Contains("entry 4")));
            Assert.IsTrue(problems.Exists(p => p.Contains("listed more than once")));
        }

        [Test]
        public void Catalog_ResolvesNeitherFishOfADuplicatedId_AndLogsTheProblems()
        {
            FishDefinition a = Fish(1, "A");
            FishDefinition twin = Fish(1, "Twin");
            FishDefinition b = Fish(2, "B");
            FishCatalog catalog = Catalog(a, twin, b);

            LogAssert.Expect(LogType.Error, new Regex(@"\[FISH\] Fish catalog 'TestCatalog': Fish id 1 is used by both"));
            catalog.Initialize();

            Assert.IsFalse(catalog.TryGet(1, out _));
            Assert.IsFalse(catalog.Resolves(a));
            Assert.IsFalse(catalog.Resolves(twin));
            Assert.IsTrue(catalog.Resolves(b));

            var tables = new[] { Table(Entry(a, 1f), Entry(twin, 1f)) };
            Assert.IsNull(CatchSelector.Choose(tables, At(TimeOfDayBand.Day), catalog, 0.5f));
        }

        [Test]
        public void Catalog_Clean_HasNoProblems_AndResolvesEveryFish()
        {
            FishDefinition a = Fish(1, "A");
            FishDefinition b = Fish(2, "B");
            FishCatalog catalog = Catalog(a, b);

            Assert.AreEqual(0, catalog.Validate().Count);
            Assert.IsTrue(catalog.TryGet(1, out FishDefinition found));
            Assert.AreSame(a, found);
            Assert.IsTrue(catalog.Resolves(b));
            Assert.IsFalse(catalog.TryGet(3, out _));
        }

        [Test]
        public void ProjectCatalog_IsClean()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FishCatalog>("Assets/_FishingZone/ScriptableObjects/Fish/FishCatalog.asset");
            Assert.IsNotNull(catalog, "The project's FishCatalog asset is missing.");
            Assert.AreEqual(0, catalog.Validate().Count, string.Join("\n", catalog.Validate()));
        }
    }
}
