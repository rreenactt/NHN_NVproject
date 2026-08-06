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
