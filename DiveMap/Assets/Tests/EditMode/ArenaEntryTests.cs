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
