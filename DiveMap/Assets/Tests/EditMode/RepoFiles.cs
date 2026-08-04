using System.IO;

namespace DiveMap.Tests
{
    /// <summary>
    /// Finds a file in the repository from a test, whichever of the two harnesses is running it.
    ///
    /// 🔴 Why a test would want to read a FILE at all: some of the most important decisions in this
    /// project are not in C# at all. The colour space is a line in
    /// <c>ProjectSettings/ProjectSettings.asset</c>, and the ACES curve exists twice — once in
    /// <c>Core/ToneMap.cs</c> and once in HLSL in <c>Shaders/DM_AcesToneMap.shader</c>. Neither can
    /// be asserted by calling a method, and both have exactly the failure mode this project keeps
    /// hitting: a value that silently reverts, or two copies of one formula that drift apart. A
    /// test that reads the file is the only kind that can see either.
    ///
    /// The two harnesses have different working directories — <c>tools/test.sh</c> runs from the
    /// repository root, Unity's EditMode runner from <c>DiveMap/</c> — so nothing is hard-coded:
    /// this walks up looking for the file and fails loudly, with the directory it started from, if
    /// it cannot find it. A test that quietly skips itself is worth less than no test.
    /// </summary>
    internal static class RepoFiles
    {
        /// <summary>
        /// Absolute path of <paramref name="relative"/> (given from the Unity project folder, e.g.
        /// <c>ProjectSettings/ProjectSettings.asset</c>), or null if it is nowhere above the cwd.
        /// </summary>
        public static string Find(string relative)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
            {
                string direct = Path.Combine(dir.FullName, relative);
                if (File.Exists(direct)) return direct;
                string nested = Path.Combine(dir.FullName, "DiveMap", relative);
                if (File.Exists(nested)) return nested;
            }
            return null;
        }

        /// <summary>The whole file, or null when it could not be found.</summary>
        public static string Read(string relative)
        {
            string path = Find(relative);
            return path == null ? null : File.ReadAllText(path);
        }

        /// <summary>Where the search started, for a failure message that can be acted on.</summary>
        public static string SearchedFrom => Directory.GetCurrentDirectory();
    }
}
