using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using UnityEngine;

namespace NV.Client.Net
{
    /// 씬과 네트워크를 잇는 유일한 지점. 이 컴포넌트가 씬에 없으면 프로젝트는
    /// 종전과 똑같이 혼자 돌아간다 — 연동은 순수하게 얹는 기능이다.
    ///
    /// 하는 일은 셋이다. 로컬 플레이어를 서버 권위 모드로 돌리고, 입력을 30Hz 로 보내고,
    /// 스냅샷을 몸에 적용한다. 로컬 플레이어와 원격 플레이어의 적용 방식이 다르다.
    ///
    /// - 원격: 100ms 보간 버퍼. 30Hz 스냅샷을 렌더 프레임레이트로 펴는 표준 수단이다.
    /// - 로컬: 최신 스냅샷으로 짧게 감쇠. 자기 캐릭터에 보간 지연을 얹으면 왕복 지연
    ///   위에 100ms 가 더 붙어 조작이 무거워진다. 예측(M5)이 들어오면 이 경로가 대체된다.
    ///
    /// 실행 순서를 NetworkClient(-100) 뒤, FirstPersonController(0) 앞에 둔다.
    /// 트랜스폼을 옮긴 다음 컨트롤러가 변위를 재야 걸음 속도가 한 프레임 늦지 않는다.
    [DefaultExecutionOrder(-90)]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [Header("Server")]
        [Tooltip("host:port. 로컬 개발 서버는 dotnet run --project Api 의 5202 포트다.")]
        public string host = "localhost:5202";

        [Tooltip("룸 id. 비우면 서버 기본 룸에 들어간다.")]
        public string room = "";

        [Tooltip("배포 환경에서는 반드시 켠다. HTTPS 페이지의 ws:// 는 mixed content 로 차단되고, " +
                 "증상은 접속 실패 로그 하나로만 나타난다.")]
        public bool secure = false;

        public bool connectOnStart = true;

        [Header("Smoothing")]
        [Tooltip("원격 플레이어 보간 버퍼 길이(초). 고정 파라미터는 100ms 다. " +
                 "줄이면 손실 한 번에 끊김이 보이고, 늘리면 상대가 더 과거에 보인다.")]
        public float interpolationDelay = 0.1f;

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

        public NetworkClient Client => _client;

        /// 로컬 플레이어. UI 가 커서와 입력을 넘겨받을 때 쓴다.
        public FirstPersonController LocalPlayer => _localPlayer;

        /// 클라이언트가 계산한 맵 해시. Welcome 을 받은 뒤에만 값이 있다.
        public uint ClientMapHash => _clientMapHash;

        public string MapHashStatus => _mapHashStatus;

        public string MapName => _map != null ? _map.MapName : "(맵 없음)";

        /// UI 가 부르는 접속. 인자를 받는 이유는 UI 에서 주소를 고칠 수 있어야 하기 때문이다.
        public void Connect(string targetHost, string targetRoom)
        {
            if (_client == null)
            {
                return;
            }

            host = targetHost;
            room = targetRoom;
            _client.Connect(host, room, secure, interpolationDelay);
        }

        public void Disconnect()
        {
            if (_client != null)
            {
                _client.Disconnect();
            }
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

            _client = gameObject.AddComponent<NetworkClient>();
            _client.InputSource = new LocalInputSource(_localPlayer);
            _client.WelcomeReceived += OnWelcome;
            _client.Ended += OnEnded;

            if (connectOnStart)
            {
                _client.Connect(host, room, secure, interpolationDelay);
            }
        }

        private void Update()
        {
            if (_client == null || _client.State != ConnectionState.Playing || _client.Snapshots == null)
            {
                return;
            }

            ApplyLocal();
            ApplyRemotes();
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

            if (_localPlayer != null)
            {
                _localPlayer.controlMode = FirstPersonController.ControlMode.Local;
                _localPlayer.walkSpeed = _offlineWalkSpeed;
                _localPlayer.sprintSpeed = _offlineSprintSpeed;
            }
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
        /// 그래서 조용히 넘기지 않고 에러로 남긴다.
        private void OnWelcome()
        {
            // 서버가 슬롯을 준 시점에서야 권위를 넘긴다.
            if (_localPlayer != null)
            {
                _localPlayer.controlMode = FirstPersonController.ControlMode.NetworkAuthority;

                // 걸음걸이와 기울기는 컨트롤러의 속도 파라미터를 기준으로 스케일된다.
                // 이동은 이제 서버가 하므로, 그 기준을 서버 상수로 맞춰야 보폭이 맞는다.
                _localPlayer.walkSpeed = SimConstants.MoveSpeed;
                _localPlayer.sprintSpeed = SimConstants.MoveSpeed * SimConstants.SprintMultiplier;
            }

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
            Debug.LogError(
                $"[NV] 맵 해시 불일치. 서버 {_client.ServerMapHash:X8}, " +
                $"클라이언트 {_clientMapHash:X8} ({_map.MapName}). 룸 '{room}'. " +
                $"서버 설정의 Game:Maps:{(string.IsNullOrEmpty(room) ? "default" : room)} 가 " +
                $"{_map.MapName}.json 을 가리키는지, 씨드를 바꾼 뒤 " +
                "Tools ▸ NV Network ▸ Export Map Collision 을 돌렸는지 확인한다.");
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

            var state = _client.State.ToString();
            var entities = _client.Snapshots != null ? _client.Snapshots.LatestEntityCount : 0;

            var text =
                $"NV  {state}  {host}\n" +
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
