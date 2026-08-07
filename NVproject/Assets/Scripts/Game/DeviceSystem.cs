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
        private bool _serverFreeze;

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

            // 다음 배치의 첫 전문이 정지 없음으로 오면 그것이 곧 모서리여야 한다. 남겨 두면
            // 지난 매치에서 켜진 채로 끝난 투시가 새 매치에서 꺼지지 않는다.
            _serverFreeze = false;
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

            // **서버가 판정하는 매치에서는 여기서 효과를 걸지 않는다.** E 는 이미 입력 프레임의
            // Interact 비트로 나가 있고(`FirstPersonController.ConsumeInteract`), 서버가 같은
            // 검사를 다시 한 뒤 `Room.TryUseDevice` 에서 효과를 건다. 여기서 함께 걸면 서버가
            // 소유한 것을 로컬로 바꾸는 셈이라 다음 전문이 그대로 되돌린다 — 순간이동은 제자리로
            // 튕기고, 지혈은 한 프레임 뒤 다시 피가 흐르고, 시계는 원래 값으로 돌아간다.
            //
            // 소진·쿨다운도 서버가 보낸다(`AcceptServerState`). 여기서 미리 표시하면 서버가
            // 거절한 사용이 화면에서만 소진된 것으로 남는다.
            if (match.ServerOwnsObjectives)
            {
                // 화면에만 사는 둘은 예외다. 서버에는 옮길 상태가 없고 — 이 지도를 본 사람이
                // 누구인지는 아무 규칙도 바꾸지 않는다 — 누른 프레임에 열리지 않으면 왕복
                // 지연만큼 늦게 열린다. 위의 검사가 서버가 볼 조건과 같은 것을 이미 다 봤으므로
                // 예측이 틀리는 경우는 전문 하나만큼의 창이다.
                if (device.type == DeviceType.FullMapView || device.type == DeviceType.SeekerCameraView)
                    EffectFired?.Invoke(device.type, user, Apply(device.type, user, match, config));

                return true;
            }

            float duration = Apply(device.type, user, match, config);

            float until = Time.time + (device.IsOneShot ? 0f : config.repeatableDeviceCooldown);
            if (device.type == DeviceType.Teleport)
                _teleportReadyTime = Time.time + config.teleportSharedCooldown;

            device.MarkUsed(until);
            EffectFired?.Invoke(device.type, user, duration);
            return true;
        }

        /// <summary>
        /// The server's device states, straight off the bulletin (IG-013). Indexed the same as the
        /// objective placement, which is the order the devices were spawned in.
        ///
        /// The freeze is applied here too, because it is the one device effect that is *world*
        /// state on every client rather than the user's own screen: the walls go transparent and
        /// everybody stops. Which client pressed the button does not enter into it.
        /// </summary>
        public void AcceptServerStates(NV.Client.Net.NetworkClient client)
        {
            if (client == null) return;

            for (int i = 0; i < _devices.Count && i < client.DeviceStateCount; i++)
            {
                if (_devices[i] == null) continue;

                var state = client.DeviceStateAt(i);
                _devices[i].AcceptServerState(
                    (state & NV.Shared.Contracts.Enums.MatchDeviceState.Spent) != 0,
                    (state & NV.Shared.Contracts.Enums.MatchDeviceState.Cooling) != 0,
                    (state & NV.Shared.Contracts.Enums.MatchDeviceState.Destroyed) != 0,
                    NV.Shared.Contracts.Enums.MatchDeviceHits.Of(state));
            }

            ApplyServerFreeze(client.DeviceFreezeActive);
        }

        /// <summary>
        /// The freeze device, driven by the server rather than by a local coroutine.
        ///
        /// Idempotent on purpose — it is called every frame off a polled bulletin, and
        /// <c>SetWallTransparency</c> walks every wall material. Only the edges do anything.
        /// </summary>
        private void ApplyServerFreeze(bool active)
        {
            if (active == _serverFreeze) return;
            _serverFreeze = active;

            MatchManager match = MatchManager.Instance;
            GameConfig config = Config;
            if (match == null || config == null) return;

            if (active) match.Notify("EVERYTHING STOPS");

            match.SetGlobalFreeze(active);
            match.Map?.SetWallTransparency(active ? config.xrayWallAlpha : 1f);
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
