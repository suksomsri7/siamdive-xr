using System.Collections;
using System.IO;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime.Ui
{
    /// <summary>
    /// Makes a gizmo screenshot prove that it photographed the gizmo.
    ///
    /// 🔴 WHY. Run 31513452365 shipped <c>qc_ui_gizmo_axes.png</c> and <c>qc_ui_gizmo.png</c> as
    /// evidence for WO-O's three axis arrows. Both were pictures of the drone tour HUD — open
    /// water, joysticks, a compass, no selected object, not one arrow. The harness could not tell:
    /// <c>ScreenCapture.CaptureScreenshot</c> cannot fail, it writes whatever is on the screen, and
    /// a picture of the wrong screen is still a perfectly good PNG. The state line beside it said
    /// <c>visible=False … mode=Tour</c> and was logged as information rather than as a refusal.
    ///
    /// This is that refusal. Before a gizmo shot is believed:
    ///
    ///   1. the STATE has to be the state under test — View mode, a selection, handles shown;
    ///   2. the PIXELS have to contain the handles. The same camera is rendered twice inside ONE
    ///      frame, once with the handles drawn and once with them hidden, and the pixels that
    ///      differ are the handles and nothing else. Both renders in one frame is load-bearing:
    ///      fish swim and water scrolls between frames, and two renders a frame apart differ
    ///      everywhere, which would let any screen at all pass (QcShotProofTests pins that limit).
    ///
    /// A failure is <c>Debug.LogError</c> with the token <c>INVALID SHOT</c>, which CI greps for
    /// and turns the build red. It does NOT delete the PNG: the picture of the wrong screen is the
    /// most useful thing in the artifact when working out what the harness was looking at.
    ///
    /// The verdict itself lives in <c>Core.QcShotProof</c> so it is testable without a Unity
    /// Editor — including the case that matters, which is the instrument saying no.
    /// </summary>
    public static class QcGizmoWitness
    {
        /// <summary>
        /// Check the shot just taken. <paramref name="proofPngPath"/> gets the world-only frame the
        /// check was run on (no overlay UI — see <c>QcBlankShot.Capture</c> for why a camera target
        /// texture sees no ScreenSpaceOverlay canvas), so a human can look at exactly what the
        /// arithmetic looked at.
        /// </summary>
        public static IEnumerator Prove(string shot, string expectSelectedId, string proofPngPath)
        {
            // 🔴 Let the screenshot this is vouching for finish first. ScreenCapture.CaptureScreenshot
            // grabs the BACK BUFFER at the end of the frame it was asked in; pointing the camera at
            // a render texture inside that same frame would corrupt the very picture being proved.
            // Half a second is two orders of magnitude more than the one frame it needs, and this
            // pass is not on anybody's critical path.
            yield return new WaitForSecondsRealtime(0.5f);

            string state = $"mode={ModeManager.Current} selected={GizmoController.Selected} " +
                           $"expected={expectSelectedId} visible={GizmoHandles.Visible} " +
                           $"toolbar={SelectionToolbar.IsOpen}";

            GizmoHandles h = GizmoHandles.Current;
            Camera cam = Camera.main;

            // The three ways the shot is already lost before a single pixel is read. Each one is a
            // thing that actually happened in b386.
            string blocked = null;
            if (cam == null) blocked = "no-camera";
            else if (h == null || !GizmoHandles.Visible) blocked = "handles-hidden";
            else if (string.IsNullOrEmpty(GizmoController.Selected)) blocked = "nothing-selected";
            else if (!string.IsNullOrEmpty(expectSelectedId) &&
                     GizmoController.Selected != expectSelectedId) blocked = "wrong-object-selected";
            // The same rule the gizmo polices itself by (GizmoController:161): outside View/Edit it
            // drops the selection, so a shot taken there cannot contain what it claims.
            else if (!ModeRules.AllowsEditTools(ModeManager.Current)) blocked = "not-in-map-view";

            if (blocked != null)
            {
                Debug.LogError(QcShotProof.FailedLine(shot, blocked, state));
                yield break;
            }

            if (!h.QcScreenAxes(out Vector2 o, out Vector2 xt, out Vector2 yt, out Vector2 zt))
            {
                Debug.LogError(QcShotProof.FailedLine(shot, "no-projection", state));
                yield break;
            }

            int w = Mathf.Max(16, Screen.width);
            int ht = Mathf.Max(16, Screen.height);
            RenderTexture rt = new RenderTexture(w, ht, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(w, ht, TextureFormat.RGB24, false);
            byte[] on = null, off = null;

            // One frame, two renders. Nothing in the sim advances between them because nothing
            // yields between them.
            yield return new WaitForEndOfFrame();

            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                RenderTexture.active = rt;

                cam.Render();
                readback.ReadPixels(new Rect(0f, 0f, w, ht), 0, 0, false);
                readback.Apply(false);
                on = readback.GetRawTextureData();
                if (!string.IsNullOrEmpty(proofPngPath))
                {
                    try { File.WriteAllBytes(proofPngPath, readback.EncodeToPNG()); }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[QcShot] proof png write failed: " + e.Message);
                    }
                }

                h.QcSetShown(false);
                cam.Render();
                readback.ReadPixels(new Rect(0f, 0f, w, ht), 0, 0, false);
                readback.Apply(false);
                off = readback.GetRawTextureData();
            }
            finally
            {
                h.QcSetShown(true);
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                Object.Destroy(readback);
                rt.Release();
                Object.Destroy(rt);
            }

            QcShotProof.Axes a = QcShotProof.Arrows(
                on, off, w, ht,
                o.x, o.y, xt.x, xt.y, yt.x, yt.y, zt.x, zt.y);

            string line = QcShotProof.Line(shot, a, state);
            if (QcShotProof.Passes(a)) Debug.Log(line);
            else Debug.LogError(line);
        }
    }
}
