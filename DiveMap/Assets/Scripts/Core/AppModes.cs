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
        public static bool AnimalsSwim(AppMode mode) => mode != AppMode.Edit || EditPlayback;

        /// <summary>
        /// WO-L — the web's <c>playMode</c>, the ▶ button in the top-right of the builder
        /// (builder.html:292, toggled at :3903 where the icon swaps to ❚❚ and the button turns
        /// teal). It is the author's "let me see it alive" switch: the animals an author has just
        /// arranged start swimming, and pressing it again freezes them so they can be arranged
        /// some more.
        ///
        /// It lives here, as the one exception to <see cref="AnimalsSwim"/>, rather than as an
        /// argument, because the single consumer is deep in the animal brain
        /// (Runtime/Marine/WhaleController.cs:615) and threading a flag down to it would mean
        /// editing marine code for a UI feature. A property keeps that file untouched.
        ///
        /// Always false outside Edit — <see cref="ModeManager"/> clears it on every mode change,
        /// so a tour can never inherit a frozen reef from a builder session.
        /// </summary>
        public static bool EditPlayback { get; set; }

        /// <summary>
        /// May the builder's tools run — gizmo, sculpt, rope, pin?
        ///
        /// 🔴 WO-N, and the reason this is a named rule rather than four copies of
        /// `Current != AppMode.View`. Those four copies are what every tool used to test, from
        /// back when the map view was the only place editing happened. WO-L then introduced the
        /// 🛒 button, which is the first thing in the app's history to enter <see cref="AppMode.Edit"/>
        /// — and so made the mode literally named "the builder" the one mode in which no builder
        /// tool would run. On build 9005 that shipped: with the palette open, ☰ → sculpt/rope/pin
        /// opened a panel that closed itself one frame later with no message, and any selected
        /// object was silently dropped by GizmoController's per-frame deselect.
        ///
        /// Both modes qualify because both look at the map from outside and both allow the menu
        /// (<see cref="AllowsOrbit"/>, <see cref="AllowsMenu"/>) — Edit is View with the palette
        /// up, not a different place. The first-person and AR modes still refuse: a gizmo drag
        /// during a tour would fight the joystick for the same finger.
        /// </summary>
        public static bool AllowsEditTools(AppMode mode) => mode == AppMode.View || mode == AppMode.Edit;

        /// <summary>
        /// Does a tap on an object SELECT it (gizmo) rather than open the read card?
        ///
        /// The web has no info card at all: `canvas pointerup` → `select(rootOf(hit))`
        /// (builder.html:3060-3096) attaches TransformControls and shows #seltool, full stop.
        /// Ours grew a card because the app is also a viewer — but an author who taps a rock to
        /// move it does not want an animal fact sheet in the way, which is exactly what the user
        /// reported on 9005.
        ///
        /// So the tap is owned by whoever can act on it: an account that may edit this map, in a
        /// mode where the tools run, gets the gizmo. Everyone else — every viewer, and everyone
        /// in a tour — keeps the card, because for them a tap has nothing else to do.
        /// </summary>
        public static bool SelectsOnTap(AppMode mode, bool canEdit) => canEdit && AllowsEditTools(mode);

        /// <summary>The other side of <see cref="SelectsOnTap"/>; the two must never both be true.</summary>
        public static bool ShowsInfoCard(AppMode mode, bool canEdit) => !SelectsOnTap(mode, canEdit);

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
