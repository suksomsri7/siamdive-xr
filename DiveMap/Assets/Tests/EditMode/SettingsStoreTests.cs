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
        private string _savedSpeed;
        private bool _hadSpeed;

        [SetUp]
        public void SetUp()
        {
            _hadGfx = PlayerPrefs.HasKey(SettingsStore.GfxPrefKey);
            _savedGfx = PlayerPrefs.GetString(SettingsStore.GfxPrefKey, "");
            _hadSpeed = PlayerPrefs.HasKey(SettingsStore.SpeedPrefKey);
            _savedSpeed = PlayerPrefs.GetString(SettingsStore.SpeedPrefKey, "");
            SettingsStore.ResetCache();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadGfx) PlayerPrefs.SetString(SettingsStore.GfxPrefKey, _savedGfx);
            else PlayerPrefs.DeleteKey(SettingsStore.GfxPrefKey);
            if (_hadSpeed) PlayerPrefs.SetString(SettingsStore.SpeedPrefKey, _savedSpeed);
            else PlayerPrefs.DeleteKey(SettingsStore.SpeedPrefKey);
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

        // ── drone speed ──────────────────────────────────────────────────────────

        [Test]
        public void DroneSpeed_DefaultsToNormal_AndSurvivesGarbage()
        {
            PlayerPrefs.DeleteKey(SettingsStore.SpeedPrefKey);
            SettingsStore.ResetCache();
            Assert.AreEqual(SettingsStore.SpeedNormal, SettingsStore.DroneSpeed);
            // Scale 1 = DroneFlight.Speed untouched = the web's SP 30 (builder.html:3770). Someone
            // who never opens settings gets the speed the user tuned on the web and called
            // "ดีมากๆ" — see EverySpeedPreset_IsAMultipleOfTheWebsFlightModel.
            Assert.AreEqual(1f, SettingsStore.SpeedScale, 1e-6f, "the default must be the web's speed");

            PlayerPrefs.SetString(SettingsStore.SpeedPrefKey, "เร็วมาก");
            SettingsStore.ResetCache();
            Assert.AreEqual(SettingsStore.SpeedNormal, SettingsStore.DroneSpeed);
        }

        [Test]
        public void DroneSpeed_RoundTripsAndNormalises()
        {
            SettingsStore.DroneSpeed = " CALM ";
            Assert.AreEqual(SettingsStore.SpeedCalm, PlayerPrefs.GetString(SettingsStore.SpeedPrefKey, ""));
            SettingsStore.ResetCache();
            Assert.AreEqual(SettingsStore.SpeedCalm, SettingsStore.DroneSpeed);

            SettingsStore.DroneSpeed = SettingsStore.SpeedFast;
            Assert.AreEqual(SettingsStore.FastSpeedScale, SettingsStore.SpeedScale, 1e-6f);
        }

        /// <summary>
        /// The three presets, against the ONE speed they are multipliers of.
        ///
        /// 🔴 2026-08-04 — READ THIS BEFORE PINNING ABSOLUTE m/s HERE AGAIN.
        ///
        /// This test used to assert calm = 0.98, normal = 1.50, fast = 2.18 m/s, with the comment
        /// "…must not put the drone back where the complaint started (5 m/s)". Those numbers came
        /// from a round that read the web's speed as a metric implausibility and re-scaled the
        /// whole flight model down (SP 30 → 9). The user then flew that build — 261, on a real
        /// iPhone — and the verdict was the opposite one:
        ///
        ///     "โดรนเคลื่อนที่ช้าไป … เรื่องเหล่านี้เราปรับที่เว็บจนดีมากๆ ควรไปศึกษาจากเว็บ"
        ///
        /// So the web IS the specification, 5 m/s and all, and <see cref="DroneFlight.Speed"/> is
        /// back to the web's <c>SP = 30</c> (builder.html:3770). The realism argument was not
        /// wrong, it was aimed at the wrong control: it now lives in the preset a user can choose,
        /// which is where a preference belongs, and NOT in the number everybody gets.
        ///
        /// Hence nothing below is a floating literal. Every assertion is a relationship to
        /// <see cref="DroneFlight.Speed"/> and the scale constants, so that a future change to the
        /// flight model moves this test with it instead of being blocked by it. The one absolute
        /// is the 1.5 m/s migration promise in the "ช้า" preset, and it is anchored to the 9 u/s
        /// it is promising to reproduce rather than typed in as a metres-per-second opinion.
        /// </summary>
        [Test]
        public void EverySpeedPreset_IsAMultipleOfTheWebsFlightModel()
        {
            float calmScale = SettingsStore.SpeedScaleOf(SettingsStore.SpeedCalm);
            float normalScale = SettingsStore.SpeedScaleOf(SettingsStore.SpeedNormal);
            float fastScale = SettingsStore.SpeedScaleOf(SettingsStore.SpeedFast);

            // 🔴 "ปกติ" is not a tuning of its own: it is the web, untouched. If this ever stops
            // being exactly 1, the default has quietly become somebody's opinion again.
            Assert.AreEqual(1f, normalScale, 1e-6f, "the default must be the web's own speed");

            float calm = DroneFlight.MetresPerSecond(DroneFlight.Speed * calmScale);
            float normal = DroneFlight.MetresPerSecond(DroneFlight.Speed * normalScale);
            float fast = DroneFlight.MetresPerSecond(DroneFlight.Speed * fastScale);

            // Each preset is its scale × the flight model, with no arithmetic of its own.
            Assert.AreEqual(DroneFlight.MetresPerSecond(DroneFlight.Speed), normal, 1e-6f);
            Assert.AreEqual(normal * calmScale, calm, 1e-4f);
            Assert.AreEqual(normal * fastScale, fast, 1e-4f);

            // Ordered, and distinct enough that a user can feel which one they picked.
            Assert.Less(calm, normal);
            Assert.Less(normal, fast);
            Assert.Less(calmScale, 0.75f, "if 'ช้า' is not clearly slower, the setting does nothing");
            Assert.Greater(fastScale, 1.05f, "…and the same for 'เร็ว'");

            // 🔴 16 ส.ค. 2026 — คำสัญญาเดิม ("ช้า" = โดรนของ build 261 คือ 9 u/s เป๊ะ) ถูกปลดแล้ว
            // โดยเจตนา: user ขอลดความเร็วฐานสี่รอบจนเหลือ 12 u/s ซึ่งห่างจาก 9 เพียง 25% ⇒ ถ้ายัง
            // ตรึงไว้ พรีเซ็ต "ช้า" จะแทบไม่ต่างจาก "ปกติ" และการตั้งค่าก็ไร้ความหมาย
            // ที่ยังต้องจริงคือความสัมพันธ์ ไม่ใช่ตัวเลขในอดีต — ตรึงไว้ข้างบนแล้ว (ordered + ชัดเจน)
            Assert.Less(calm, normal * 0.8f, "'ช้า' ต้องช้ากว่า 'ปกติ' อย่างรู้สึกได้");

            // Nothing may be so slow that crossing a site is a chore — the failure mode at the
            // other end, and the one that produced the 2026-08-04 report.
            Assert.Greater(calm, 0.5f);
        }

        [Test]
        public void SpeedScaleOf_NeverReturnsZero()
        {
            Assert.Greater(SettingsStore.SpeedScaleOf(null), 0f);
            Assert.Greater(SettingsStore.SpeedScaleOf(""), 0f);
            Assert.Greater(SettingsStore.SpeedScaleOf("turbo"), 0f);
        }

        // ── language passthrough ─────────────────────────────────────────────────

        [Test]
        public void Lang_MirrorsTheI18nTable()
        {
            Assert.AreEqual(UiStrings.Lang, SettingsStore.Lang);
        }
    }
}
