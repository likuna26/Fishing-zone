using System.Text.RegularExpressions;
using FishingZone.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishingZone.Tests
{
    /// <summary>
    /// Where a session begins, as the catalog says: Port unless told otherwise, Expedition when told
    /// so, and Port again, loudly, for anything a crew cannot begin in.
    /// </summary>
    public class SceneCatalogTests
    {
        private SceneCatalog _catalog;

        [SetUp]
        public void CreateCatalog()
        {
            _catalog = ScriptableObject.CreateInstance<SceneCatalog>();
        }

        [TearDown]
        public void DestroyCatalog()
        {
            Object.DestroyImmediate(_catalog);
        }

        private void SetStart(GameState state)
        {
            var serialized = new SerializedObject(_catalog);
            serialized.FindProperty("_sessionStartState").enumValueIndex = (int)state;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void ANewCatalog_StartsSessionsInPort()
        {
            Assert.AreEqual(GameState.Port, _catalog.SessionStartState);
        }

        [Test]
        public void StartingInExpedition_IsKept()
        {
            SetStart(GameState.Expedition);
            Assert.AreEqual(GameState.Expedition, _catalog.SessionStartState);
        }

        [Test]
        public void StartingInPort_IsKept()
        {
            SetStart(GameState.Port);
            Assert.AreEqual(GameState.Port, _catalog.SessionStartState);
        }

        [Test]
        public void StartingSomewhereNoCrewCanBegin_FallsBackToPort_AndSaysSo()
        {
            SetStart(GameState.Lobby);

            LogAssert.Expect(LogType.Error, new Regex("not somewhere a crew can begin"));
            Assert.AreEqual(GameState.Port, _catalog.SessionStartState);
        }

        [Test]
        public void OnlyPortAndExpedition_AreSessionStarts()
        {
            Assert.IsTrue(SceneCatalog.IsValidSessionStart(GameState.Port));
            Assert.IsTrue(SceneCatalog.IsValidSessionStart(GameState.Expedition));
            Assert.IsFalse(SceneCatalog.IsValidSessionStart(GameState.Boot));
            Assert.IsFalse(SceneCatalog.IsValidSessionStart(GameState.MainMenu));
            Assert.IsFalse(SceneCatalog.IsValidSessionStart(GameState.Lobby));
        }
    }
}
