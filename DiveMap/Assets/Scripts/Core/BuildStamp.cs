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
