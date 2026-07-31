// Make the app's sound survive the silent switch.
//
// iOS gives every app the "SoloAmbient" audio session unless it says otherwise, and that category
// is silenced by the ring/silent switch on the side of the phone. Unity does not change it. So on
// a phone that lives on silent — which is most phones — the dive ambience, the whale calls and the
// coin sound all play correctly, at the right volume, into nothing. Nothing in any log says so:
// AudioSource.isPlaying is true, the clip position advances, the mute button in the app is off.
//
// "Playback" is the category for audio that IS the experience rather than a decoration, and it
// keeps playing with the switch on. That is the whole fix.
//
// Not mixWithOthers: the ambience is a continuous underwater bed, and ducking someone's music
// under it for ten minutes is worse than stopping it — iOS asks the user's music to pause, which
// is what a game is expected to do.

#import <AVFoundation/AVFoundation.h>

extern "C" void DiveMapEnablePlaybackAudio(void)
{
    @autoreleasepool {
        AVAudioSession *session = [AVAudioSession sharedInstance];
        NSError *error = nil;

        if (![session setCategory:AVAudioSessionCategoryPlayback error:&error]) {
            NSLog(@"[DiveMap] audio session category failed: %@", error.localizedDescription);
            return;
        }
        if (![session setActive:YES error:&error]) {
            NSLog(@"[DiveMap] audio session activate failed: %@", error.localizedDescription);
            return;
        }
        NSLog(@"[DiveMap] audio session = Playback (plays with the silent switch on)");
    }
}
