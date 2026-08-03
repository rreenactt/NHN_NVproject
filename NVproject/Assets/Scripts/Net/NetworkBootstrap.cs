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

            Bind();
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
        }

        private void OnDestroy()
        {
            if (_client == null)
            {
                return;
            }

            _client.WelcomeReceived -= OnWelcome;
            _client.Ended -= OnEnded;
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
                    _puppets[id] = RemotePlayerPuppet.Create(id, null, null);
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
            _clientMapHash = MapExport.BuildMapData(_map).ComputeHash();

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
                "Tools ▸ NV Network ▸ Export Map Collision 을 돌렸는지 확인한다.");

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
