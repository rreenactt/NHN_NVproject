using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NV.Game
{
    /// <summary>
    /// Where a dead Runner's eyes go: into a teammate's head, with a click to move to the next one.
    ///
    /// **The problem this closes is not "there is nothing to do".** A Runner who is out keeps a body
    /// on the server (the wipe check has to count it), so before this they sat at the spot they died
    /// on, invisible, able to look around — an accidental ghost nobody designed. Watching a teammate
    /// is a decision about what that state *is*.
    ///
    /// **Runners only, never the Seeker.** Seeing what the Seeker sees is what the security-camera
    /// device costs a walk across the level to buy (`SeekerFeed`); handing it to every corpse for
    /// free empties that device. It also stops being harmless the day §7 proximity voice lands and
    /// the dead can talk.
    ///
    /// **A second camera, not the player's own.** The local camera hangs off the rig's head and
    /// <see cref="FirstPersonController"/> rewrites its local rotation every LateUpdate; anything
    /// parked on top of that fights a script whose execution order is not ours to fix. A camera of
    /// our own has no owner to argue with. It follows the same recipe as <see cref="UI.SeekerFeed"/>,
    /// which has been rendering somebody else's viewpoint all along.
    /// </summary>
    public sealed class MatchSpectator
    {
        /// The viewmodel arms belong to whoever is holding them, and that is never the target.
        private const int ViewmodelArmsLayer = 8;

        /// The dead player's own body. Their camera already culls it; so does this one, or they fly
        /// through their own corpse standing where they left it.
        private const int OwnBodyLayer = 9;

        private Camera _camera;
        private AudioListener _ear;

        /// The camera and ear being borrowed from. Held so they can be handed back exactly.
        private Camera _own;
        private AudioListener _ownEar;

        private PlayerAgent _target;
        private readonly List<PlayerAgent> _targets = new List<PlayerAgent>();

        /// <summary>Who is being watched, or null when nothing is.</summary>
        public PlayerAgent Target => _target;

        public bool Watching => _camera != null && _camera.enabled;

        /// <summary>
        /// Decides whether to watch and who. Call from Update — picking a target is a frame-rate
        /// question, following one is not (see <see cref="LateTick"/>).
        /// </summary>
        /// <param name="own">The local player's camera, switched off while someone else's is used.</param>
        public void Tick(PlayerAgent local, IReadOnlyList<PlayerAgent> agents, Camera own)
        {
            // 관전은 죽은 **Runner** 의 것이다. 술래는 총에 맞지 않으므로 정상 경로에서는
            // 여기 오지 않지만, 오게 되면 Runner 의 시야 규칙이 술래에게 열린다.
            bool eligible = local != null
                            && local.Role == Role.Runner
                            && !local.InPlay;

            if (!eligible)
            {
                Stop();
                return;
            }

            Collect(local, agents);

            if (_targets.Count == 0)
            {
                // 볼 사람이 없다. 전멸이 확정된 순간이고 매치는 곧 끝난다 — 자기 자리에
                // 남겨 둔다. 카메라를 꺼 버리면 화면이 검게 죽는다.
                Stop();
                return;
            }

            // 보던 사람이 죽거나 빠져나갔으면 다음 사람으로 넘어간다. 여기서 멈추면 관전이
            // 조용히 끝나고, 그 화면은 "게임이 멈췄다" 로 읽힌다.
            if (_target == null || !_targets.Contains(_target))
            {
                _target = _targets[0];
            }

            Step(ReadSwitch());

            Begin(own);
        }

        /// <summary>
        /// Moves the camera onto the target. **LateUpdate only** — remote bodies are placed in
        /// Update, so following them in Update renders a frame behind and the view jitters against
        /// its own subject.
        /// </summary>
        public void LateTick()
        {
            if (!Watching || _target == null) return;

            // 눈높이는 `HeadPosition` 이 안다. **원격 몸의 `head` 는 비어 있다** — 리그의
            // 관절이 생기기 전에 컴포넌트가 붙기 때문이고(`RemotePlayerPuppet`), 그래서
            // 트랜스폼 + 1.6m 로 떨어진다. 그것이 이 리그의 눈높이다.
            Vector3 eye = _target.HeadPosition;

            // 시선은 몸의 요와 컨트롤러의 피치를 합쳐 만든다. 원격 몸에는 피치를 담아 둘
            // 카메라 트랜스폼이 없으므로(같은 이유) 각도를 직접 읽는다.
            float yaw = _target.transform.eulerAngles.y;
            float pitch = _target.controller != null ? _target.controller.Pitch : 0f;

            _camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(pitch, yaw, 0f));
        }

        /// <summary>Hands the camera and the ear back. Safe to call when not watching.</summary>
        public void Stop()
        {
            if (_camera != null) _camera.enabled = false;
            if (_ear != null) _ear.enabled = false;

            if (_own != null) _own.enabled = true;
            if (_ownEar != null) _ownEar.enabled = true;

            _own = null;
            _ownEar = null;
            _target = null;
        }

        public void Release()
        {
            Stop();

            if (_camera != null) Object.Destroy(_camera.gameObject);
            _camera = null;
            _ear = null;
        }

        /// 지금 볼 수 있는 사람들. **이름순으로 고정한다** — 등록 순서는 몸이 도착한 순서라
        /// 사람마다 다르고, 한 번 넘길 때마다 목록이 흔들리면 "다음 사람" 이 무엇인지 배울
        /// 수 없다.
        private void Collect(PlayerAgent local, IReadOnlyList<PlayerAgent> agents)
        {
            _targets.Clear();

            if (agents == null) return;

            for (int i = 0; i < agents.Count; i++)
            {
                PlayerAgent agent = agents[i];

                if (agent == null || agent == local) continue;
                if (agent.Role != Role.Runner || !agent.InPlay) continue;

                _targets.Add(agent);
            }

            _targets.Sort(static (a, b) => string.CompareOrdinal(a.displayName, b.displayName));
        }

        /// <returns>+1 for the next teammate, -1 for the previous, 0 for stay.</returns>
        private static int ReadSwitch()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return 0;

            if (mouse.leftButton.wasPressedThisFrame) return 1;
            if (mouse.rightButton.wasPressedThisFrame) return -1;

            return 0;
        }

        private void Step(int direction)
        {
            if (direction == 0) return;

            int at = _targets.IndexOf(_target);
            int count = _targets.Count;

            _target = _targets[((at + direction) % count + count) % count];
        }

        /// 카메라를 빌린다. 이미 보고 있으면 아무것도 하지 않는다.
        private void Begin(Camera own)
        {
            Ensure(own);

            if (_camera == null) return;

            if (_own == null && own != null)
            {
                _own = own;
                _ownEar = own.GetComponent<AudioListener>();

                // **끄는 것이 먼저다.** 귀가 둘 켜져 있는 프레임이 있으면 Unity 가 경고를
                // 뱉고 어느 쪽이 들리는지는 정해져 있지 않다.
                _own.enabled = false;
                if (_ownEar != null) _ownEar.enabled = false;
            }

            _camera.enabled = true;
            if (_ear != null) _ear.enabled = true;
        }

        private void Ensure(Camera own)
        {
            if (_camera != null || own == null) return;

            var go = new GameObject("Spectator Camera");

            _camera = go.AddComponent<Camera>();
            _camera.fieldOfView = own.fieldOfView;
            _camera.nearClipPlane = own.nearClipPlane;
            _camera.farClipPlane = own.farClipPlane;
            _camera.clearFlags = own.clearFlags;
            _camera.backgroundColor = own.backgroundColor;
            _camera.depth = own.depth;

            // 시야 규칙은 빌려 온 카메라의 것을 그대로 쓴다 — 관전자는 죽었어도 Runner 이고,
            // 문이 보이고 남의 피는 보이지 않는 것이 그 역할의 시야다. 여기서 마스크를 새로
            // 조립하면 그 규칙이 두 곳에 생긴다.
            _camera.cullingMask = own.cullingMask & ~(1 << ViewmodelArmsLayer) & ~(1 << OwnBodyLayer);

            _ear = go.AddComponent<AudioListener>();
            _ear.enabled = false;
        }
    }
}
