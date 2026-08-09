using UnityEngine;

namespace FishingZone.Core.DevTools
{
    /// <summary>
    /// A stand-in interactable for testing PlayerInteraction before real NPCs, stations or shops exist.
    /// It is also the reference example of how little an <see cref="IInteractable"/> has to implement.
    /// </summary>
    public class DebugInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField]
        private string _interactionText = "Press E to test";

        /// <summary>Toggle in the Inspector to verify that a refused interaction is handled cleanly.</summary>
        [SerializeField]
        private bool _canInteract = true;

        /// <summary>Runtime only, so a test run never dirties the scene.</summary>
        private int _interactionCount;

        public bool CanInteract(GameObject interactor)
        {
            return _canInteract;
        }

        public void Interact(GameObject interactor)
        {
            _interactionCount++;

            // Logged under UI because that is the closest existing category; interaction has no
            // category of its own yet, and adding one would mean editing GameLog.
            GameLog.Info(LogCategory.UI, $"INTERACTED with '{name}' by '{interactor.name}' (count: {_interactionCount})");
        }

        public string GetInteractionText(GameObject interactor)
        {
            return _interactionText;
        }
    }
}
