using UnityEngine;

namespace DiveMap.Runtime
{
    /// <summary>
    /// Keeps a label upright and facing the active camera (billboard). Used for the
    /// placeholder text labels so they stay readable while the orbit camera moves.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Billboard : MonoBehaviour
    {
        private Camera _cam;

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Face the camera but keep world-up so text never tilts.
            Vector3 dir = transform.position - _cam.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}
