namespace FishingZone.Core
{
    /// <summary>
    /// The high-level states the game can be in. Each state (except Boot) owns exactly one scene.
    /// The intended flow is Main Menu -> Lobby -> Expedition, where a sea whose harbour is its shore
    /// keeps the crew across voyages. Port remains for the older route, Main Menu -> Lobby -> Port ->
    /// Expedition -> Port, chosen by <see cref="SceneCatalog.SessionStartState"/>.
    /// </summary>
    public enum GameState
    {
        /// <summary>Bootstrap scene only. Never loaded again after startup.</summary>
        Boot,
        MainMenu,
        Lobby,
        Port,
        Expedition
    }
}
