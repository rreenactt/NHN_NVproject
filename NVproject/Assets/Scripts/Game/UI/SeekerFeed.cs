using UnityEngine;

namespace NV.Game.UI
{
    /// <summary>
    /// The security-camera device: a real camera parked at the Seeker's head, rendering into a
    /// texture the HUD shows as a CRT feed.
    ///
    /// It really is a second camera rather than a faked overhead marker, because what makes the
    /// device worth a walk is seeing what the Seeker is *looking at* — a dot on a map cannot tell
    /// a Runner whether they have been spotted.
    ///
    /// The feed obeys the Seeker's own visibility rules: no door, and the blood only the Seeker can
    /// see. Anything else would leak the Runners' secret through their own device.
    /// </summary>
    public sealed class SeekerFeed
    {
        private Camera _camera;
        private RenderTexture _target;

        public RenderTexture Target => _target;
        public bool Live => _camera != null;

        public void Ensure(Transform parent)
        {
            if (_camera != null) return;

            _target = new RenderTexture(420, 236, 16) { name = "Seeker Feed", filterMode = FilterMode.Bilinear };

            var go = new GameObject("Seeker Feed Camera");
            go.transform.SetParent(parent, false);

            _camera = go.AddComponent<Camera>();
            _camera.targetTexture = _target;
            _camera.fieldOfView = 74f;
            _camera.nearClipPlane = 0.05f;
            _camera.cullingMask = ~0 & ~(1 << 8);      // never the Seeker's own viewmodel arms
            MatchLayers.ApplyRoleVisibility(_camera, Role.Seeker);
        }

        /// <returns>False when there is nobody to watch, so the HUD can show static instead.</returns>
        public bool Follow(PlayerAgent seeker)
        {
            if (_camera == null) return false;

            if (seeker == null || !seeker.InPlay)
            {
                _camera.enabled = false;
                return false;
            }

            _camera.enabled = true;
            Transform head = seeker.head != null ? seeker.head : seeker.transform;
            _camera.transform.SetPositionAndRotation(head.position, head.rotation);
            return true;
        }

        public void Release()
        {
            if (_camera != null) Object.Destroy(_camera.gameObject);
            _camera = null;

            if (_target != null) _target.Release();
            _target = null;
        }
    }
}
