using DiveMap.Runtime.Ui;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// Regression guard for the WO-XR-05.2 "map name is invisible" bug.
    ///
    /// Legacy <c>UnityEngine.UI.Text</c> with <c>VerticalWrapMode.Truncate</c> does not
    /// clip a line that is taller than its RectTransform — it DROPS the line, so the row
    /// renders nothing at all. The bundled NotoSansThai-Regular is 1.511 × fontSize tall
    /// per line (TTF: unitsPerEm 1000, ascender 1061, descender -450, lineGap 0), which
    /// meant the 36 px card name did not fit its hard-coded 52 px row while the 26 px
    /// meta line squeezed into 40 px and stayed visible.
    ///
    /// These tests are pure arithmetic — they lock the ratio and the "row must be at
    /// least one line tall" invariant so the layout constants cannot silently regress.
    /// </summary>
    public class UiTextMetricsTests
    {
        // Measured from Assets/Resources/NotoSansThai-Regular.ttf.
        private const float TtfRatio = (1061f + 450f) / 1000f;

        [Test]
        public void LineHeightRatio_MatchesTheBundledTtfMetrics()
        {
            Assert.AreEqual(TtfRatio, UiKit.LineHeightRatio, 0.001f);
        }

        [Test]
        public void LineHeightRatio_IsTallerThanALatinOnlyFace()
        {
            // Thai stacks tone marks above and vowels below; assuming ~1.2 like a Latin
            // face is exactly how the row heights ended up too short.
            Assert.Greater(UiKit.LineHeightRatio, 1.4f);
        }

        [Test]
        public void LineHeight_IsNeverLessThanTheRawLineSize()
        {
            for (int size = 20; size <= 48; size += 2)
                Assert.GreaterOrEqual(UiKit.LineHeight(size), size * UiKit.LineHeightRatio,
                                      "line height must not round DOWN at size " + size);
        }

        [Test]
        public void LineHeight_OfZeroOrNegativeIsZero()
        {
            Assert.AreEqual(0f, UiKit.LineHeight(0));
            Assert.AreEqual(0f, UiKit.LineHeight(-12));
        }

        [Test]
        public void RowHeight_AlwaysFitsAtLeastOneWholeLine()
        {
            for (int size = 20; size <= 48; size += 2)
                Assert.Greater(UiKit.RowHeight(size), UiKit.LineHeight(size),
                               "a row must have slack over one line at size " + size);
        }

        [Test]
        public void RowHeight_ScalesWithLineCount()
        {
            Assert.Greater(UiKit.RowHeight(32, 2), UiKit.LineHeight(32) * 2f);
            Assert.AreEqual(UiKit.RowHeight(32, 1), UiKit.RowHeight(32, 0)); // clamped to 1
        }

        [Test]
        public void RowHeight_ForTheCardNameExceedsTheBrokenHardCodedValue()
        {
            // The literal 52 that made every card name vanish.
            Assert.Greater(UiKit.RowHeight(36), 52f);
            Assert.Greater(UiKit.LineHeight(36), 52f); // …and one line alone did not fit
        }

        [Test]
        public void RowHeight_ForTheCardMetaLineWasOnlyJustSurviving()
        {
            // 26 px in a 40 px row cleared the bar by well under a pixel — the reason
            // only ONE of the two rows on the card was blank.
            Assert.Less(UiKit.LineHeight(26), 41f);
            Assert.Greater(UiKit.LineHeight(26), 39f);
        }
    }
}
