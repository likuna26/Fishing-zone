using Unity.Netcode.Components;

namespace FishingZone.Networking
{
    [UnityEngine.DisallowMultipleComponent]
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
