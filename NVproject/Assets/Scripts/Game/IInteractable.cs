using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// Anything the interact key can be pointed at. The door and the devices implement it; keys do
    /// not, because walking over a key should pick it up — a Runner being chased has no spare
    /// keypress.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Where the interaction is measured from.</summary>
        Vector3 Position { get; }

        /// <summary>Metres. Beyond this the prompt does not appear.</summary>
        float UseRadius { get; }

        /// <summary>
        /// HUD line, or null to show nothing. Takes the viewer because the same object says
        /// different things to different roles — and to a Seeker the door says nothing at all.
        /// </summary>
        string Prompt(PlayerAgent viewer);

        /// <summary>Called on the interact key. Must re-check everything Prompt checked.</summary>
        void Interact(PlayerAgent user);
    }
}
