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
    }
}
