using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Shared font provider. The Linux CI player has NO Thai system font, so uGUI Text
    /// and TextMesh drop every Thai glyph (the "Htms Chang · 12 · 2" QC regression where
    /// the Thai title vanished). We bundle a static Noto Sans Thai (Latin + Thai in one
    /// face) under Resources/ and hand it to every text element built in code.
    ///
    /// Falls back to Unity's builtin LegacyRuntime.ttf if the resource is ever missing
    /// (editor / stripped build) so text never disappears entirely.
    /// </summary>
    public static class UiFont
    {
        private static Font _cached;

        public static Font Get()
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<Font>("NotoSansThai-Regular");
            if (_cached == null)
            {
                Debug.LogWarning("[UiFont] NotoSansThai-Regular not found in Resources — Thai text will be missing.");
                _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _cached;
        }
    }
}
