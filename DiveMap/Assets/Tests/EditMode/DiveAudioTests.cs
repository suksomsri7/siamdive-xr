using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// P1.2b — the audio rules. Cooldowns and falloff are exactly the kind of thing that "sounds
    /// fine" in a 3-second QC clip and then machine-guns a whale call at a user who parks next to
    /// one, so they are pinned here.
    /// </summary>
    public class DiveAudioTests
    {
        [Test]
        public void ClipsComeFromTheMapsCdn_NotFromTheApk()
        {
            Assert.IsTrue(DiveAudio.Url(DiveAudio.Ambience).StartsWith("https://maps.siamdive.com/audio/"));
            Assert.AreEqual("https://maps.siamdive.com/audio/sfx_coin.mp3",
                            DiveAudio.Url(DiveAudio.SfxClip("coin")));
        }

        [Test]
        public void VolumesMatchTheWebsTable()
        {
            Assert.AreEqual(0.5f, DiveAudio.AmbienceVolume, 0.001f);
            Assert.AreEqual(0.85f, DiveAudio.CueVolume, 0.001f);
            Assert.AreEqual(0.7f, DiveAudio.SfxVolume("coin"), 0.001f);
            Assert.AreEqual(0.9f, DiveAudio.SfxVolume("humpback"), 0.001f);
            Assert.AreEqual(0.85f, DiveAudio.SfxVolume("dolphin"), 0.001f);
            Assert.AreEqual(0.9f, DiveAudio.SfxVolume("sperm"), 0.001f);
            Assert.AreEqual(0.4f, DiveAudio.SfxVolume("click"), 0.001f);
            Assert.AreEqual(1f, DiveAudio.SfxVolume("something-new"), 0.001f, "unknown = full volume");
        }

        [Test]
        public void AnimalTable_IsTheWebs()
        {
            Assert.AreEqual(3, DiveAudio.Animals.Length);
            Assert.AreEqual(140f, DiveAudio.Animals[0].Radius, 0.01f);
            Assert.AreEqual(16f, DiveAudio.Animals[0].Cooldown, 0.01f);
            Assert.AreEqual(95f, DiveAudio.Animals[2].Radius, 0.01f);
            Assert.AreEqual(11f, DiveAudio.Animals[2].Cooldown, 0.01f);
        }

        [Test]
        public void TryMatch_FindsTheAnimalInAnAssetId()
        {
            Assert.IsTrue(DiveAudio.TryMatch("msh:humpback_whale", out DiveAudio.AnimalCall a));
            Assert.AreEqual("humpback", a.Sfx);
            Assert.IsTrue(DiveAudio.TryMatch("MSH:Dolphin_Real", out DiveAudio.AnimalCall b));
            Assert.AreEqual("dolphin", b.Sfx, "asset ids are matched case-insensitively");
            Assert.IsFalse(DiveAudio.TryMatch("msh:whaleshark", out _), "the whale shark is silent");
            Assert.IsFalse(DiveAudio.TryMatch(null, out _));
            Assert.IsFalse(DiveAudio.TryMatch("", out _));
        }

        [Test]
        public void ProximityVolume_FallsOffWithDistance_ButNeverToSilence()
        {
            const float r = 140f;
            float near = DiveAudio.ProximityVolume("humpback", 5f, r);
            float mid = DiveAudio.ProximityVolume("humpback", 70f, r);
            float edge = DiveAudio.ProximityVolume("humpback", r, r);

            Assert.Greater(near, mid);
            Assert.Greater(mid, edge);
            Assert.AreEqual(0.9f * 0.12f, edge, 0.001f, "the web floors the falloff at 0.12");
            Assert.LessOrEqual(near, 0.9f, "never louder than the clip's own volume");
        }

        [Test]
        public void ShouldPlay_RespectsRadiusAndCooldown()
        {
            DiveAudio.AnimalCall call = DiveAudio.Animals[0];   // humpback: 140 u, 16 s

            Assert.IsTrue(DiveAudio.ShouldPlay(call, 100f, 100f, -999f), "first time in range");
            Assert.IsFalse(DiveAudio.ShouldPlay(call, 200f, 100f, -999f), "out of earshot");
            Assert.IsFalse(DiveAudio.ShouldPlay(call, 100f, 100f, 95f), "still cooling down (5 s of 16)");
            Assert.IsTrue(DiveAudio.ShouldPlay(call, 100f, 120f, 100f), "20 s later it may call again");
        }

        [Test]
        public void ShouldPlay_IsPerAnimal_SoTwoWhalesDoNotSilenceEachOther()
        {
            DiveAudio.AnimalCall call = DiveAudio.Animals[0];
            // Whale A just called (lastPlayed = now); whale B has never called.
            Assert.IsFalse(DiveAudio.ShouldPlay(call, 50f, 10f, 10f));
            Assert.IsTrue(DiveAudio.ShouldPlay(call, 50f, 10f, -999f));
        }
    }
}
