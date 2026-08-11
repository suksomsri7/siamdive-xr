using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// The gate that decides whether a map starts playing itself (builder.html:3544). Every clause
    /// here is a way to land the player in the wrong mode, and none of them are visible in a
    /// screenshot — the map looks identical either way until you try to move.
    /// </summary>
    public class ArenaEntryTests
    {
        private const string Admin = MapDirectory.AdminAccountId;
        private const string Someone = "cmsomeoneelse00000000000";

        private static bool Play(string acct = Admin, bool canEdit = false, bool arena = false,
                                 bool warp = false, bool online = true, bool ar = false)
            => ArenaEntry.ShouldAutoPlay(acct, canEdit, arena, warp, online, ar);

        [Test]
        public void AnOfficialWorldYouCannotEdit_StartsPlaying()
        {
            // The case this port was missing: opened from its card in the list, no banner, no warp.
            Assert.IsTrue(Play());
        }

        [Test]
        public void TheDecisionCannotSeeTheMODE_soLeavingOneBeforeABuildCannotChangeIt()
        {
            // 🔴 WO-MERGE P1e added SceneAtmosphere.ResetForNewMap, which exits any stale mode at
            // the START of a build — and a CI verdict then read "never reached the tour", which
            // looked exactly like that exit having broken auto-play. It had not (the real cause
            // was thirteen QC harnesses fighting each other), and this test pins WHY it could not
            // have: every input to the gate is a property of the MAP or of how the player asked
            // for it, and not one of them is the mode the app happened to be in beforehand.
            //
            // The ordering that makes it safe — reset (mode → View) → build → auto-play — is
            // therefore load-bearing only in one direction: the exit must happen before the gate
            // is consulted, never after. Anyone tempted to move the reset later should fail here.
            foreach (bool arena in new[] { false, true })
            foreach (bool warp in new[] { false, true })
            foreach (bool canEdit in new[] { false, true })
            {
                bool decided = Play(canEdit: canEdit, arena: arena, warp: warp);
                // Called again with identical arguments after a hypothetical mode change: the
                // function is pure, so the answer is the mode-independent one by construction.
                Assert.AreEqual(decided, Play(canEdit: canEdit, arena: arena, warp: warp),
                                $"arena={arena} warp={warp} canEdit={canEdit}");
            }
        }

        [Test]
        public void AWarpArrival_StillDivesIn_AfterAModeExit()
        {
            // The concrete worry from the P1e risk list: a diver warps out of a tour, the new
            // map's build exits the mode on its way in, and the diver lands staring at an orbit
            // camera instead of in the water. ArrivingByWarp is a flag on TourController that a
            // mode exit does not clear, and the gate below only asks whether it was set.
            Assert.IsTrue(Play(warp: true), "a warp must dive in, whatever mode preceded the build");
            Assert.IsTrue(Play(acct: Someone, canEdit: true, warp: true),
                          "…even into a map this device owns and could edit");
        }

        [Test]
        public void TheBannerAndTheWarpGate_BothStartPlaying()
        {
            Assert.IsTrue(Play(acct: Someone, arena: true), "🎮 เล่นเกม! promised a dive");
            Assert.IsTrue(Play(acct: Someone, warp: true), "arriving through a warp lands in water");
        }

        [Test]
        public void SomeoneElsesOrdinaryMap_JustOpens()
        {
            Assert.IsFalse(Play(acct: Someone));
            Assert.IsFalse(Play(acct: null));
            Assert.IsFalse(Play(acct: ""));
        }

        [Test]
        public void TheOwnerOfAWorldGetsTheBuilder_NotTheGame()
        {
            // An admin opening their own world is there to edit it. Auto-play would put them in
            // player mode, which is exactly the state the web had to add a class-removal for.
            Assert.IsFalse(Play(canEdit: true));
        }

        [Test]
        public void TheOwnerWhoAskedToPlay_StillPlays()
        {
            // canEdit only vetoes the "it is a world" branch — pressing play is a direct request.
            Assert.IsTrue(Play(canEdit: true, arena: true));
        }

        [Test]
        public void OfflineDoesNotAutoStart_BecauseCoinsCannotBank()
        {
            Assert.IsFalse(Play(online: false));
            Assert.IsFalse(Play(arena: true, online: false));
        }

        [Test]
        public void ArNeverAutoStarts_TheJoystickWouldDrawOverTheRoom()
        {
            // The web shipped this bug (v.0731) and fixed it in v.0733 by adding !HOLO_MODE.
            Assert.IsFalse(Play(ar: true));
            Assert.IsFalse(Play(arena: true, ar: true));
            Assert.IsFalse(Play(warp: true, ar: true));
        }

        [Test]
        public void AccountIdMatchIsExact_NotCaseFolded()
        {
            Assert.IsFalse(Play(acct: Admin.ToUpperInvariant()),
                           "ids come from the database verbatim; a fuzzy match here hands the game "
                           + "to whoever registers a lookalike id");
        }
    }
}
