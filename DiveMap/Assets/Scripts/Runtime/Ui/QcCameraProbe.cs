using System.Text;
using UnityEngine;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Is anything except the main camera about to paint the screen?
    ///
    /// 🔴 WHY THIS EXISTS. "จอมืด" — a flat navy screen with a perfectly live HUD over it — cost
    /// this project six device rounds and two whole hypotheses (a stale atmosphere, a backdrop
    /// quad covering the world). Both were wrong. The answer came from measuring the user's
    /// screenshot: every pixel was (13,31,51), with no gradient anywhere, and that colour appears
    /// exactly once in the codebase — <c>SpeciesPreview</c>'s little preview camera. The card had
    /// destroyed the render texture that camera drew into and left the camera enabled on an
    /// undying stage, so it took over the screen and cleared it to its own background.
    ///
    /// The lesson is not about that one camera. It is that <b>a camera with no target texture is a
    /// full-screen eraser</b>, and nothing in the app was watching for one. This probe is the
    /// watcher: cheap, honest about what it can see, and greppable from CI.
    ///
    /// It reports rather than fixes: the fix belongs where the camera is owned, and a probe that
    /// silently "repairs" the scene would hide the next occurrence instead of reporting it.
    /// </summary>
    public static class QcCameraProbe
    {
        /// <summary>The token CI greps for. Spelled out in words for the same reason as
        /// <c>INVALID SHOT</c>: it has to survive being read in a 90 000-line log.</summary>
        public const string Offender = "SCREEN-CLEARING CAMERA";

        /// <summary>
        /// Log every enabled camera, and name any that would clear the screen without being the
        /// main one. <paramref name="tag"/> says WHEN the look was taken — "after-card-closed" and
        /// "after a map switch" are different questions with the same answer format.
        /// </summary>
        public static bool Report(string tag)
        {
            Camera main = Camera.main;
            var sb = new StringBuilder();
            int enabled = 0, offenders = 0;

            foreach (Camera c in Camera.allCameras)   // allCameras is already enabled-only
            {
                enabled++;
                bool toScreen = c.targetTexture == null;
                bool isMain = c == main;
                sb.Append(" · ").Append(c.name)
                  .Append(toScreen ? "→SCREEN" : "→rt")
                  .Append(" depth=").Append(c.depth.ToString("0.#"))
                  .Append(' ').Append(c.clearFlags);
                if (toScreen && !isMain)
                {
                    offenders++;
                    sb.Append(" [").Append(Offender).Append(" bg=")
                      .Append(Mathf.RoundToInt(c.backgroundColor.r * 255f)).Append(',')
                      .Append(Mathf.RoundToInt(c.backgroundColor.g * 255f)).Append(',')
                      .Append(Mathf.RoundToInt(c.backgroundColor.b * 255f)).Append(']');
                }
            }

            string line = $"[QcCam] {tag} enabled={enabled} offenders={offenders} " +
                          $"main={(main != null ? main.name : "NONE")}{sb}";
            if (offenders > 0) Debug.LogError(line); else Debug.Log(line);
            return offenders == 0;
        }
    }
}
