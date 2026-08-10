using DiveMap.Core;
using NUnit.Framework;

namespace DiveMap.Tests
{
    /// <summary>
    /// The memory-warning throttle (WO-MERGE P1c). Small, but the two ways to get it wrong are
    /// both bad and neither shows up anywhere but a phone: swallow the FIRST warning and the one
    /// pass that mattered never runs; answer every warning in a burst and a full GC plus an asset
    /// unload runs four times on a device that was already struggling.
    /// </summary>
    public class MemoryReliefTests
    {
        [Test]
        public void TheFirstWarningIsAlwaysAnswered()
        {
            // Negative = "never run". Must be true even at t=0, which is where a zero-initialised
            // field plus a clock that also starts at zero would have said "too soon".
            Assert.IsTrue(MemoryRelief.ShouldRelieve(-1f, 0f));
            Assert.IsTrue(MemoryRelief.ShouldRelieve(-1f, 0.001f));
            Assert.IsTrue(MemoryRelief.ShouldRelieve(-1f, 900f));
        }

        [Test]
        public void ABurstIsAnsweredOnce()
        {
            // iOS delivers several warnings within a second while it walks its list of apps.
            Assert.IsTrue(MemoryRelief.ShouldRelieve(-1f, 10f));
            Assert.IsFalse(MemoryRelief.ShouldRelieve(10f, 10.05f));
            Assert.IsFalse(MemoryRelief.ShouldRelieve(10f, 10.4f));
            Assert.IsFalse(MemoryRelief.ShouldRelieve(10f, 14.9f));
        }

        [Test]
        public void AWorseningSituationGetsAnotherPass()
        {
            // The quiet period must expire: pressure that keeps building deserves a second pass
            // while the app is still alive to give one.
            Assert.IsTrue(MemoryRelief.ShouldRelieve(10f, 10f + MemoryRelief.MinGapSeconds));
            Assert.IsTrue(MemoryRelief.ShouldRelieve(10f, 30f));
        }

        [Test]
        public void TheQuietPeriodIsSecondsNotMinutes()
        {
            // Pinned as a range rather than a value: the point is that it swallows a burst and
            // still lets a real second event through, not that it is exactly five.
            Assert.GreaterOrEqual(MemoryRelief.MinGapSeconds, 1f);
            Assert.LessOrEqual(MemoryRelief.MinGapSeconds, 30f);
        }

        [Test]
        public void AnExplicitGapOverridesTheDefault()
        {
            Assert.IsFalse(MemoryRelief.ShouldRelieve(10f, 11f, 2f));
            Assert.IsTrue(MemoryRelief.ShouldRelieve(10f, 12f, 2f));
        }
    }
}
