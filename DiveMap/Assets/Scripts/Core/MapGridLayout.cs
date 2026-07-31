namespace DiveMap.Core
{
    /// <summary>
    /// Pure placement math for the map hub's 2-column card grid.
    ///
    /// Ported from the shipped React Native hub (siamdive-rn <c>src/app/map.tsx</c>), which is
    /// the screen the reference shot <c>docs/refs/web-maplist.png</c> came from:
    /// <code>
    ///   FlatList numColumns={2}
    ///     columnWrapperStyle={{ gap: 12 }}
    ///     contentContainerStyle={{ gap: 12, paddingTop: 16 }}
    ///   arena (banner) marginTop: 16, padding: 14, badge 80
    ///   wrap padding: 16
    /// </code>
    /// so the banner block is <c>16 (list pad) + 16 (margin) + 108 (banner) + 12 (row gap)</c>
    /// above the first card row, and cards are <c>(width − 12) / 2</c> wide.
    ///
    /// Every method takes its metrics as arguments rather than reading the constants: the view
    /// works in canvas units (CSS px × <c>UiKit.Css</c>), the constants below are CSS px, and
    /// mixing the two silently produces a layout that is right on exactly one screen size.
    /// Unit-tested in <c>MapGridLayoutTests</c>.
    /// </summary>
    public static class MapGridLayout
    {
        // ── the RN stylesheet, in CSS px ─────────────────────────────────────────
        public const int Columns = 2;
        public const float Gap = 12f;              // columnWrapperStyle + contentContainerStyle gap
        public const float SidePad = 16f;          // wrap padding
        public const float ListPadTop = 16f;       // contentContainerStyle paddingTop
        public const float BannerMarginTop = 16f;  // arena marginTop
        public const float BannerPad = 14f;        // arena padding
        public const float BannerBadge = 80f;      // arenaBadge (the coin)
        /// <summary>arena height = padding + badge + padding (the badge is its tallest child).</summary>
        public const float BannerHeight = BannerPad * 2f + BannerBadge;

        /// <summary>Width of one card inside a <paramref name="contentWidth"/>-wide list.</summary>
        public static float CardWidth(float contentWidth, int columns, float gap)
        {
            if (columns < 1) columns = 1;
            float w = (contentWidth - gap * (columns - 1)) / columns;
            return w > 0f ? w : 0f;
        }

        /// <summary>Left edge of the card at <paramref name="index"/>, relative to the list.</summary>
        public static float CardX(int index, float cardWidth, int columns, float gap)
        {
            if (columns < 1) columns = 1;
            if (index < 0) index = 0;
            return (index % columns) * (cardWidth + gap);
        }

        public static int RowOf(int index, int columns)
        {
            if (columns < 1) columns = 1;
            if (index < 0) index = 0;
            return index / columns;
        }

        public static int RowCount(int count, int columns)
        {
            if (columns < 1) columns = 1;
            if (count <= 0) return 0;
            return (count + columns - 1) / columns;
        }

        /// <summary>
        /// Distance from the top of the scroll content to the first card row — the list's own
        /// top padding, plus the banner block when the banner is showing. The web hides the
        /// banner while searching (<c>!searching &amp;&amp; online</c>), and so do we.
        /// </summary>
        public static float HeaderBlock(bool showBanner, float listPadTop, float bannerMarginTop,
                                        float bannerHeight, float gap)
        {
            float h = listPadTop;
            if (showBanner) h += bannerMarginTop + bannerHeight + gap;
            return h;
        }

        /// <summary>Top edge of the card at <paramref name="index"/> (positive = downward).</summary>
        public static float CardY(int index, float cardHeight, float headerBlock, int columns, float gap)
        {
            return headerBlock + RowOf(index, columns) * (cardHeight + gap);
        }

        /// <summary>Total scrollable height for <paramref name="count"/> cards.</summary>
        public static float ContentHeight(int count, float cardHeight, float headerBlock,
                                          int columns, float gap, float bottomPad)
        {
            int rows = RowCount(count, columns);
            if (rows == 0) return headerBlock;
            return headerBlock + rows * cardHeight + (rows - 1) * gap + bottomPad;
        }
    }
}
