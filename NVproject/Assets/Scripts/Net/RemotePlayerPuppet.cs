using NV.Game;
using UnityEngine;

namespace NV.Client.Net
{
    /// 다른 플레이어의 몸. 로컬 플레이어와 같은 블록 리그와 같은 애니메이터를 쓰고,
    /// 다른 것은 움직임의 출처뿐이다 — 입력이 아니라 스냅샷이 이 몸을 움직인다.
    ///
    /// 애니메이터를 원격용으로 따로 만들지 않는다. 걸음걸이가 실제 변위에서 유도되므로,
    /// 트랜스폼만 옮겨 주면 보폭·팔 스윙·기울기가 전부 따라온다. 그 성질이 없다면
    /// 원격 플레이어의 애니메이션을 위해 이동 상태를 와이어에 더 실어야 했을 것이다.
    public sealed class RemotePlayerPuppet : MonoBehaviour
    {
        /// 로컬 카메라가 자기 몸을 감추기 위해 PlayerBody(9) 를 컬링한다.
        /// 원격 플레이어를 같은 레이어에 두면 아무도 서로를 볼 수 없게 된다.
        /// 증상은 "접속은 되는데 상대가 안 보임" 하나로만 나타난다.
        public const int RemoteBodyLayer = 0;

        public byte PlayerId { get; private set; }

        public FirstPersonController Controller { get; private set; }

        /// 매치 레이어에서 이 몸에 해당하는 참가자. 매치 레이어가 없는 씬에서는 null 이다.
        public PlayerAgent Agent { get; private set; }

        /// 이 몸에 이미 반영한 역할과, 한 번이라도 반영했는가.
        ///
        /// **플래그가 따로 있어야 한다.** 역할의 초기값도 `Unassigned` 라, 값만 비교하면
        /// "아직 역할이 없다" 와 "역할이 없다고 이미 반영했다" 가 구별되지 않는다 — 첫 적용이
        /// 통째로 건너뛰어지고 몸은 리그의 기본값(무장)을 든 채로 남는다.
        private Role _appliedRole;
        private bool _roleApplied;

        /// <summary>
        /// 서버가 정한 역할을 몸에 입힌다 — 지금은 **총을 들었는가** 하나다.
        ///
        /// **폴링이다.** 역할은 `MatchManager.RolesAssigned` 로 한 번 오지만, 원격 몸은 첫
        /// 스냅샷이 와야 생기므로 그 이벤트보다 늦게 태어나는 몸이 있다 — 늦게 온 사람은
        /// 그 한 번을 영영 놓친다. 명단은 계속 오므로 매 프레임 확인하는 편이 싸고 안전하다.
        /// 바뀌었을 때만 적용하므로 `SetActive` 가 프레임마다 돌지는 않는다.
        ///
        /// 이것이 없던 동안 원격 몸은 리그의 기본값(무장)을 그대로 들고 있었다. 그래서
        /// **Runner 도 권총을 들고 서 있었고**, 안개 너머로 술래를 알아보는 단서가 사라졌다.
        /// 역할은 이미 클라이언트가 알고 있었다(`RoomState.SeekerPlayerId`) — 몸에 옮기는
        /// 곳만 없었다.
        /// </summary>
        private void Update()
        {
            // 매치 레이어가 없는 씬(`MultiplayerTest`)에는 역할이라는 것이 없다. 그 씬의 몸은
            // 만들어진 그대로 둔다 — 규칙이 없는 곳에서 무장 여부를 정할 근거가 없다.
            if (Agent == null || (_roleApplied && Agent.Role == _appliedRole))
            {
                return;
            }

            _roleApplied = true;
            _appliedRole = Agent.Role;

            // 역할이 정해지기 전에는 아무도 무장하지 않는다. 로비에서 총을 든 채로 서 있는
            // 몸이 없어야 하고, 그것이 `PlayerRoleLoadout` 의 기본값과도 같다.
            PlayerRoleLoadout.ShowWeapon(gameObject, _appliedRole == Role.Seeker);
        }

        /// 몸을 만든다. 컴포넌트의 Awake 가 필드보다 먼저 도는 것을 막기 위해
        /// 비활성 상태로 조립한 뒤 마지막에 활성화한다. 이 순서를 어기면
        /// FirstPersonController 가 자기를 로컬 플레이어로 알고 메인 카메라를 붙잡는다.
        public static RemotePlayerPuppet Create(byte playerId, string displayName, Transform parent, Material blockMaterial)
        {
            var go = new GameObject($"Remote Player {playerId}");
            go.SetActive(false);
            go.transform.SetParent(parent, false);

            // 서버 히트박스와 같은 치수. 이 콜라이더는 이동에 쓰이지 않지만
            // BlockRig 가 발밑 보정을 skinWidth 에서 읽으므로 존재해야 한다.
            var body = go.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.4f;
            body.center = new Vector3(0f, 0.9f, 0f);
            // 서버는 플레이어끼리의 충돌을 계산하지 않는다. 켜 두면 로컬 플레이어가
            // 원격 몸에 막혀 서버 위치와 어긋난다.
            body.enabled = false;

            var rig = go.AddComponent<BlockRig>();
            rig.bodyLayer = RemoteBodyLayer;
            rig.cameraTransform = null;      // 뷰모델 팔은 자기 화면에만 있는 것이다
            rig.blockMaterial = blockMaterial;

            var controller = go.AddComponent<FirstPersonController>();
            controller.controlMode = FirstPersonController.ControlMode.Remote;
            controller.cameraTransform = null;

            var animator = go.AddComponent<BlockCharacterAnimator>();
            animator.rig = rig;
            animator.controller = controller;
            animator.Armed = true;

            var puppet = go.AddComponent<RemotePlayerPuppet>();
            puppet.PlayerId = playerId;
            puppet.Controller = controller;

            // 매치 레이어가 있는 씬에서만 참가자로 만든다. MultiplayerTest 처럼 규칙
            // 레이어가 없는 씬에서는 몸만 필요하다.
            //
            // PlayerAgent 는 OnEnable 에서 스스로 명단에 등록한다. 아래의 SetActive 가
            // 그 시점이며, 그래서 역할과 이름을 그 전에 채워야 한다.
            if (MatchManager.Instance != null)
            {
                var agent = go.AddComponent<PlayerAgent>();
                agent.isLocalPlayer = false;
                agent.displayName = string.IsNullOrEmpty(displayName) ? "플레이어 " + playerId : displayName;
                agent.controller = controller;

                // head 는 비워 둔다. 리그의 관절은 Awake 에서 만들어지고 그 Awake 는 아래
                // SetActive 에서야 돌기 때문에 지금은 아직 없다. PlayerAgent 는 비어 있으면
                // 트랜스폼 + 1.6m 를 쓰는데, 그것이 바로 이 리그의 눈높이다.

                // 원격 플레이어의 발소리는 위치를 갖는다. 이 게임에서 발소리는 Seeker 의
                // 주 감각이고 Runner 의 주 누출이므로, 이것이 빠지면 술래잡기의 절반이 없다.
                var steps = go.AddComponent<FootstepAudio>();
                steps.isLocalListener = false;

                var shots = go.AddComponent<WeaponAudio>();
                shots.isLocalListener = false;

                // 남이 끌려가는 것도 보여야 한다. 이 몸에는 무기가 없으므로 빈 탄창으로
                // 걸리는 일이 없고, 서버의 `Frozen` 비트를 `MatchSync` 가 넘겨 준다.
                // 몸은 스냅샷이 옮기고 이 컴포넌트는 사슬만 그린다.
                go.AddComponent<ChainDrag>();

                // 체인이 풀릴 때 팔이 재장전 동작을 한다. `BlockCharacterAnimator` 가
                // `GetComponent` 로 집어 가므로 붙이는 것만으로 팔에 반영된다.
                go.AddComponent<ProceduralReload>();

                puppet.Agent = agent;
            }

            go.SetActive(true);
            return puppet;
        }

        /// 보간된 스냅샷 하나를 몸에 적용한다.
        public void Apply(in EntitySample sample)
        {
            Controller.ApplyNetworkState(sample.Position, sample.IsGrounded, sample.Velocity.y);
            Controller.ApplyRemoteLook(sample.YawDegrees, sample.PitchDegrees);
        }
    }
}
