namespace FishingZone.Roles
{
    /// <summary>
    /// The job a player takes for a mission (Technical Specification section 12).
    ///
    /// A role is a choice, not a character class: the same person picks again next time out, so
    /// nothing about a player is permanently tied to one of these.
    ///
    /// Values are written out because they travel over the network as integers, and letting them
    /// shift with a future reordering would silently reassign everyone's job.
    /// </summary>
    public enum PlayerRole
    {
        None = 0,
        Navigator = 1,
        Fisher = 2,
        Observer = 3
    }
}
