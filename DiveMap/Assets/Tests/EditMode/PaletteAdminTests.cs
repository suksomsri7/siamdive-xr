using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// WO-L — the palette rules that depend on WHO is looking. They live apart from
    /// <see cref="PaletteTests"/> for a dull reason worth writing down: that file asserts the
    /// chip translations through <c>UiStrings</c>, which reads PlayerPrefs, so it can only run
    /// under a real Unity Editor. Everything here is pure, which means tools/test.sh answers
    /// "does the admin still get the Warp chip" in two seconds instead of a 35-minute CI round.
    ///
    /// Both rules come from the same place in builder.html: the admin is a different user of
    /// this screen, not a privileged one — they build the official game worlds, so they get the
    /// warp category (<c>PALETTE.SPECIAL = _isAdmin ? … : []</c>, :1399) and they are shown ∞
    /// rather than a balance (<c>coinUI</c>, :4123).
    /// </summary>
    public class PaletteAdminTests
    {
        private static PaletteSource S(string id, string kind, string name = "x", bool glb = true)
            => new PaletteSource { Id = id, Kind = kind, Name = name, HasGlb = glb };

        private const string Base = "https://maps.siamdive.com";

        // ── WO-L: the admin's 🌀 Warp chip (buildCats :1399) ─────────────────────

        [Test]
        public void Warp_IsHiddenFromEveryoneButTheAdmin()
        {
            var rows = new[] { S("warp:0", Palette.Special, "ประตูวาป"),
                               S("cc0:portal", Palette.Special, "portal"),
                               S("cc0:rock_a", Palette.Rock) };

            // The web builds PALETTE.SPECIAL as `_isAdmin ? [...] : []`, so a player must not
            // even see the chip — a category that exists but refuses to place anything is worse
            // than one that is absent.
            Dictionary<string, List<PaletteItem>> player = Palette.Build(rows, Base);
            Assert.IsFalse(player.ContainsKey(Palette.Special));
            CollectionAssert.DoesNotContain(Palette.ChipKinds(player), Palette.Special);

            Dictionary<string, List<PaletteItem>> admin = Palette.Build(rows, Base, includeWarp: true);
            Assert.IsTrue(admin.ContainsKey(Palette.Special));
            CollectionAssert.Contains(Palette.ChipKinds(admin), Palette.Special);
            // warp:0 is procedural (BUILDABLE contains "warp") and is dropped for the admin too —
            // the chip appears because of the CC0 module, not in spite of the filter.
            Assert.AreEqual(1, admin[Palette.Special].Count);
            Assert.AreEqual("cc0:portal", admin[Palette.Special][0].Id);
        }

        [Test]
        public void Warp_IsLastInTheChipOrder()
        {
            // The screenshot has it after ✨ Special and before 📍 Pin, i.e. the last ASSET chip;
            // the three tool chips are appended by the sheet, not by ChipKinds.
            var rows = new[] { S("cc0:portal", Palette.Special), S("cc0:rock_a", Palette.Rock) };
            List<string> chips = Palette.ChipKinds(Palette.Build(rows, Base, includeWarp: true));
            Assert.AreEqual(Palette.Special, chips[chips.Count - 1]);
        }

        // ── WO-L: the coin pill (coinUI :4123) ───────────────────────────────────

        [Test]
        public void CoinLabel_IsInfinityForTheAdminAndTheBalanceForEveryoneElse()
        {
            Assert.AreEqual("∞", Palette.CoinLabel(640, isAdmin: true));
            Assert.AreEqual("∞", Palette.CoinLabel(0, isAdmin: true));
            Assert.AreEqual("640", Palette.CoinLabel(640, isAdmin: false));
            Assert.AreEqual("0", Palette.CoinLabel(0, isAdmin: false));
        }
    }
}
