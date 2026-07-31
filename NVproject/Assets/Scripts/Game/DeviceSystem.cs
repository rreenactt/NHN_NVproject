using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// Owns what the devices *do*. The objects in the level only know their type; every rule about
    /// uses, cooldowns and effects is resolved here, on the authority, for the same reason the
    /// match rules are: eight consoles each enforcing their own version of "shared 12 second
    /// cooldown" is eight chances to disagree.
    ///
    /// Two effects — the map view and the Seeker camera — are pure presentation, so they are
    /// raised as <see cref="EffectFired"/> and drawn by the HUD. The rest change the world and are
    /// applied right here.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class DeviceSystem : MonoBehaviour
    {
        private static DeviceSystem _instance;

        /// <summary>Same recovery as <see cref="MatchManager.Instance"/> — a domain reload during play wipes statics without re-running Awake.</summary>
        public static DeviceSystem Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<DeviceSystem>();
                return _instance;
            }
            private set => _instance = value;
        }

        /// <summary>Raised when a device fires: type, who used it, how long it lasts.</summary>
        public event Action<DeviceType, PlayerAgent, float> EffectFired;

        /// <summary>Raised when the Seeker destroys one.</summary>
        public event Action<MapDevice> DeviceDestroyed;

        private readonly List<MapDevice> _devices = new List<MapDevice>();
        private float _teleportReadyTime;
        private Coroutine _freeze;

        private GameConfig Config => MatchManager.Instance != null ? MatchManager.Instance.Config : null;

        /// <summary>Seconds until the shared teleport lockout expires. Global, not per player.</summary>
        public float TeleportCooldownRemaining => Mathf.Max(0f, _teleportReadyTime - Time.time);

        public IReadOnlyList<MapDevice> Devices => _devices;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Sweeps up anything placed before this system was listening. Devices register themselves,
        /// but the first match is started from another component's Start — early enough that the
        /// spawner cannot rely on this object being awake yet. One scan closes that window; after
        /// it, registration is immediate.
        /// </summary>
        private void Start()
        {
            foreach (MapDevice device in FindObjectsByType<MapDevice>(FindObjectsSortMode.None))
                Register(device);
        }

        public void Register(MapDevice device)
        {
            if (device != null && !_devices.Contains(device)) _devices.Add(device);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _devices.Count; i++)
                if (_devices[i] != null) Destroy(_devices[i].gameObject);
            _devices.Clear();
            _teleportReadyTime = 0f;
        }

        /// <summary>Whatever lockout applies to this device right now, in seconds.</summary>
        public float CooldownFor(MapDevice device)
        {
            if (device == null) return 0f;
            float own = Mathf.Max(0f, device.NextUseTime - Time.time);
            return device.type == DeviceType.Teleport
                ? Mathf.Max(own, TeleportCooldownRemaining)
                : own;
        }

        /// <summary>
        /// The single gate every activation goes through. Returns false with a reason the HUD can
        /// show — a device that silently refuses reads as a bug, and players will shoot it.
        /// </summary>
        public bool TryActivate(MapDevice device, PlayerAgent user)
        {
            GameConfig config = Config;
            MatchManager match = MatchManager.Instance;
            if (device == null || user == null || match == null || config == null) return false;

            if (match.Phase != MatchPhase.Playing) return Refuse(match, "match is not running");
            if (!user.InPlay) return false;
            if (device.Destroyed) return Refuse(match, "device destroyed");
            if (device.Spent) return Refuse(match, "device already used");
            if (user.Role == Role.Seeker && !config.seekerCanActivateDevices)
                return Refuse(match, "shoot it instead");

            float cooldown = CooldownFor(device);
            if (cooldown > 0f) return Refuse(match, $"{MapDevice.Label(device.type)} ready in {cooldown:0.0}s");

            // Stop Bleeding is single-use and cannot be un-used. Spending it while healthy would
            // be a mistake the player cannot take back, so it is refused rather than wasted.
            if (device.type == DeviceType.StopBleeding && !user.Bleeding)
                return Refuse(match, "you are not bleeding");

            float duration = Apply(device.type, user, match, config);

            float until = Time.time + (device.IsOneShot ? 0f : config.repeatableDeviceCooldown);
            if (device.type == DeviceType.Teleport)
                _teleportReadyTime = Time.time + config.teleportSharedCooldown;

            device.MarkUsed(until);
            EffectFired?.Invoke(device.type, user, duration);
            return true;
        }

        private static bool Refuse(MatchManager match, string reason)
        {
            match.Notify(reason);
            return false;
        }

        /// <returns>How long the effect lasts, for the HUD. 0 for instant effects.</returns>
        private float Apply(DeviceType type, PlayerAgent user, MatchManager match, GameConfig config)
        {
            switch (type)
            {
                case DeviceType.AddTime:
                    match.AddTime(config.deviceTimeBonus);
                    match.Notify($"+{Mathf.RoundToInt(config.deviceTimeBonus)}s ON THE CLOCK");
                    return 0f;

                case DeviceType.StopBleeding:
                    match.ClearBleeding(user);
                    match.Notify("BLEEDING STOPPED");
                    return 0f;

                case DeviceType.Teleport:
                    match.TeleportToRandomPoint(user);
                    match.Notify("TELEPORTED");
                    return 0f;

                case DeviceType.FreezeAndXray:
                    if (_freeze != null) StopCoroutine(_freeze);
                    _freeze = StartCoroutine(FreezeRoutine(config, match));
                    return config.freezeDuration;

                case DeviceType.FullMapView:
                    return config.mapViewDuration;

                case DeviceType.SeekerCameraView:
                    return config.seekerCamDuration;
            }
            return 0f;
        }

        /// <summary>
        /// Everyone stops and the walls go see-through. The x-ray is one shared material switch on
        /// the level, so it is necessarily global — which matches the rule ("freeze everyone"),
        /// and is why the effect is worth a single use.
        /// </summary>
        private IEnumerator FreezeRoutine(GameConfig config, MatchManager match)
        {
            match.Notify("EVERYTHING STOPS");
            match.SetGlobalFreeze(true);
            match.Map?.SetWallTransparency(config.xrayWallAlpha);

            yield return new WaitForSeconds(config.freezeDuration);

            match.Map?.SetWallTransparency(1f);
            match.SetGlobalFreeze(false);
            _freeze = null;
        }

        internal void ReportDestroyed(MapDevice device)
        {
            DeviceDestroyed?.Invoke(device);
            MatchManager.Instance?.Notify($"{MapDevice.Label(device.type)} DESTROYED");
        }
    }
}
