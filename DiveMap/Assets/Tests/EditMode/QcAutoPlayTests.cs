using NUnit.Framework;
using DiveMap.Core;

namespace DiveMap.Tests
{
    /// <summary>
    /// Which runs the tour is not allowed to hijack. Both entries on this list are there because
    /// the auto-tour arrived in the middle of a measurement and the instrument reported nonsense
    /// with no sign that anything had happened to it (run 31513452365, two instruments at once).
    /// </summary>
    public class QcAutoPlayTests
    {
        private static string[] Cmd(params string[] args) => args;

        [Test]
        public void TheUiCaptureRun_KeepsTheTourOut()
        {
            // The run that photographed the drone HUD and filed it as "three axis arrows".
            Assert.AreEqual("-qcui",
                QcAutoPlay.SuppressedBy(Cmd("DiveMap", "-qcui", "/tmp/qc_ui", "-screen-width", "1280")));
        }

        [Test]
        public void ThePositiveControlRun_KeepsTheTourOut()
        {
            // The light meter: 0.450 → 0.324 = ×0.72, which is the tour's own ambient factor
            // arriving between the control's two baseline samples.
            Assert.IsTrue(QcAutoPlay.Suppresses(Cmd("DiveMap", "-qcblank", "/tmp/qc_blank")));
        }

        [Test]
        public void TheModelAndMapShotRun_KeepsTheTourOut()
        {
            // Already fixed at the source in 6e2c0de; pinned here so it cannot be dropped when
            // somebody edits the list.
            Assert.IsTrue(QcAutoPlay.Suppresses(Cmd("DiveMap", "-qcshot", "/tmp/qc.png")));
        }

        [Test]
        public void TheFishClipRun_IsLeftEXACTLYAsItWas()
        {
            // 🔴 Deliberate. The school clips are the reference the user judged the fish tuning
            // against frame by frame; a clip filmed under different conditions is not comparable
            // with the ones already signed off, and this fix is not about the fish. If a later
            // round decides -schoolclip should be on the list, it has to change this test on
            // purpose rather than by adding a string somewhere.
            Assert.IsNull(QcAutoPlay.SuppressedBy(Cmd("DiveMap", "-schoolclip", "/tmp/clip",
                                                      "-clipmap", "874ti6ignvp9")));
        }

        [Test]
        public void AnOrdinaryPlayerRun_IsUntouched()
        {
            // The whole point: the player's own auto-play is a FEATURE (builder.html:3544), and
            // suppressing it on a device would be a bigger bug than the one being fixed.
            Assert.IsNull(QcAutoPlay.SuppressedBy(Cmd("DiveMap")));
            Assert.IsNull(QcAutoPlay.SuppressedBy(null));
            Assert.IsFalse(QcAutoPlay.Suppresses(Cmd("DiveMap", "-screen-width", "1280",
                                                     "-force-glcore")));
        }

        [Test]
        public void ASwitchThatMerelyCONTAINSTheName_DoesNotCount()
        {
            // Ordinal equality, not a substring search: an argument VALUE that happens to mention
            // a harness (a path like /tmp/-qcui-old) must not silently disable a player's tour.
            Assert.IsNull(QcAutoPlay.SuppressedBy(Cmd("DiveMap", "-mapdir", "/tmp/-qcui-old")));
            Assert.IsNull(QcAutoPlay.SuppressedBy(Cmd("DiveMap", "-qcuix", "/tmp/x")));
        }
    }
}
