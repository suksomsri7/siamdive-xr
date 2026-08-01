namespace DiveMap.Core
{
    /// <summary>
    /// Which build is this? Printed on the status line of every map so a screenshot answers the
    /// question by itself.
    ///
    /// 🔎 Written after a round where the user reported a fixed bug as still broken and neither of
    /// us could tell whether the phone was running the build that contained the fix. Two people
    /// then argued from screenshots that could not be dated. The number costs eight characters on
    /// one line and ends that argument permanently.
    ///
    /// The value is stamped into Resources at build time by CIBuild; a local Editor run has no
    /// file and simply shows nothing rather than a lie like "dev".
    /// </summary>
    public static class BuildStamp
    {
        public const string ResourceName = "build_number";

        private static string _cached;

        /// <summary>
        /// The numbers behind the black frame around the picture. Two fixes have now been aimed at
        /// this from guesses and neither hit, so the app reports what it actually has: the drawable
        /// it was given, the safe area iOS declared, and the render-scale factor. If the drawable is
        /// smaller than the screen the frame is a resolution problem; if it matches and the safe
        /// area is inset, it is a layout problem. One screenshot decides it instead of a third guess.
        /// Temporary — remove once the cause is known.
        /// </summary>
        public static string ScreenInfo
        {
            get
            {
                var sa = UnityEngine.Screen.safeArea;
                return $" · {UnityEngine.Screen.width}×{UnityEngine.Screen.height}" +
                       $" safe {sa.width:F0}×{sa.height:F0}@{sa.x:F0},{sa.y:F0}" +
                       $" dpi×{UnityEngine.QualitySettings.resolutionScalingFixedDPIFactor:F2}";
            }
        }

        /// <summary>e.g. " · b163", or "" when the build was not stamped.</summary>
        public static string Suffix
        {
            get
            {
                if (_cached != null) return _cached;
                var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>(ResourceName);
                string n = asset != null ? asset.text.Trim() : "";
                _cached = string.IsNullOrEmpty(n) ? "" : " · b" + n;
                return _cached;
            }
        }
    }
}
