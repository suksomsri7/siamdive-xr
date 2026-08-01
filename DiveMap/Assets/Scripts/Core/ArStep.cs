namespace DiveMap.Core
{
    /// <summary>
    /// Where the user is in putting the site on the table.
    ///
    /// The old AR mode had no steps at all: it entered, parked the map in front of the face and
    /// left the user with a size stepper. With real tracking there is an order to it, and each step
    /// is a different sentence on screen and a different set of controls — so it is a state rather
    /// than a pile of booleans that can disagree with each other.
    /// </summary>
    public enum ArStep
    {
        /// <summary>Looking for a surface. Nothing is placed and nothing can be.</summary>
        Searching,

        /// <summary>A surface is known. Waiting for the tap that says where.</summary>
        Aiming,

        /// <summary>Placed, but still being moved and resized. Not yet anchored.</summary>
        Adjusting,

        /// <summary>Confirmed and pinned to an ARAnchor — it stays put when you walk around it.</summary>
        Anchored,
    }
}
