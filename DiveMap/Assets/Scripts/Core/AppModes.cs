namespace DiveMap.Core
{
    /// <summary>What the app is currently for. One mode owns the screen at a time.</summary>
    public enum AppMode
    {
        /// <summary>Orbit around the map from outside — what the app does today.</summary>
        View = 0,
        /// <summary>Flying the drone through the map (the web's tour mode).</summary>
        Tour = 1,
        /// <summary>Tour + the trash/coin game layer on top.</summary>
        Game = 2,
        /// <summary>Map placed on a real table through the camera (ARCore).</summary>
        Ar = 3,
        /// <summary>Placing/moving/sculpting — the builder.</summary>
        Edit = 4,
    }

    /// <summary>
    /// P0.5 — the rules about modes, kept pure so they are unit-tested instead of being
    /// re-derived as `if` chains in three MonoBehaviours (the shape AppBoot was heading for:
    /// 443 lines and every feature adding another flag).
    ///
    /// Ported intent from the web: tour and game share the same first-person rig (the game IS
    /// the tour plus trash/coins, `gameInit()` runs inside play mode), AR and Edit are entered
    /// from the map view, and every mode can always fall back to <see cref="AppMode.View"/> —
    /// there is no state a user can get stuck in.
    /// </summary>
    public static class ModeRules
    {
        /// <summary>Orbit-drag belongs to the modes that look at the map from outside.</summary>
        public static bool AllowsOrbit(AppMode mode) => mode == AppMode.View || mode == AppMode.Edit;

        /// <summary>The ☰ menu is hidden while a first-person or AR mode owns the screen.</summary>
        public static bool AllowsMenu(AppMode mode) => mode == AppMode.View || mode == AppMode.Edit;

        /// <summary>Modes that draw a HUD (joysticks, depth, compass, coins…).</summary>
        public static bool ShowsHud(AppMode mode) => mode == AppMode.Tour || mode == AppMode.Game;

        /// <summary>The web locks the tour to landscape (`tourLockLandscape`).</summary>
        public static bool LocksLandscape(AppMode mode) => mode == AppMode.Tour || mode == AppMode.Game;

        /// <summary>First-person modes: the camera is the diver/drone, not an orbit rig.</summary>
        public static bool IsFirstPerson(AppMode mode) => mode == AppMode.Tour || mode == AppMode.Game;

        /// <summary>
        /// May the solo animals swim?
        ///
        /// 🔴 Not in Edit. The builder moves an item by writing its transform, and so does the
        /// animal — so an author trying to place a shark would be dragging something that swims
        /// out from under the gizmo, and one that let go would watch it wander off the spot they
        /// chose. This mattered little while only the <c>msh:*</c> heroes moved (a map has one or
        /// two); C6 phase 2 gave all 58 other species a brain, so a reef of twenty fish would
        /// have become unplaceable. In Edit an animal is furniture — which is exactly what an
        /// author is trying to arrange.
        /// </summary>
        public static bool AnimalsSwim(AppMode mode) => mode != AppMode.Edit;

        /// <summary>Where "exit" lands from <paramref name="mode"/>. Always somewhere usable.</summary>
        public static AppMode ExitTarget(AppMode mode) => AppMode.View;

        /// <summary>
        /// Whether <paramref name="from"/> → <paramref name="to"/> is a legal move.
        /// View is the hub; Tour↔Game is a direct swap (same rig); AR and Edit are entered from
        /// View only, so an AR session can never be half-inside the builder.
        /// </summary>
        public static bool CanEnter(AppMode from, AppMode to)
        {
            if (from == to) return false;
            if (to == AppMode.View) return true;              // exit is always allowed
            if (from == AppMode.View) return true;            // hub → anything
            if (IsFirstPerson(from) && IsFirstPerson(to)) return true;  // tour ↔ game
            return false;
        }
    }
}
