namespace Weizza
{
    /// <summary>
    /// A scene object whose state survives a save: a door left open, a wall
    /// broken, a page picked up.
    ///
    /// State is a short string, not a structure, because StorySave only keeps
    /// numbers, text and true/false. A door is "1" or "0"; anything richer
    /// packs itself into a string and unpacks it again.
    /// </summary>
    public interface ISaveable
    {
        /// <summary>Unique key in the save. Null means this instance opts out.</summary>
        string SaveKey { get; }

        string CaptureState();

        void RestoreState(string state);
    }
}
