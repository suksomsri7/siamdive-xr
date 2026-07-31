using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for the map hub's 2-column grid.
    ///
    /// The numbers asserted here are the RN hub's own (siamdive-rn <c>src/app/map.tsx</c>):
    /// a 12 px gap in both directions, 16 px list padding, and a banner block of
    /// 16 + 16 + 108 + 12 = 152 px above the first row. The maths is trivial; what these tests
    /// actually protect is the OFF-BY-ONE class of bug — a row index computed per card instead
    /// of per row puts every card in column 0, and a headless screenshot is a slow way to find
    /// that out.
    /// </summary>
    public class MapGridLayoutTests
    {
        private const int Cols = MapGridLayout.Columns;
        private const float Gap = MapGridLayout.Gap;

        [Test]
        public void CardWidth_SplitsTheListMinusOneGap()
        {
            // A 375 px phone: 375 − 16×2 = 343 list, (343 − 12) / 2 = 165.5 per card.
            Assert.AreEqual(165.5f, MapGridLayout.CardWidth(343f, Cols, Gap), 0.001f);
        }

        [Test]
        public void CardWidth_NeverGoesNegative()
        {
            Assert.AreEqual(0f, MapGridLayout.CardWidth(8f, Cols, Gap), 0.001f);
            Assert.AreEqual(0f, MapGridLayout.CardWidth(0f, Cols, Gap), 0.001f);
        }

        [Test]
        public void CardWidth_SingleColumnUsesTheWholeWidth()
        {
            Assert.AreEqual(343f, MapGridLayout.CardWidth(343f, 1, Gap), 0.001f);
            Assert.AreEqual(343f, MapGridLayout.CardWidth(343f, 0, Gap), 0.001f, "columns < 1 is clamped");
        }

        [Test]
        public void CardX_AlternatesBetweenTheTwoColumns()
        {
            Assert.AreEqual(0f, MapGridLayout.CardX(0, 165.5f, Cols, Gap), 0.001f);
            Assert.AreEqual(177.5f, MapGridLayout.CardX(1, 165.5f, Cols, Gap), 0.001f);
            Assert.AreEqual(0f, MapGridLayout.CardX(2, 165.5f, Cols, Gap), 0.001f);
            Assert.AreEqual(177.5f, MapGridLayout.CardX(3, 165.5f, Cols, Gap), 0.001f);
        }

        [Test]
        public void RowOf_PairsCards()
        {
            Assert.AreEqual(0, MapGridLayout.RowOf(0, Cols));
            Assert.AreEqual(0, MapGridLayout.RowOf(1, Cols));
            Assert.AreEqual(1, MapGridLayout.RowOf(2, Cols));
            Assert.AreEqual(2, MapGridLayout.RowOf(5, Cols));
        }

        [Test]
        public void RowCount_RoundsUpForAnOddTail()
        {
            Assert.AreEqual(0, MapGridLayout.RowCount(0, Cols));
            Assert.AreEqual(1, MapGridLayout.RowCount(1, Cols));
            Assert.AreEqual(1, MapGridLayout.RowCount(2, Cols));
            Assert.AreEqual(2, MapGridLayout.RowCount(3, Cols));
            Assert.AreEqual(3, MapGridLayout.RowCount(6, Cols));
        }

        [Test]
        public void HeaderBlock_IsTheBannerPlusItsMargins()
        {
            float withBanner = MapGridLayout.HeaderBlock(
                true, MapGridLayout.ListPadTop, MapGridLayout.BannerMarginTop,
                MapGridLayout.BannerHeight, Gap);
            Assert.AreEqual(16f + 16f + 108f + 12f, withBanner, 0.001f);
        }

        [Test]
        public void HeaderBlock_IsJustTheListPaddingWhileSearching()
        {
            float searching = MapGridLayout.HeaderBlock(
                false, MapGridLayout.ListPadTop, MapGridLayout.BannerMarginTop,
                MapGridLayout.BannerHeight, Gap);
            Assert.AreEqual(16f, searching, 0.001f, "the web hides the banner while searching");
        }

        [Test]
        public void BannerHeight_IsPaddingPlusTheCoin()
        {
            Assert.AreEqual(108f, MapGridLayout.BannerHeight, 0.001f);
        }

        [Test]
        public void CardY_AdvancesOncePerROW_NotPerCard()
        {
            const float h = 200f, header = 152f;
            Assert.AreEqual(header, MapGridLayout.CardY(0, h, header, Cols, Gap), 0.001f);
            Assert.AreEqual(header, MapGridLayout.CardY(1, h, header, Cols, Gap), 0.001f);
            Assert.AreEqual(header + 212f, MapGridLayout.CardY(2, h, header, Cols, Gap), 0.001f);
            Assert.AreEqual(header + 212f, MapGridLayout.CardY(3, h, header, Cols, Gap), 0.001f);
            Assert.AreEqual(header + 424f, MapGridLayout.CardY(4, h, header, Cols, Gap), 0.001f);
        }

        [Test]
        public void ContentHeight_CoversEveryRowPlusTheBottomPadding()
        {
            const float h = 200f, header = 152f, bottom = 16f;
            // 6 cards = 3 rows: 152 + 3×200 + 2×12 + 16
            Assert.AreEqual(152f + 600f + 24f + 16f,
                            MapGridLayout.ContentHeight(6, h, header, Cols, Gap, bottom), 0.001f);
            // an odd tail still occupies a whole row
            Assert.AreEqual(152f + 600f + 24f + 16f,
                            MapGridLayout.ContentHeight(5, h, header, Cols, Gap, bottom), 0.001f);
        }

        [Test]
        public void ContentHeight_EmptyListIsJustTheHeader()
        {
            Assert.AreEqual(152f, MapGridLayout.ContentHeight(0, 200f, 152f, Cols, Gap, 16f), 0.001f);
        }

        [Test]
        public void LastCardFitsInsideTheContent()
        {
            // The bug this guards: content shorter than the last row means the bottom card is
            // unreachable no matter how far you scroll.
            const float h = 200f, header = 152f, bottom = 16f;
            for (int count = 1; count <= 9; count++)
            {
                float content = MapGridLayout.ContentHeight(count, h, header, Cols, Gap, bottom);
                float lastBottom = MapGridLayout.CardY(count - 1, h, header, Cols, Gap) + h;
                Assert.GreaterOrEqual(content, lastBottom, $"card {count - 1} of {count} hangs out of the content");
            }
        }
    }
}
