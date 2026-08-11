using System.Text;
using DiveMap.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Why is the world not on screen? (WO-MERGE DARK)
    ///
    /// 🔴 Build 9005 still shows it: close map 1, pick map 2, the 3D world is a flat dark navy
    /// IMMEDIATELY — while the HUD, the coin counter, the depth readout and a minimap dotted with
    /// map 2's items all work perfectly. Three rounds have now been spent on hypotheses about
    /// this, and the atmosphere fix — which is sound and stays — was evidently not the cause of
    /// what the user is looking at.
    ///
    /// So this file has no theory in it. It prints the state that every remaining explanation
    /// disagrees about, and the log picks the winner:
    ///
    ///   MAP       a root that is missing, or present-but-INACTIVE, or present with every
    ///             renderer disabled, are three different bugs with three different fixes —
    ///             and all three look identical on a screenshot. "Data loaded, HUD alive,
    ///             minimap dots, nothing rendered" is what a retired/discarded root looks like,
    ///             because dots come from the scene DATA and not from renderers.
    ///   CAMERA    a camera that is disabled, culling everything, or clipping at the wrong
    ///             distance produces the same picture from the other end.
    ///   BACKDROP  the gradient quad sits ~2 units in front of the camera in the Background
    ///             queue. It is supposed to be depth-write-OFF so the world paints over it —
    ///             but the code that asks for that is conditional (`if (mat.HasProperty
    ///             ("_ZWrite"))`) and DM_GltfUnlit.mat sets no floats at all, so whether the
    ///             property exists is a fact about the shipped glTFast shader that nothing on a
    ///             build machine can answer. A depth-writing quad two units from the eye hides
    ///             the entire world behind a flat gradient. That is printed here, resolved, at
    ///             runtime, on the device.
    ///   GHOSTS    duplicate "Map" roots / Backdrops / GodRays surviving a switch — the exact
    ///             shape of the ghost-map bug this repo has already been bitten by twice.
    ///
    /// Cheap enough to call on every build and on every host message; it allocates one string.
    /// </summary>
    public static class DarkTrace
    {
        /// <summary>
        /// What the diagnostic concluded about the map root. The badge turns this into a sentence
        /// for the user; the log prints the raw fields beside it.
        /// </summary>
        public enum MapState
        {
            /// <summary>No root at all — the build never produced one, or it was destroyed.</summary>
            Missing = 0,
            /// <summary>A root exists but something switched it off. It cannot draw.</summary>
            Inactive = 1,
            /// <summary>Active, but not one renderer under it is enabled.</summary>
            NoRenderers = 2,
            /// <summary>Active with live renderers — if the screen is still blank, look elsewhere.</summary>
            Live = 3,
        }

        /// <summary>Everything the badge needs, gathered once so the two cannot disagree.</summary>
        public struct Snapshot
        {
            public MapState State;
            public int Roots;          // how many "Map" roots exist in the scene (ghosts!)
            public int Renderers;      // renderers under the live root
            public int EnabledRenderers;
            public int Backdrops;      // Backdrop components in the scene
            public float BackdropDist; // its distance in front of the camera, or -1
            public bool BackdropZWrite;// true when the quad is known to WRITE depth
            public int Cameras;
            public bool CameraOn;
            public float Near, Far;
        }

        public static Snapshot Last { get; private set; }

        /// <summary>
        /// Gather and log. <paramref name="phase"/> says where in the sequence this was taken, so
        /// the first phase at which the picture goes wrong is readable straight off the log.
        /// </summary>
        public static Snapshot Log(string phase)
        {
            var s = new Snapshot { BackdropDist = -1f };

            // ── the map root ────────────────────────────────────────────────────
            // Scene roots rather than GameObject.Find: Find skips INACTIVE objects, and
            // "the root is there but inactive" is one of the answers being looked for.
            GameObject live = null;
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || roots[i].name != "Map") continue;
                s.Roots++;
                if (live == null || roots[i].activeInHierarchy) live = roots[i];
            }

            var map = new StringBuilder(220);
            map.Append("[DarkTrace] ").Append(phase).Append(" · MAP roots=").Append(s.Roots);

            if (live == null)
            {
                s.State = MapState.Missing;
                map.Append(" MISSING");
            }
            else
            {
                Renderer[] rends = live.GetComponentsInChildren<Renderer>(true);
                s.Renderers = rends.Length;
                for (int i = 0; i < rends.Length; i++)
                    if (rends[i] != null && rends[i].enabled && rends[i].gameObject.activeInHierarchy)
                        s.EnabledRenderers++;

                s.State = !live.activeInHierarchy ? MapState.Inactive
                        : s.EnabledRenderers == 0 ? MapState.NoRenderers
                                                  : MapState.Live;

                map.Append(" activeSelf=").Append(live.activeSelf)
                   .Append(" inHierarchy=").Append(live.activeInHierarchy)
                   .Append(" children=").Append(live.transform.childCount)
                   .Append(" renderers=").Append(s.EnabledRenderers).Append('/').Append(s.Renderers)
                   .Append(" layer=").Append(live.layer)
                   .Append(" pos=").Append(live.transform.position.ToString("F0"))
                   .Append(" scale=").Append(live.transform.lossyScale.ToString("F2"))
                   .Append(" → ").Append(s.State);
            }
            Debug.Log(map.ToString());

            // ── the camera ──────────────────────────────────────────────────────
            Camera cam = Camera.main;
            Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,
                                                             FindObjectsSortMode.None);
            s.Cameras = cams.Length;

            var camLine = new StringBuilder(240);
            camLine.Append("[DarkTrace] ").Append(phase).Append(" · CAM count=").Append(s.Cameras);
            if (cam == null)
            {
                camLine.Append(" NO Camera.main");
            }
            else
            {
                s.CameraOn = cam.enabled && cam.gameObject.activeInHierarchy;
                s.Near = cam.nearClipPlane;
                s.Far = cam.farClipPlane;
                var orbit = cam.GetComponent<OrbitCamera>();
                camLine.Append(" main='").Append(cam.name).Append('\'')
                       .Append(" on=").Append(s.CameraOn)
                       .Append(" clip=").Append(s.Near.ToString("F2")).Append("..").Append(s.Far.ToString("F0"))
                       .Append(" mask=0x").Append(cam.cullingMask.ToString("X"))
                       .Append(" clear=").Append(cam.clearFlags)
                       .Append(" bg=").Append(cam.backgroundColor.ToString("F2"))
                       .Append(" pos=").Append(cam.transform.position.ToString("F0"))
                       .Append(" orbit=").Append(orbit == null ? "none" : orbit.enabled ? "on" : "off")
                       .Append(" mode=").Append(ModeManager.Current);
            }
            Debug.Log(camLine.ToString());

            // ── the backdrop quad ───────────────────────────────────────────────
            Backdrop[] backs = Object.FindObjectsByType<Backdrop>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None);
            s.Backdrops = backs.Length;

            var bd = new StringBuilder(300);
            bd.Append("[DarkTrace] ").Append(phase).Append(" · BACKDROP count=").Append(s.Backdrops);
            for (int i = 0; i < backs.Length; i++)
            {
                Transform q = backs[i] != null ? backs[i].Quad : null;
                if (q == null) { bd.Append(" [").Append(i).Append(" no-quad]"); continue; }

                var mr = q.GetComponent<MeshRenderer>();
                Material m = mr != null ? mr.sharedMaterial : null;
                float dist = q.localPosition.z;
                if (i == 0) s.BackdropDist = dist;

                bd.Append(" [").Append(i)
                  .Append(" active=").Append(q.gameObject.activeInHierarchy)
                  .Append(" z=").Append(dist.ToString("F2"))
                  .Append(" scale=").Append(q.localScale.ToString("F1"))
                  .Append(" rend=").Append(mr != null && mr.enabled);

                if (m != null)
                {
                    bd.Append(" shader='").Append(m.shader != null ? m.shader.name : "null").Append('\'')
                      .Append(" queue=").Append(m.renderQueue);

                    // 🔴 THE question for this quad. The code that switches depth writing off is
                    // conditional on the property existing, and nothing off-device can say whether
                    // the shipped glTFast unlit shader has it. If ZWrite is 1 at two units from the
                    // eye in the Background queue, this quad is hiding the entire world.
                    if (m.HasProperty("_ZWrite"))
                    {
                        float zw = m.GetFloat("_ZWrite");
                        s.BackdropZWrite = zw > 0.5f;
                        bd.Append(" ZWrite=").Append(zw.ToString("F0"));
                    }
                    else
                    {
                        // Unknown, and unknown is the dangerous answer: the material never got the
                        // instruction, so it writes depth if the shader's pass does.
                        s.BackdropZWrite = true;
                        bd.Append(" ZWrite=NO-PROPERTY(assumed ON)");
                    }
                    if (m.HasProperty("_ZTest")) bd.Append(" ZTest=").Append(m.GetFloat("_ZTest").ToString("F0"));
                    if (m.HasProperty("_Cull")) bd.Append(" Cull=").Append(m.GetFloat("_Cull").ToString("F0"));
                }
                bd.Append(']');
            }
            Debug.Log(bd.ToString());

            // ── ghosts + atmosphere ─────────────────────────────────────────────
            int godRays = Object.FindObjectsByType<GodRays>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None).Length;
            int reefs = Object.FindObjectsByType<Marine.FishSchoolSystem>(FindObjectsInactive.Include,
                                                                          FindObjectsSortMode.None).Length;
            int listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None).Length;
            Debug.Log($"[DarkTrace] {phase} · GHOSTS godRays={godRays} reefs={reefs} " +
                      $"listeners={listeners} · ATMOS {SceneAtmosphere.StateLine()} " +
                      $"fogCol={RenderSettings.fogColor:F2} lib={NativeBoot.LibraryMode}");

            Last = s;
            return s;
        }

        /// <summary>
        /// One short sentence a non-engineer can photograph and a developer can act on.
        /// Thai first: the person holding the phone is the user, not us (feedback: ทุกข้อความที่
        /// user เห็นต้องเป็นไทย).
        /// </summary>
        public static string Explain(Snapshot s)
        {
            switch (s.State)
            {
                case MapState.Missing:
                    return "ยังไม่มีแมพในฉาก (โหลดไม่สำเร็จ หรือถูกลบทิ้ง)";
                case MapState.Inactive:
                    return "แมพโหลดแล้วแต่ถูกปิดไว้ (ถูกสั่งซ่อน)";
                case MapState.NoRenderers:
                    return "แมพโหลดแล้วแต่ไม่มีอะไรถูกวาดเลย";
                default:
                    return s.BackdropZWrite && s.Backdrops > 0
                        ? "แมพพร้อมวาด — น่าจะมีฉากหลังบังอยู่"
                        : "แมพพร้อมวาด — น่าจะเป็นเรื่องหมอก/แสง";
            }
        }
    }
}
