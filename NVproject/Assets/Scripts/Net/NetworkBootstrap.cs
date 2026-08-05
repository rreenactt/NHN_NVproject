using NV.Client.Net.Session;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using UnityEngine;

namespace NV.Client.Net
{
    /// 씬과 세션을 잇는 지점. 스냅샷을 몸에 적용하는 일만 한다.
    ///
    /// 접속을 소유하지 않는다. 그것은 `NetSession` 의 몫이고, 세션은 씬보다 오래
    /// 산다 — 로비에서 붙은 소켓이 게임 씬으로 넘어가야 하기 때문이다. 예전에는 이
    /// 컴포넌트가 접속을 들고 있었고, 그래서 씬에 플레이어가 없으면 접속 자체가
    /// 불가능했다.
    ///
    /// 세션이 없으면 아무 일도 하지 않는다. 오프라인으로 씬을 여는 경로가 그대로
    /// 남아야 하고, 여기서 세션을 만들어 버리면 혼자 돌려 보는 것이 불가능해진다.
    ///
    /// 로컬 플레이어와 원격 플레이어의 적용 방식이 다르다.
    /// - 원격: 100ms 보간 버퍼. 30Hz 스냅샷을 렌더 프레임레이트로 펴는 표준 수단이다.
    /// - 로컬: 최신 스냅샷으로 짧게 감쇠. 자기 캐릭터에 보간 지연을 얹으면 왕복 지연
    ///   위에 100ms 가 더 붙어 조작이 무거워진다. 예측(M5)이 들어오면 이 경로가 대체된다.
    ///
    /// 실행 순서를 NetSession(-120)·NetworkClient(-100) 뒤, FirstPersonController(0)
    /// 앞에 둔다. 트랜스폼을 옮긴 다음 컨트롤러가 변위를 재야 걸음 속도가 한 프레임
    /// 늦지 않는다.
    [DefaultExecutionOrder(-90)]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [Header("Smoothing")]
        [Tooltip("로컬 플레이어가 서버 위치로 수렴하는 시간(초). 30Hz 계단을 펴는 용도이므로 짧게 둔다.")]
        public float localSmoothing = 0.05f;

        [Tooltip("이 거리를 넘는 보정은 감쇠 없이 순간이동시킨다. 스폰과 리스폰이 그 경우다.")]
        public float localSnapDistance = 2f;

        [Header("Debug")]
        [Tooltip("연결 UI 가 없을 때만 쓰는 최소 상태 표시. NetworkTestUi 가 있으면 그쪽이 그린다.")]
        public bool showOverlay = false;

        private NetworkClient _client;
        private FirstPersonController _localPlayer;
        private BlockRig _localRig;
        private INetworkMapSource _map;

        private readonly RemotePlayerPuppet[] _puppets = new RemotePlayerPuppet[SnapshotBuffer.MaxEntities];
        private readonly byte[] _liveIds = new byte[SnapshotBuffer.MaxEntities];
        private readonly bool[] _seen = new bool[SnapshotBuffer.MaxEntities];

        private Vector3 _localVelocity;
        private bool _localPlaced;
        private float _offlineWalkSpeed;
        private float _offlineSprintSpeed;
        private uint _clientMapHash;
        private bool _mapHashChecked;
        private string _mapHashStatus = "미확인";
        private bool _serverAuthority;

        public NetworkClient Client => _client;

        /// 로컬 플레이어. UI 가 커서와 입력을 넘겨받을 때 쓴다.
        public FirstPersonController LocalPlayer => _localPlayer;

        /// 클라이언트가 계산한 맵 해시. Welcome 을 받은 뒤에만 값이 있다.
        public uint ClientMapHash => _clientMapHash;

        public string MapHashStatus => _mapHashStatus;

        public string MapName => _map != null ? _map.MapName : "(맵 없음)";

        /// 이 id 의 원격 몸. 없으면 false — 첫 스냅샷이 오기 전에는 아무 몸도 없다.
        public bool TryGetPuppet(byte playerId, out RemotePlayerPuppet puppet)
        {
            puppet = playerId < _puppets.Length ? _puppets[playerId] : null;
            return puppet != null;
        }

        /// 지금 씬에 있는 원격 몸의 수. 명단과 대조해 전원이 도착했는지 판단하는 데 쓴다.
        public int PuppetCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < _puppets.Length; index++)
                {
                    if (_puppets[index] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// 명단에서 이 id 의 표시 이름을 찾는다. 없으면 빈 문자열이다.
        private string RosterName(byte playerId)
        {
            if (_client == null)
            {
                return string.Empty;
            }

            for (var index = 0; index < _client.RosterCount; index++)
            {
                var entry = _client.RosterEntry(index);
                if (entry.PlayerId == playerId)
                {
                    return entry.Name;
                }
            }

            return string.Empty;
        }

        private void Start()
        {
            _localPlayer = FindLocalPlayer();
            if (_localPlayer == null)
            {
                Debug.LogError("[NV] Player 에 FirstPersonController 가 없다. 연동을 시작할 수 없다.");
                enabled = false;
                return;
            }

            _localRig = _localPlayer.GetComponent<BlockRig>();
            _map = MapExport.FindInScene();

            // 접속하기 전에는 오프라인 조작 그대로 둔다. 여기서 서버 권위로 바꿔 버리면
            // 서버가 없는 동안 캐릭터가 아무 입력에도 반응하지 않고, 원인이 드러나지 않는다.
            _offlineWalkSpeed = _localPlayer.walkSpeed;
            _offlineSprintSpeed = _localPlayer.sprintSpeed;

            PrecomputeMapHash();

            EnsureAppearanceSync();

            Bind();
        }

        /// 명단의 캐릭터를 몸에 입히는 다리를 세운다.
        ///
        /// 여기서 만드는 이유는 **원격 몸을 이 컴포넌트가 만들기 때문**이다. 매치 레이어가
        /// 없는 씬(`MultiplayerTest`)에도 몸은 있고, 로비에서 고른 캐릭터는 거기서도 보여야
        /// 한다 — `MatchBootstrap` 에 매달면 그 씬에서는 아무도 옷을 입지 않는다.
        ///
        /// 씬에 미리 두지 않는다. 이 프로젝트의 규칙대로 씬은 거의 비어 있고, 어떤 메뉴를
        /// 돌렸는지에 동작이 달라지지 않아야 한다.
        private void EnsureAppearanceSync()
        {
            if (FindFirstObjectByType<Session.AppearanceSync>() != null)
            {
                return;
            }

            var go = new GameObject("Appearance Sync");
            go.transform.SetParent(transform, false);
            go.AddComponent<Session.AppearanceSync>();
        }

        /// 맵 해시를 **미리** 계산한다.
        ///
        /// 예전에는 `Welcome` 을 받은 프레임에 계산했다. 그 계산은 격자 셀마다 겹침 해소를
        /// 부르고 겹침 해소는 박스 목록을 선형으로 훑으므로(브로드페이즈가 없다) `backrooms`
        /// 에서는 상한이 수백만 회 AABB 검사다. WebGL 은 단일 스레드이므로 그것이 접속하는
        /// 프레임에 그대로 얹혔다.
        ///
        /// 여기서는 레벨이 `Awake` 에 이미 만들어져 있고 아직 아무것도 급하지 않다. 결과는
        /// `MapExport` 가 캐시하므로 오프라인 배치도 같은 것을 쓴다.
        private void PrecomputeMapHash()
        {
            if (_map == null)
            {
                return;
            }

            var data = MapExport.BuildMapDataCached(_map);
            _clientMapHash = data == null ? 0u : data.ComputeHash();
        }

        /// 세션이 있으면 그 클라이언트를 붙인다. 없으면 오프라인이다.
        ///
        /// 세션을 여기서 만들지 않는다. `NetSession.Current` 는 없을 때 만들어 버리므로
        /// `Exists` 로 먼저 확인한다 — 그러지 않으면 오프라인으로 씬을 여는 것만으로
        /// 세션이 생기고, 그 세션은 아무 곳에도 접속하지 않은 채 남는다.
        private void Bind()
        {
            if (_client != null || !NetSession.Exists)
            {
                return;
            }

            _client = NetSession.Current.Client;
            if (_client == null)
            {
                return;
            }

            // 입력원은 씬이 준다. 세션은 플레이어가 어디 있는지 모르고, 알게 만들면
            // 세션이 씬 구조에 묶여 로비 씬에서 살아 있을 수 없다.
            _client.InputSource = new LocalInputSource(_localPlayer);

            _client.WelcomeReceived += OnWelcome;
            _client.Ended += OnEnded;
            _client.FireObserved += OnFireObserved;

            // **Welcome 은 대개 이미 지나갔다.** 접속하는 것은 로비이고 이 컴포넌트는 게임 씬에서
            // 만들어지므로, 구독만 해 두면 이 씬에서는 그 이벤트가 다시 오지 않는다 — 맵 해시
            // 검사가 통째로 건너뛰어지고 상태는 "미확인" 으로 남는다. 그 검사가 이 좌표계 커플링의
            // 유일한 감시이며, 어긋났을 때의 증상은 "특정 위치에서만 캐릭터가 튐" 하나뿐이라
            // 조용히 넘어가는 것이 가장 나쁘다. `OnWelcome` 은 `_mapHashChecked` 로 멱등하다.
            if (_client.HasWelcome)
            {
                OnWelcome();
            }
        }

        private void OnDestroy()
        {
            if (_client == null)
            {
                return;
            }

            _client.WelcomeReceived -= OnWelcome;
            _client.Ended -= OnEnded;
            _client.FireObserved -= OnFireObserved;
        }

        /// 남이 쏜 총알의 예광탄을 그린다(IG-028b2).
        ///
        /// **자기 발사는 그리지 않는다.** 로컬 `Bullet` 이 트리거를 당긴 프레임에 이미 예광탄을
        /// 만들고, 그것이 히트마커·발사음·반동의 타이밍도 함께 만든다. 서버 알림으로 갈아타면
        /// 자기 사격의 반응이 한 왕복만큼 늦어지는데, §8 이 로컬 연출에 예측을 허용하는 것이
        /// 정확히 그 이유다. **대가는 히트마커가 서버 판정과 어긋날 수 있다는 것**이고 그것은
        /// 판정이 아니라 표시다.
        ///
        /// **알림의 틱을 써서 앞으로 건너뛰지 않는다.** 늦게 도착한 만큼 총알을 진행시키는 것이
        /// 정확해 보이지만, **원격 몸은 보간 때문에 100ms 과거에 그려진다** — 예광탄만 현재로
        /// 당기면 그것을 쏜 몸의 총구와 어긋난다. 원격 표현 전체가 같은 만큼 과거에 있는 편이
        /// 일관되다. 틱은 그 보정을 원하는 클라이언트를 위해 와이어에 남아 있다.
        private void OnFireObserved(FireEventMessage fire)
        {
            if (!_client.HasWelcome || fire.ShooterId == _client.LocalPlayerId)
            {
                return;
            }

            var origin = new Vector3(
                Quantization.ToMeters(fire.X),
                Quantization.ToMeters(fire.Y),
                Quantization.ToMeters(fire.Z));

            // 방향은 요·피치에서 만든다. 서버가 총알을 만들 때 쓴 것과 **같은 함수**이므로
            // 예광탄이 실제 탄도와 같은 쪽으로 날아간다.
            var forward = PlayerMovement.Forward(
                Quantization.ToYawRadians(fire.Yaw),
                Quantization.ToPitchRadians(fire.Pitch));

            // 데미지 0. 판정은 서버가 하고 이것은 연출이다 — `Bullet` 의 `OnHit` 는
            // `MatchManager.ReportHit` 로 가고 그쪽이 `ServerOwnsCombat` 에서 거부한다.
            // 마스크에서 뷰모델 팔(레이어 8)만 뺀다: 몸에 맞아 멈추는 것은 맞는 표현이다.
            Bullet.Spawn(
                origin,
                new Vector3(forward.X, forward.Y, forward.Z),
                MatchConstants.BulletSpeed,
                0f,
                0f,
                ~(1 << 8),
                MatchConstants.BulletLifetime);
        }

        private void Update()
        {
            Bind();

            if (_client == null)
            {
                return;
            }

            ApplyAuthority(_client.State == ConnectionState.Playing && _client.Phase == RoomPhase.Playing);

            if (!_serverAuthority || _client.Snapshots == null)
            {
                return;
            }

            ApplyLocal();
            ApplyRemotes();
        }

        /// 서버 권위는 룸이 진행 단계일 때만 켠다.
        ///
        /// Welcome 시점에 켜면 안 된다. 대기 단계에서는 서버가 시뮬레이션하지 않으므로
        /// 스냅샷이 오지 않고, 그동안 캐릭터는 입력에도 반응하지 않고 서버 위치로도
        /// 옮겨지지 않는다 — 화면에서는 그냥 멈춘 것으로 보인다.
        private void ApplyAuthority(bool serverAuthority)
        {
            if (_serverAuthority == serverAuthority)
            {
                return;
            }

            _serverAuthority = serverAuthority;

            if (_localPlayer == null)
            {
                return;
            }

            if (serverAuthority)
            {
                _localPlayer.controlMode = FirstPersonController.ControlMode.NetworkAuthority;

                // 걸음걸이와 기울기는 컨트롤러의 속도 파라미터를 기준으로 스케일된다.
                // 이동은 이제 서버가 하므로, 그 기준을 서버 상수로 맞춰야 보폭이 맞는다.
                _localPlayer.walkSpeed = SimConstants.MoveSpeed;
                _localPlayer.sprintSpeed = SimConstants.MoveSpeed * SimConstants.SprintMultiplier;

                // 서버가 매치 시작에 전원을 스폰으로 되돌린다. 그 보정은 감쇠 없이 받는다.
                _localPlaced = false;
                _localVelocity = Vector3.zero;
                return;
            }

            _localPlayer.controlMode = FirstPersonController.ControlMode.Local;
            _localPlayer.walkSpeed = _offlineWalkSpeed;
            _localPlayer.sprintSpeed = _offlineSprintSpeed;
        }

        /// 끊기면 원격 몸을 지우고 로컬 플레이어를 오프라인 조작으로 되돌린다.
        /// 남겨 두면 서버가 없는데 다른 플레이어의 시체가 허공에 서 있고, 자기 캐릭터는
        /// 아무 입력에도 반응하지 않는다 — 둘 다 원인이 드러나지 않는 증상이다.
        private void OnEnded()
        {
            for (var id = 0; id < _puppets.Length; id++)
            {
                if (_puppets[id] == null) continue;

                Destroy(_puppets[id].gameObject);
                _puppets[id] = null;
            }

            _localPlaced = false;
            _localVelocity = Vector3.zero;
            _mapHashChecked = false;
            _mapHashStatus = "미확인";

            ApplyAuthority(false);
        }

        /// 로컬 플레이어는 최신 스냅샷을 향해 짧게 감쇠한다.
        private void ApplyLocal()
        {
            if (!_client.Snapshots.TryLatest(_client.LocalPlayerId, out var sample))
            {
                return;
            }

            var offset = _localRig != null ? _localRig.GroundOffset : 0f;
            var current = _localPlayer.transform.position - new Vector3(0f, offset, 0f);

            Vector3 target;
            if (!_localPlaced || Vector3.Distance(current, sample.Position) > localSnapDistance)
            {
                // 스폰·리스폰·큰 보정. 감쇠로 끌면 벽을 통과해 미끄러져 들어간다.
                target = sample.Position;
                _localVelocity = Vector3.zero;
                _localPlaced = true;
            }
            else
            {
                target = Vector3.SmoothDamp(current, sample.Position, ref _localVelocity, localSmoothing);
            }

            _localPlayer.ApplyNetworkState(target, sample.IsGrounded, sample.Velocity.y);
        }

        /// 원격 플레이어는 보간 버퍼에서 꺼낸다. 스냅샷에서 사라진 몸은 지운다.
        private void ApplyRemotes()
        {
            for (var index = 0; index < _seen.Length; index++)
            {
                _seen[index] = false;
            }

            var count = _client.Snapshots.ReadLatestIds(_liveIds);
            var now = Time.unscaledTime;

            for (var index = 0; index < count; index++)
            {
                var id = _liveIds[index];
                if (id >= _puppets.Length)
                {
                    continue;
                }

                _seen[id] = true;

                if (id == _client.LocalPlayerId)
                {
                    continue;
                }

                if (!_client.Snapshots.TrySample(id, now, out var sample))
                {
                    continue;
                }

                if (_puppets[id] == null)
                {
                    // 씬 루트에 만든다. 이 컴포넌트가 어디에 붙어 있든 원격 몸이
                    // 로컬 플레이어의 트랜스폼을 상속하지 않게 한다.
                    _puppets[id] = RemotePlayerPuppet.Create(id, RosterName(id), null, null);
                    Debug.Log($"[NV] 플레이어 {id} 입장.");
                }

                _puppets[id].Apply(sample);
            }

            for (var id = 0; id < _puppets.Length; id++)
            {
                if (_puppets[id] == null || _seen[id])
                {
                    continue;
                }

                Destroy(_puppets[id].gameObject);
                _puppets[id] = null;
                Debug.Log($"[NV] 플레이어 {id} 퇴장.");
            }
        }

        /// 서버와 같은 지형에서 시뮬레이션하고 있는지 확인한다.
        ///
        /// 해시가 다르면 서버는 export 된 예전 맵으로 판정하고 클라이언트는 새 씨드로 만든
        /// 지형을 그린다. 증상은 "특정 위치에서만 캐릭터가 튐" 으로만 나타나 추적이 어렵다.
        /// 그래서 조용히 넘기지 않고 에러로 남기고, 세션에도 알려 화면에 뜨게 한다.
        private void OnWelcome()
        {
            if (_mapHashChecked || _map == null)
            {
                return;
            }

            _mapHashChecked = true;

            // `Start` 에서 이미 계산했다. 그때 레벨이 없었던 경우만 여기서 채운다 —
            // 접속 프레임에 수백만 회 AABB 검사를 얹지 않는 것이 이 순서의 요점이다.
            if (_clientMapHash == 0u)
            {
                PrecomputeMapHash();
            }

            if (_clientMapHash == _client.ServerMapHash)
            {
                _mapHashStatus = "일치 (" + _map.MapName + ")";
                return;
            }

            _mapHashStatus = "불일치";

            var detail = $"서버 {_client.ServerMapHash:X8}, 클라이언트 {_clientMapHash:X8} ({_map.MapName})";

            Debug.LogError(
                $"[NV] 맵 해시 불일치. {detail}. " +
                "룸의 맵에 맞는 씬을 열었는지, 씨드를 바꾼 뒤 " +
                "Tools ▸ NV ▸ Map ▸ Export Map Collision 을 돌렸는지 확인한다.");

            if (NetSession.Exists)
            {
                NetSession.Current.ReportMapHashMismatch(detail);
            }
        }

        private static FirstPersonController FindLocalPlayer()
        {
            var players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
            for (var index = 0; index < players.Length; index++)
            {
                if (players[index].controlMode != FirstPersonController.ControlMode.Remote)
                {
                    return players[index];
                }
            }

            return null;
        }

        private void OnGUI()
        {
            if (!showOverlay || _client == null)
            {
                return;
            }

            var entities = _client.Snapshots != null ? _client.Snapshots.LatestEntityCount : 0;

            var text =
                $"NV  {_client.State}  {_client.Phase}\n" +
                $"플레이어 {_client.LocalPlayerId}   엔티티 {entities}\n" +
                $"서버 틱 {(_client.Snapshots != null ? _client.Snapshots.LatestTick : 0u)}   입력 지연 {_client.InputLag}틱\n" +
                $"맵 해시 {_mapHashStatus}";

            if (!string.IsNullOrEmpty(_client.LastError))
            {
                text += "\n" + _client.LastError;
            }

            GUI.Label(new Rect(12f, 12f, 460f, 96f), text);
        }
    }

    /// 로컬 입력을 와이어 포맷으로 바꾼다.
    ///
    /// 위치를 절대 싣지 않는다. 싣는 순간 클라이언트 권위가 된다.
    internal sealed class LocalInputSource : IInputSource
    {
        private readonly FirstPersonController _controller;

        public LocalInputSource(FirstPersonController controller)
        {
            _controller = controller;
        }

        public InputFrame Sample()
        {
            var buttons = ButtonFlags.None;

            // 점프는 눌린 프레임에 래치되고 여기서 소비된다. 30Hz 틱 사이에 눌린 점프를
            // 그냥 읽으면 절반쯤 사라진다.
            if (_controller.ConsumeJump()) buttons |= ButtonFlags.Jump;
            if (_controller.SprintHeld) buttons |= ButtonFlags.Sprint;
            if (_controller.FireHeld) buttons |= ButtonFlags.Fire;

            // 상호작용도 래치를 소비한다. **대상은 싣지 않는다** — 서버가 자기 좌표로
            // 무엇에 대한 상호작용인지 고른다. 지금 이 비트를 쓰는 판정은 아직 없고
            // (열쇠 삽입은 IG-012b2, 장치는 IG-013) 서버는 받아서 버린다.
            if (_controller.ConsumeInteract()) buttons |= ButtonFlags.Interact;

            var move = _controller.MoveInput;

            return new InputFrame(
                buttons,
                Quantization.ToFixedMoveAxis(move.x),
                Quantization.ToFixedMoveAxis(move.y),
                Quantization.ToFixedYaw(_controller.Yaw * Mathf.Deg2Rad),
                Quantization.ToFixedPitch(_controller.Pitch * Mathf.Deg2Rad));
        }
    }
}
