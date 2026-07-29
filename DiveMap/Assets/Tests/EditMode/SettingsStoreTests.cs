using DiveMap.Core;
using NUnit.Framework;
using UnityEngine;

namespace DiveMap.Tests
{
    /// <summary>
    /// Unit tests for SettingsStore (WO-XR-05.4).
    ///
    /// The store's whole job is that a corrupt / stale / hand-edited PlayerPrefs value can
    /// never reach the renderer: anything unknown must read back as the default preset.
    /// The tests restore whatever the machine had in PlayerPrefs so running them cannot
    /// change the editor's own settings.
    /// </summary>
    public class SettingsStoreTests
    {
        private string _savedGfx;
        private bool _hadGfx;

        [SetUp]
        public void SetUp()
        {
            _hadGfx = PlayerPrefs.HasKey(SettingsStore.GfxPrefKey);
            _savedGfx = PlayerPrefs.GetString(SettingsStore.GfxPrefKey, "");
            SettingsStore.ResetCache();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadGfx) PlayerPrefs.SetString(SettingsStore.GfxPrefKey, _savedGfx);
            else PlayerPrefs.DeleteKey(SettingsStore.GfxPrefKey);
            PlayerPrefs.Save();
            SettingsStore.ResetCache();
        }

        // ── validation ───────────────────────────────────────────────────────────

        [Test]
        public void NormalizeGfx_AcceptsBothPresetsCaseInsensitively()
        {
            Assert.AreEqual(SettingsStore.Lite, SettingsStore.NormalizeGfx("lite"));
            Assert.AreEqual(SettingsStore.Lite, SettingsStore.NormalizeGfx(" LITE "));
            Assert.AreEqual(SettingsStore.High, SettingsStore.NormalizeGfx("high"));
            Assert.AreEqual(SettingsStore.High, SettingsStore.NormalizeGfx("High"));
        }

        [Test]
        public void NormalizeGfx_UnknownValuesFallBackToHigh()
        {
            Assert.AreEqual(SettingsStore.High, SettingsStore.NormalizeGfx(null));
            Assert.AreEqual(SettingsStore.High, SettingsStore.NormalizeGfx(""));
            Assert.AreEqual(SettingsStore.High, SettingsStore.NormalizeGfx("ultra"));
            Assert.AreEqual(SettingsStore.High, SettingsStore.NormalizeGfx("{}"));
        }

        [Test]
        public void IsLite_OnlyForTheLitePreset()
        {
            Assert.IsTrue(SettingsStore.IsLite(SettingsStore.Lite));
            Assert.IsFalse(SettingsStore.IsLite(SettingsStore.High));
            Assert.IsFalse(SettingsStore.IsLite("nonsense"));
        }

        // ── persistence ──────────────────────────────────────────────────────────

        [Test]
        public void Gfx_DefaultsToHighWhenNothingIsStored()
        {
            PlayerPrefs.DeleteKey(SettingsStore.GfxPrefKey);
            SettingsStore.ResetCache();
            Assert.AreEqual(SettingsStore.High, SettingsStore.Gfx);
        }

        [Test]
        public void Gfx_RoundTripsThroughPlayerPrefs()
        {
            SettingsStore.Gfx = SettingsStore.Lite;
            Assert.AreEqual(SettingsStore.Lite, PlayerPrefs.GetString(SettingsStore.GfxPrefKey, ""));

            SettingsStore.ResetCache();
            Assert.AreEqual(SettingsStore.Lite, SettingsStore.Gfx);
        }

        [Test]
        public void Gfx_StoresTheNormalisedValueNotTheRawInput()
        {
            SettingsStore.Gfx = "LITE";
            Assert.AreEqual(SettingsStore.Lite, PlayerPrefs.GetString(SettingsStore.GfxPrefKey, ""));

            SettingsStore.Gfx = "who knows";
            Assert.AreEqual(SettingsStore.High, PlayerPrefs.GetString(SettingsStore.GfxPrefKey, ""));
        }

        [Test]
        public void Gfx_GarbageInPlayerPrefsReadsBackAsTheDefault()
        {
            PlayerPrefs.SetString(SettingsStore.GfxPrefKey, "สูง");
            SettingsStore.ResetCache();
            Assert.AreEqual(SettingsStore.High, SettingsStore.Gfx);
        }

        // ── render scale ─────────────────────────────────────────────────────────

        [Test]
        public void ScaledResolution_HighKeepsTheNativeSize()
        {
            SettingsStore.ScaledResolution(1080, 2400, SettingsStore.High, out int w, out int h);
            Assert.AreEqual(1080, w);
            Assert.AreEqual(2400, h);
        }

        [Test]
        public void ScaledResolution_LiteAppliesTheRenderScale()
        {
            SettingsStore.ScaledResolution(1080, 2400, SettingsStore.Lite, out int w, out int h);
            Assert.AreEqual(Mathf.RoundToInt(1080 * SettingsStore.LiteRenderScale), w);
            Assert.AreEqual(Mathf.RoundToInt(2400 * SettingsStore.LiteRenderScale), h);
            Assert.Less(w, 1080);
        }

        [Test]
        public void ScaledResolution_NeverReturnsADegenerateSize()
        {
            SettingsStore.ScaledResolution(1, 1, SettingsStore.Lite, out int w, out int h);
            Assert.GreaterOrEqual(w, 1);
            Assert.GreaterOrEqual(h, 1);

            SettingsStore.ScaledResolution(0, -5, SettingsStore.High, out int w2, out int h2);
            Assert.GreaterOrEqual(w2, 1);
            Assert.GreaterOrEqual(h2, 1);
        }

        // ── language passthrough ─────────────────────────────────────────────────

        [Test]
        public void Lang_MirrorsTheI18nTable()
        {
            Assert.AreEqual(UiStrings.Lang, SettingsStore.Lang);
        }
    }
}
