using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// P0.5 — the mode rules are the thing every later feature will lean on (tour, game, AR,
    /// builder), so they are pinned here rather than living as `if` chains in MonoBehaviours
    /// that can only be checked by building an APK and looking at it.
    /// </summary>
    public class ModeRulesTests
    {
        [Test]
        public void OrbitBelongsToTheModesThatLookAtTheMapFromOutside()
        {
            Assert.IsTrue(ModeRules.AllowsOrbit(AppMode.View));
            Assert.IsTrue(ModeRules.AllowsOrbit(AppMode.Edit));
            Assert.IsFalse(ModeRules.AllowsOrbit(AppMode.Tour), "a first-person tour must not also orbit");
            Assert.IsFalse(ModeRules.AllowsOrbit(AppMode.Game));
            Assert.IsFalse(ModeRules.AllowsOrbit(AppMode.Ar));
        }

        [Test]
        public void HudAndLandscapeGoWithTheFirstPersonModes()
        {
            foreach (AppMode m in new[] { AppMode.Tour, AppMode.Game })
            {
                Assert.IsTrue(ModeRules.IsFirstPerson(m));
                Assert.IsTrue(ModeRules.ShowsHud(m));
                Assert.IsTrue(ModeRules.LocksLandscape(m));
                Assert.IsFalse(ModeRules.AllowsMenu(m), "the ☰ menu must not sit over the tour HUD");
            }
            foreach (AppMode m in new[] { AppMode.View, AppMode.Edit, AppMode.Ar })
            {
                Assert.IsFalse(ModeRules.ShowsHud(m));
                Assert.IsFalse(ModeRules.LocksLandscape(m));
            }
        }

        [Test]
        public void ViewIsTheHub()
        {
            foreach (AppMode m in new[] { AppMode.Tour, AppMode.Game, AppMode.Ar, AppMode.Edit })
                Assert.IsTrue(ModeRules.CanEnter(AppMode.View, m), $"View→{m} must be allowed");
        }

        [Test]
        public void ExitIsAlwaysPossible_AndAlwaysLandsInView()
        {
            foreach (AppMode m in new[] { AppMode.View, AppMode.Tour, AppMode.Game, AppMode.Ar, AppMode.Edit })
            {
                Assert.AreEqual(AppMode.View, ModeRules.ExitTarget(m));
                if (m != AppMode.View)
                    Assert.IsTrue(ModeRules.CanEnter(m, AppMode.View), $"{m} must be escapable");
            }
        }

        [Test]
        public void TourAndGameSwapDirectly_TheyShareTheRig()
        {
            Assert.IsTrue(ModeRules.CanEnter(AppMode.Tour, AppMode.Game));
            Assert.IsTrue(ModeRules.CanEnter(AppMode.Game, AppMode.Tour));
        }

        [Test]
        public void ArAndEditAreEnteredFromTheMapViewOnly()
        {
            Assert.IsFalse(ModeRules.CanEnter(AppMode.Tour, AppMode.Edit));
            Assert.IsFalse(ModeRules.CanEnter(AppMode.Game, AppMode.Edit));
            Assert.IsFalse(ModeRules.CanEnter(AppMode.Tour, AppMode.Ar));
            Assert.IsFalse(ModeRules.CanEnter(AppMode.Edit, AppMode.Ar), "no AR session inside the builder");
            Assert.IsFalse(ModeRules.CanEnter(AppMode.Ar, AppMode.Edit));
        }

        [Test]
        public void EnteringTheModeYouAreAlreadyInIsNotAMove()
        {
            foreach (AppMode m in new[] { AppMode.View, AppMode.Tour, AppMode.Game, AppMode.Ar, AppMode.Edit })
                Assert.IsFalse(ModeRules.CanEnter(m, m));
        }

        [Test]
        public void AnimalsFreezeInTheBuilder()
        {
            // 🔴 C6 phase 2. The gizmo moves an item by writing its transform and so does
            // WhaleController, so an author placing a shark would be dragging something that
            // swims out from under them. Harmless while only one or two msh:* heroes moved;
            // unusable once all 58 other species got a brain.
            Assert.IsFalse(ModeRules.AnimalsSwim(AppMode.Edit));

            foreach (AppMode m in new[] { AppMode.View, AppMode.Tour, AppMode.Game, AppMode.Ar })
                Assert.IsTrue(ModeRules.AnimalsSwim(m), m.ToString());
        }

        [Test]
        public void AllowsEditTools_TheBuilderRunsInTheModeNamedAfterIt()
        {
            // 🔴 The 9005 regression, pinned. Four tools tested `Current != AppMode.View`, WO-L
            // introduced the only code path that enters Edit, and the result shipped: with the
            // palette open, sculpt/rope/pin closed themselves one frame after being opened and
            // the gizmo dropped its selection — all silently.
            Assert.IsTrue(ModeRules.AllowsEditTools(AppMode.Edit), "the regression");
            Assert.IsTrue(ModeRules.AllowsEditTools(AppMode.View));

            // A gizmo drag during a tour would fight the joystick for the same finger.
            foreach (AppMode m in new[] { AppMode.Tour, AppMode.Game, AppMode.Ar })
                Assert.IsFalse(ModeRules.AllowsEditTools(m), m.ToString());
        }

        [Test]
        public void AllowsEditTools_MatchesTheModesThatKeepTheMenuAndTheOrbit()
        {
            // Edit is View with the palette up, not a different place. If these three rules ever
            // disagree the app gets a mode where you can see the tools but not use them, which is
            // exactly the shape of the bug above.
            foreach (AppMode m in System.Enum.GetValues(typeof(AppMode)) as AppMode[])
            {
                Assert.AreEqual(ModeRules.AllowsOrbit(m), ModeRules.AllowsEditTools(m), m.ToString());
                Assert.AreEqual(ModeRules.AllowsMenu(m), ModeRules.AllowsEditTools(m), m.ToString());
            }
        }

        [Test]
        public void SelectsOnTap_TheAuthorGetsTheGizmoAndEveryoneElseGetsTheCard()
        {
            // WO-N item 6. Whoever can act on the tap owns it.
            Assert.IsTrue(ModeRules.SelectsOnTap(AppMode.Edit, canEdit: true));
            Assert.IsTrue(ModeRules.SelectsOnTap(AppMode.View, canEdit: true));

            // A tour is for looking, even on your own map — the card is the point of tapping.
            Assert.IsFalse(ModeRules.SelectsOnTap(AppMode.Tour, canEdit: true));
            Assert.IsFalse(ModeRules.SelectsOnTap(AppMode.Game, canEdit: true));
            Assert.IsFalse(ModeRules.SelectsOnTap(AppMode.Ar, canEdit: true));

            // No rights → nothing to select, so the card is all a tap can do.
            foreach (AppMode m in System.Enum.GetValues(typeof(AppMode)) as AppMode[])
                Assert.IsFalse(ModeRules.SelectsOnTap(m, canEdit: false), m.ToString());
        }

        [Test]
        public void ShowsInfoCard_IsExactlyTheComplementOfSelectsOnTap()
        {
            // Two rules that can drift are how you end up with a card AND a gizmo on one tap, or
            // neither — the second of which looks like the app ignoring the user.
            foreach (AppMode m in System.Enum.GetValues(typeof(AppMode)) as AppMode[])
                foreach (bool canEdit in new[] { true, false })
                    Assert.AreNotEqual(ModeRules.SelectsOnTap(m, canEdit),
                                       ModeRules.ShowsInfoCard(m, canEdit), $"{m}/{canEdit}");
        }

        [Test]
        public void EditPlayback_IsTheBuildersPlayButtonAndOnlyAffectsEdit()
        {
            // WO-L. ▶ in the palette header is the web's playMode: the author's "let me see it
            // alive" switch. It is the one thing that may overrule the freeze above, and it must
            // not leak — ModeManager clears it on every mode change, and this asserts the rule
            // it is clearing.
            Assert.IsFalse(ModeRules.EditPlayback, "must default to frozen");
            try
            {
                ModeRules.EditPlayback = true;
                Assert.IsTrue(ModeRules.AnimalsSwim(AppMode.Edit));
                foreach (AppMode m in new[] { AppMode.View, AppMode.Tour, AppMode.Game, AppMode.Ar })
                    Assert.IsTrue(ModeRules.AnimalsSwim(m), m.ToString());
            }
            finally
            {
                // Static state shared with every other test in this file — a leak here would
                // silently disarm AnimalsSwim_FurnitureDoesNotSwimAway above, depending on order.
                ModeRules.EditPlayback = false;
            }
            Assert.IsFalse(ModeRules.AnimalsSwim(AppMode.Edit));
        }
    }
}
