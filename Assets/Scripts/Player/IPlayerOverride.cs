namespace Weizza
{
    /// <summary>
    /// Something that takes over the player's movement for a while: a vine
    /// swing, a ledge grab, a cannon. While one is running the controller
    /// skips its own state machine and gravity, the override sets the motor's
    /// velocity, and the controller still steps the motor so collisions stay
    /// real.
    ///
    /// One seam rather than a new PlayerState per gadget, so adding a gadget
    /// never touches the rules about what interrupts what.
    /// </summary>
    public interface IPlayerOverride
    {
        /// <summary>Called once a frame. Return false to hand control back.</summary>
        bool TickOverride(PlayerController player, float dt);

        /// <summary>Called once when control is handed back for any reason, including death.</summary>
        void EndOverride(PlayerController player);
    }
}
