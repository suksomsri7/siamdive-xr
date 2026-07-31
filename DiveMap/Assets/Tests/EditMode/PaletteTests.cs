using System.Collections.Generic;
using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the palette — the screen where placing an object spends real coins, so a
    /// grouping bug is not cosmetic: put a <c>losin:</c> animal in the free ROCK tab and it is
    /// given away, drop the price sort and the cheapest fish is unreachable behind 80 cards.
    ///
    /// The rules asserted here are builder.html's, cited per test.
    /// </summary>
    public class PaletteTests
    {
        private static PaletteSource S(string id, string kind, string name = "x", bool glb = true)
            => new PaletteSource { Id = id, Kind = kind, Name = name, HasGlb = glb };

        private const string Base = "https://maps.siamdive.com";

        // ── fold (builder.html:1322) ─────────────────────────────────────────────

        [Test]
        public void Fold_LegacyKindsCollapseIntoTheDisplayCategories()
        {
            Assert.AreEqual(Palette.Coral, Palette.FoldKind("ANEMONE", "cor:1"));
            Assert.AreEqual(Palette.Coral, Palette.FoldKind("PLANT", "nat:x"));
            Assert.AreEqual(Palette.MarineLife, Palette.FoldKind("TURTLE", "msh:turtle"));
            Assert.AreEqual(Palette.School, Palette.FoldKind("FISH", "fish:1"));
        }

        [Test]
        public void Fold_TheOneBigProceduralFishGoesTheOtherWay()
        {
            // "fish:3 = the one big procedural fish" — it is an animal, not a shoal.
            Assert.AreEqual(Palette.MarineLife, Palette.FoldKind("FISH", "fish:3"));
            Assert.AreEqual(Palette.School, Palette.FoldKind("FISH", "fish:2"));
        }

        [Test]
        public void Fold_LeavesRealKindsAlone()
        {
            Assert.AreEqual(Palette.Rock, Palette.FoldKind("ROCK", "cc0:rock_c"));
            Assert.AreEqual(Palette.Artificial, Palette.FoldKind("ARTIFICIAL", "art:1268"));
        }

        // ── drops (builder.html:734 BUILDABLE · :2678 PALETTE_HIDE) ──────────────

        [Test]
        public void Procedural_PlaceholdersAreRecognisedByIdPrefix()
        {
            foreach (string id in new[] { "rock:0", "coral:2", "anemone:1", "fish:3", "turtle:0",
                                          "wreck:1", "warp:0" })
                Assert.IsTrue(Palette.IsProcedural(id), id + " is a procedural placeholder");

            foreach (string id in new[] { "cc0:rock_c", "losin:clownfish", "art:1268", "msh:whale" })
                Assert.IsFalse(Palette.IsProcedural(id), id + " is a real model");

            Assert.IsFalse(Palette.IsProcedural(null));
            Assert.IsFalse(Palette.IsProcedural("nocolon"), "no colon = not an id prefix");
        }

        [Test]
        public void Hidden_IdsTheOwnerPulledFromTheMenu()
        {
            Assert.IsTrue(Palette.IsHidden("cc0:wreck_car"));
            Assert.IsTrue(Palette.IsHidden("nat:palm"));
            Assert.IsTrue(Palette.IsHidden("glb_turtle_loggerhead"), "user removed it 2026-07-04");
            Assert.IsFalse(Palette.IsHidden("cc0:rock_c"));
        }

        [Test]
        public void Build_DropsProceduralHiddenAndAdminOnlyRows()
        {
            var src = new[]
            {
                S("rock:0", "ROCK"),              // procedural
                S("cc0:wreck_car", "WRECK"),      // hidden
                S("warp:0", "SPECIAL"),           // procedural AND admin-only
                S("msh:kraken", "SPECIAL"),       // admin-only category
                S("cc0:rock_c", "ROCK"),          // keep
            };
            Dictionary<string, List<PaletteItem>> byKind = Palette.Build(src, Base);

            Assert.AreEqual(1, byKind.Count, "only ROCK survives");
            Assert.AreEqual(1, byKind[Palette.Rock].Count);
            Assert.AreEqual("cc0:rock_c", byKind[Palette.Rock][0].Id);
            Assert.IsFalse(byKind.ContainsKey(Palette.Special), "the warp category is admin-only");
        }

        [Test]
        public void Build_ToleratesNullAndEmptyInput()
        {
            Assert.AreEqual(0, Palette.Build(null, Base).Count);
            Assert.AreEqual(0, Palette.Build(new PaletteSource[] { null }, Base).Count);
            Assert.AreEqual(0, Palette.Build(new[] { S("", "ROCK") }, Base).Count);
        }

        // ── pricing (builder.html:2688 — cheapest first) ─────────────────────────

        [Test]
        public void Build_MarksAnimalsBuyableAndSceneryFree()
        {
            Dictionary<string, List<PaletteItem>> byKind =
                Palette.Build(new[] { S("cc0:rock_c", "ROCK"), S("losin:shrimp_acrobat", "MARINE_LIFE") }, Base);

            PaletteItem rock = byKind[Palette.Rock][0];
            Assert.IsFalse(rock.Buyable, "rocks are free scenery");
            Assert.AreEqual(0, rock.Price);

            PaletteItem animal = byKind[Palette.MarineLife][0];
            Assert.IsTrue(animal.Buyable);
            Assert.AreEqual(Shop.PriceOf("losin:shrimp_acrobat"), animal.Price);
            Assert.Greater(animal.Price, 0);
        }

        [Test]
        public void Build_SortsAnimalsAndSchoolsCheapestFirst()
        {
            var src = new List<PaletteSource>();
            foreach (string id in Shop.Catalogue) src.Add(S(id, "MARINE_LIFE"));

            List<PaletteItem> list = Palette.Build(src, Base)[Palette.MarineLife];
            Assert.Greater(list.Count, 3);
            for (int i = 1; i < list.Count; i++)
                Assert.LessOrEqual(list[i - 1].Price, list[i].Price,
                                   $"{list[i - 1].Id} ({list[i - 1].Price}) came before {list[i].Id} ({list[i].Price})");
        }

        [Test]
        public void Build_DoesNotReorderFreeCategories()
        {
            var src = new[] { S("cc0:rock_z", "ROCK"), S("cc0:rock_a", "ROCK") };
            List<PaletteItem> list = Palette.Build(src, Base)[Palette.Rock];
            Assert.AreEqual("cc0:rock_z", list[0].Id, "free scenery keeps registry order, like the web");
        }

        // ── thumbnails (builder.html:2694) ───────────────────────────────────────

        [Test]
        public void ThumbUrl_IsTheServersPreRenderedPng()
        {
            Assert.AreEqual("https://maps.siamdive.com/models/thumbs/cc0_rock_c.png",
                            Palette.ThumbUrl("cc0:rock_c", true, Base));
            Assert.AreEqual("https://maps.siamdive.com/models/thumbs/sw_boonsung.png",
                            Palette.ThumbUrl("sw:boonsung", true, "https://maps.siamdive.com/"));
        }

        [Test]
        public void ThumbUrl_IsNullWithoutAModel()
        {
            Assert.IsNull(Palette.ThumbUrl("tool:rope", false, Base), "no GLB was ever rendered");
            Assert.IsNull(Palette.ThumbUrl(null, true, Base));
        }

        // ── chips ────────────────────────────────────────────────────────────────

        [Test]
        public void ChipKinds_FollowTheWebsOrderAndSkipEmptyCategories()
        {
            Dictionary<string, List<PaletteItem>> byKind = Palette.Build(new[]
            {
                S("art:1268", "ARTIFICIAL"),
                S("cc0:rock_c", "ROCK"),
                S("school:scad", "SCHOOL"),
            }, Base);

            List<string> chips = Palette.ChipKinds(byKind);
            CollectionAssert.AreEqual(new[] { Palette.Rock, Palette.School, Palette.Artificial }, chips,
                                      "chips follow KIND_ORDER, not insertion order");
        }

        [Test]
        public void ChipKinds_EmptyInput()
        {
            Assert.AreEqual(0, Palette.ChipKinds(null).Count);
            Assert.AreEqual(0, Palette.ChipKinds(new Dictionary<string, List<PaletteItem>>()).Count);
        }

        [Test]
        public void EveryChipHasALabelAndAnIcon()
        {
            foreach (string kind in Palette.KindOrder)
            {
                Assert.IsNotEmpty(Palette.LabelOf(kind), kind + " has no label");
                Assert.IsNotEmpty(Palette.IconOf(kind), kind + " has no icon");
            }
            foreach (string tool in new[] { Palette.PinTool, Palette.SettingsTool, Palette.SculptTool })
            {
                Assert.IsNotEmpty(Palette.ToolLabel(tool), tool + " has no label");
                Assert.IsNotEmpty(Palette.ToolIcon(tool), tool + " has no icon");
            }
        }

        [Test]
        public void EveryChipLabel_IsTranslated()
        {
            // A chip whose Thai label is missing from the table renders Thai in an English UI.
            foreach (string kind in Palette.KindOrder)
                Assert.AreNotEqual(Palette.LabelOf(kind),
                                   UiStrings.Tr(Palette.LabelOf(kind), UiStrings.English),
                                   $"chip '{kind}' has no English translation");
            foreach (string tool in new[] { Palette.PinTool, Palette.SettingsTool, Palette.SculptTool })
                Assert.AreNotEqual(Palette.ToolLabel(tool),
                                   UiStrings.Tr(Palette.ToolLabel(tool), UiStrings.English),
                                   $"tool chip '{tool}' has no English translation");
        }
    }
}
