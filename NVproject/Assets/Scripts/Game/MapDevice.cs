using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// One placed device. It knows what it is, whether it is spent and whether it is still
    /// standing; what its effect actually *does* lives in <see cref="DeviceSystem"/>, so adding an
    /// effect never means touching the thing in the level.
    ///
    /// It keeps a solid collider on purpose. Every other prop the match layer spawns is
    /// pass-through, but a device has to stop a bullet — four of them destroy it, and that is the
    /// Seeker's only counter-play against the whole device system.
    /// </summary>
    public sealed class MapDevice : MonoBehaviour, IInteractable
    {
        private static Material _shellMaterial;

        [Tooltip("Which effect this instance provides.")]
        public DeviceType type = DeviceType.FullMapView;

        private MeshRenderer _panel;
        private Material _panelMaterial;
        private float _pulse;

        public bool Destroyed { get; private set; }
        public int ShotsTaken { get; private set; }
        public bool Spent { get; private set; }

        /// <summary>Wall-clock time this device may next be used. Repeatables have a short lockout.</summary>
        public float NextUseTime { get; private set; }

        /// <summary>Single-use by the ruleset's table. The rest are repeatable.</summary>
        public bool IsOneShot => type == DeviceType.AddTime
                              || type == DeviceType.StopBleeding
                              || type == DeviceType.FreezeAndXray;

        public bool Available => !Destroyed && !Spent && Time.time >= NextUseTime;

        public Vector3 Position => transform.position + Vector3.up * 0.9f;

        public float UseRadius => MatchManager.Instance != null
            ? MatchManager.Instance.Config.deviceUseRadius
            : 2.2f;

        public static MapDevice Spawn(DeviceType type, Vector3 groundPosition, float yaw, Transform parent)
        {
            var go = new GameObject("Device " + type);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(groundPosition, Quaternion.Euler(0f, yaw, 0f));

            var device = go.AddComponent<MapDevice>();
            device.type = type;
            device.Build();
            return device;
        }

        private void OnEnable()
        {
            // A device puts itself on the register rather than trusting whoever spawned it. The
            // very first match starts inside another component's Start, and an object created
            // there cannot assume the system it wants to talk to has finished waking up —
            // measured: nine devices in the level and none of them registered.
            DeviceSystem.Instance?.Register(this);
        }

        private void Build()
        {
            EnsureMaterials();

            // Body: a waist-high console. The collider lives on this box and nowhere else, so a
            // shot that lands anywhere on the device counts once.
            //
            // The dimensions come from `MatchConstants` because the server judges the shot with
            // them (`Room.ConsoleOf`) while this draws what the player aims at. Two copies of that
            // number is a device you can see but not hit.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Shell";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, NV.Shared.Simulation.MatchConstants.DeviceHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(
                NV.Shared.Simulation.MatchConstants.DeviceWidth,
                NV.Shared.Simulation.MatchConstants.DeviceHeight,
                NV.Shared.Simulation.MatchConstants.DeviceDepth);
            body.GetComponent<MeshRenderer>().sharedMaterial = _shellMaterial;

            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Panel";
            Destroy(screen.GetComponent<Collider>());
            screen.transform.SetParent(transform, false);
            screen.transform.localPosition = new Vector3(0f, 0.85f, 0.12f);
            screen.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            screen.transform.localScale = new Vector3(0.52f, 0.06f, 0.34f);

            _panel = screen.GetComponent<MeshRenderer>();
            _panelMaterial = new Material(_shellMaterial) { name = "Device Panel " + type };
            _panelMaterial.EnableKeyword("_EMISSION");
            _panelMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _panel.material = _panelMaterial;

            ApplyPanelColour();
        }

        private void Update()
        {
            if (_panelMaterial == null) return;

            // A live device breathes; a spent or wrecked one sits dark. Across a foggy corridor
            // that pulse is the only way to tell one from the other.
            _pulse = Available ? (Mathf.Sin(Time.time * 2.4f) * 0.5f + 0.5f) * 0.6f + 0.7f : 0f;
            ApplyPanelColour();
        }

        private void ApplyPanelColour()
        {
            Color colour = TypeColour(type);
            if (Destroyed) colour = new Color(0.12f, 0.12f, 0.12f);
            else if (Spent) colour *= 0.35f;

            _panelMaterial.color = colour;
            _panelMaterial.SetColor("_EmissionColor", colour * (Destroyed ? 0f : Mathf.Max(0.4f, _pulse) * 2.2f));
        }

        public string Prompt(PlayerAgent viewer)
        {
            if (viewer == null) return null;

            string label = Label(type);
            if (Destroyed) return label + "  —  DESTROYED";
            if (Spent) return label + "  —  SPENT";

            MatchManager match = MatchManager.Instance;
            if (match != null && !match.Config.seekerCanActivateDevices && viewer.Role == Role.Seeker)
                return label + "  —  shoot to destroy (" + ShotsTaken + "/" + match.Config.deviceDestroyHits + ")";

            float wait = DeviceSystem.Instance != null ? DeviceSystem.Instance.CooldownFor(this) : 0f;
            if (wait > 0f) return label + $"  —  {wait:0.0}s";

            return "[E]  " + label + (IsOneShot ? "  (1x)" : "");
        }

        public void Interact(PlayerAgent user)
        {
            DeviceSystem.Instance?.TryActivate(this, user);
        }

        /// <summary>Marked by the system once the effect has actually fired.</summary>
        internal void MarkUsed(float cooldownUntil)
        {
            if (IsOneShot) Spent = true;
            NextUseTime = cooldownUntil;
            ApplyPanelColour();
        }

        /// <summary>
        /// The server's verdict on this device (IG-013). Networked, this is the only thing that
        /// decides whether it is usable — the local tally was per client, so a one-shot device
        /// could be spent once by every player in the room.
        ///
        /// **The countdown is rebuilt here rather than sent.** The bulletin goes out on change and
        /// every 5 s, which is no place for a number that ticks; it carries "cooling" and this end
        /// turns that into a deadline using the same shared constant the server counted with. The
        /// worst case is that the two disagree by the latency of one bulletin, and the next one
        /// corrects it.
        /// </summary>
        internal void AcceptServerState(bool spent, bool cooling, bool destroyed, int hits)
        {
            Spent = spent;
            ShotsTaken = hits;

            if (destroyed && !Destroyed)
            {
                Destroyed = true;
                DeviceSystem.Instance?.ReportDestroyed(this);
            }

            if (cooling)
            {
                float lockout = type == DeviceType.Teleport
                    ? NV.Shared.Simulation.MatchConstants.TeleportSharedCooldown
                    : NV.Shared.Simulation.MatchConstants.RepeatableDeviceCooldown;

                // 이미 세고 있던 카운트다운을 늘리지 않는다. 전문은 5초마다 같은 내용으로
                // 다시 오므로, 그때마다 새로 잡으면 쿨다운이 영원히 끝나지 않는다.
                if (NextUseTime <= Time.time) NextUseTime = Time.time + lockout;
            }
            else
            {
                NextUseTime = 0f;
            }

            ApplyPanelColour();
        }

        /// <summary>
        /// A round landed on the shell. <see cref="Bullet"/> raises this through
        /// <c>SendMessageUpwards</c>, so it arrives from the child collider.
        /// </summary>
        private void OnHit(float damage)
        {
            if (Destroyed) return;

            // **서버가 전투를 판정하면 여기서 세지 않는다.** `Bullet` 은 쏜 사람의 기계에서만
            // 날고, 그 기계의 탄이 맞았다고 부서지면 부순 사람 화면에서만 부서진 장치가 된다.
            // 서버도 같은 탄을 날리고 있으므로(`Room.TryFindDeviceHit`) 맞은 수와 파괴는
            // 목표물 전문으로 온다 — `MatchManager.ReportHit` 가 사람 피격에 대해 하는 것과
            // 같은 이유의 같은 거절이다.
            if (MatchManager.Instance != null && MatchManager.Instance.ServerOwnsCombat) return;

            ShotsTaken++;
            int needed = MatchManager.Instance != null
                ? MatchManager.Instance.Config.deviceDestroyHits : 4;

            if (ShotsTaken < needed)
            {
                ApplyPanelColour();
                return;
            }

            Destroyed = true;
            ApplyPanelColour();
            DeviceSystem.Instance?.ReportDestroyed(this);
        }

        public static string Label(DeviceType type) => type switch
        {
            DeviceType.AddTime => "ADD TIME",
            DeviceType.FullMapView => "MAP VIEW",
            DeviceType.StopBleeding => "STOP BLEEDING",
            DeviceType.FreezeAndXray => "FREEZE + X-RAY",
            DeviceType.SeekerCameraView => "SEEKER CAM",
            DeviceType.Teleport => "TELEPORT",
            _ => "DEVICE",
        };

        public static Color TypeColour(DeviceType type) => type switch
        {
            DeviceType.AddTime => new Color(0.45f, 0.9f, 0.5f),
            DeviceType.FullMapView => new Color(0.4f, 0.75f, 1f),
            DeviceType.StopBleeding => new Color(1f, 0.45f, 0.5f),
            DeviceType.FreezeAndXray => new Color(0.6f, 0.9f, 1f),
            DeviceType.SeekerCameraView => new Color(1f, 0.6f, 0.25f),
            DeviceType.Teleport => new Color(0.75f, 0.5f, 1f),
            _ => Color.white,
        };

        private static void EnsureMaterials()
        {
            if (_shellMaterial != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _shellMaterial = new Material(shader)
            {
                name = "Device Shell",
                color = new Color(0.22f, 0.22f, 0.24f),
            };
            _shellMaterial.enableInstancing = true;
        }
    }
}
