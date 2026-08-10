using System;
using UnityEngine;
using DiveMap.Core;

namespace DiveMap.Runtime
{
    /// <summary>
    /// The other end of the host bridge (WO-MERGE P1): the GameObject
    /// <c>UnitySendMessage("AppBoot", "OnNativeBoot", json)</c> lands on.
    ///
    /// 🔴 Why a dedicated object rather than the scene's bootstrap. UnitySendMessage addresses a
    /// GameObject BY NAME, and the object carrying <see cref="AppBoot"/> in Main.unity is called
    /// "Bootstrap". Renaming it to match a string literal in another repository would make a
    /// scene file the contract, and the failure mode of getting it wrong is invisible: Unity logs
    /// nothing useful, the map simply never switches and nobody can tell whether the message was
    /// sent, received or ignored.
    ///
    /// The second reason is timing, and it is the one that actually bites. Unity boots
    /// asynchronously inside the host app; the RN screen posts its boot message as soon as its
    /// view mounts. That message can therefore arrive BEFORE the scene has loaded, before
    /// AppBoot.Start has run, or long after the first map is already on screen. This object is
    /// created at <c>BeforeSceneLoad</c> and survives scene loads, so there is always something
    /// listening, and each of the three cases has an explicit answer below rather than a race:
    ///
    ///   • before AppBoot exists      → the id is written to PlayerPrefs; AppBoot.Start reads it
    ///   • while the first load runs  → AppBoot.SwitchMapFromHost queues or restarts the load
    ///   • after the map is up        → an ordinary map switch, the same one the hub performs
    ///
    /// <see cref="AppBoot.OnNativeBoot"/> forwards here too, so a host that addresses the scene
    /// object instead gets identical behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NativeBootReceiver : MonoBehaviour
    {
        private static NativeBootReceiver _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            // The name is the address. NativeBoot.ReceiverObjectName is the single place it is
            // written down, shared with whoever has to keep the RN side in step.
            var go = new GameObject(NativeBoot.ReceiverObjectName);
            _instance = go.AddComponent<NativeBootReceiver>();
            DontDestroyOnLoad(go);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// The host's entry point. The signature — public, void, one string — is fixed by
        /// UnitySendMessage; anything else and the call is dropped with a warning that names the
        /// method but not the reason.
        /// </summary>
        public void OnNativeBoot(string json) => Apply(json);

        /// <summary>
        /// Read one boot payload and act on it. Malformed input is logged and dropped: leaving
        /// the app exactly as it was is always a safe answer, and a half-applied boot state is
        /// not.
        /// </summary>
        public static void Apply(string json)
        {
            if (!NativeBoot.TryParse(json, out NativeBootArgs args))
            {
                Debug.LogWarning("[Native] boot message ignored — not a JSON object: " + Preview(json));
                return;
            }

            bool wasLibrary = NativeBoot.LibraryMode;
            NativeBoot.Adopt(args);

            Debug.Log($"[Native] boot shortId='{NativeBoot.Current.ShortId}' " +
                      $"device={Mask(NativeBoot.HostDeviceId)} lang='{NativeBoot.Current.Lang}' " +
                      $"libraryMode={NativeBoot.LibraryMode} " +
                      $"token={(string.IsNullOrEmpty(NativeBoot.AuthToken) ? "none" : "held")}");

            ApplyLanguage(args.Lang);

            // Chrome before the map: switching maps tears the scene down and rebuilds it, and the
            // action column must already know it is a host screen when the new map's UI settles.
            if (NativeBoot.LibraryMode && !wasLibrary) ApplyHostChrome();

            ApplyMap(args.ShortId);
        }

        /// <summary>
        /// Follow the host's language. The setter persists to PlayerPrefs on purpose: the host is
        /// the authority on what language the user reads, and a 3D screen that came up in Thai
        /// inside an English app would be a bug reported as "the map is not translated".
        /// An unknown or absent code leaves the current language alone (NativeBoot.TryParse has
        /// already clamped it to "" in that case).
        /// </summary>
        private static void ApplyLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return;
            if (string.Equals(UiStrings.Lang, lang, StringComparison.Ordinal)) return;

            UiStrings.Lang = lang;
            if (Ui.UiShell.Instance != null) Ui.UiShell.Instance.ApplyLanguage();
            Debug.Log("[Native] language ← host: " + lang);
        }

        /// <summary>
        /// Re-render the shell for host mode. Only the affordances need this: every action that
        /// behaves differently in library mode decides at TAP time, which is the pattern the rest
        /// of the action column already uses for things not known when the button was built.
        /// </summary>
        private static void ApplyHostChrome()
        {
            if (Ui.UiShell.Instance != null) Ui.UiShell.Instance.ApplyHostMode();
        }

        /// <summary>
        /// Open the map the host asked for, whenever in the boot sequence the ask arrives.
        ///
        /// PlayerPrefs is written FIRST and unconditionally. It is what AppBoot.Start reads, so it
        /// covers the case where the scene has not booted yet, and it costs nothing in the cases
        /// where it has (AppBoot.LoadMap writes the same key itself).
        /// </summary>
        private static void ApplyMap(string shortId)
        {
            if (string.IsNullOrEmpty(shortId)) return;

            PlayerPrefs.SetString(AppBoot.ShortIdPrefKey, shortId);
            PlayerPrefs.Save();

            AppBoot boot = UnityEngine.Object.FindFirstObjectByType<AppBoot>();
            if (boot == null)
            {
                Debug.Log("[Native] map " + shortId + " parked in PlayerPrefs — AppBoot has not started yet");
                return;
            }
            boot.SwitchMapFromHost(shortId);
        }

        /// <summary>Never log a whole device id; eight characters is enough to match two logs up.</summary>
        private static string Mask(string id) =>
            string.IsNullOrEmpty(id) ? "(none)" : (id.Length <= 8 ? id : id.Substring(0, 8) + "…");

        /// <summary>
        /// Enough of a rejected payload to recognise it, capped so a runaway string cannot fill
        /// the device log — which is the only place this failure is ever visible.
        /// </summary>
        private static string Preview(string json)
        {
            if (json == null) return "(null)";
            string s = json.Trim();
            if (s.Length == 0) return "(empty)";
            return s.Length <= 120 ? s : s.Substring(0, 120) + "…";
        }
    }
}
