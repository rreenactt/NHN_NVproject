using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// The torch every player carries, now that the halls are dark enough to need one.
    ///
    /// **The beam is aimed by the look, not by the arm that holds it.** The prop hangs off the left
    /// arm and swings with the walk cycle, which is what makes a torch approaching down a corridor
    /// read as a person; but a beam that swung with it would be unusable to the person holding it.
    /// So the prop is parented and the light is not — the light is placed every LateUpdate from the
    /// body's yaw and the controller's pitch.
    ///
    /// **One formula serves local and remote bodies.** A remote puppet has no camera transform to
    /// read a forward vector off (`RemotePlayerPuppet` leaves `head` null because the rig's joints
    /// are built in an `Awake` that has not run yet), so nothing here reads one. Yaw comes from the
    /// body, pitch from <see cref="FirstPersonController.Pitch"/>, and both are filled on a puppet
    /// by `ApplyRemoteLook`.
    ///
    /// **Shadows are on, and that is not decoration.** Without them a torch lights the room on the
    /// far side of the wall it is pointed at, and in a maze whose whole tension is not knowing what
    /// is around the corner, that gives the corner away. It costs one shadow map per living player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Flashlight : MonoBehaviour
    {
        private const float Range = 20f;
        private const float SpotAngle = 64f;
        private const float InnerAngle = 22f;
        private const float Intensity = 4.2f;

        /// 램프가 눈에서 떨어져 있는 거리. **앞으로 나와 있는 것이 중요하다** — 눈 뒤에 두면
        /// 자기 몸이 광원과 벽 사이에 들어가 자기 그림자를 정면으로 보게 된다.
        private static readonly Vector3 LampOffset = new Vector3(-0.20f, -0.12f, 0.30f);

        private static Material _casingMaterial;
        private static Material _lensMaterial;

        private PlayerAgent _agent;
        private FirstPersonController _controller;
        private BlockRig _rig;

        private Light _beam;
        private Transform _bodyProp;
        private Transform _viewProp;
        private MeshRenderer _bodyLens;
        private MeshRenderer _viewLens;

        private void Awake()
        {
            _agent = GetComponent<PlayerAgent>();
            _controller = GetComponent<FirstPersonController>();
            _rig = GetComponent<BlockRig>();
        }

        private void LateUpdate()
        {
            if (_rig == null) _rig = GetComponent<BlockRig>();

            // **술래는 손전등을 들지 않는다.** 어둠에서 보는 것이 그쪽의 능력이고
            // (`RoleVision`), 손전등을 함께 주면 그 능력이 값을 잃는다. 역할은 공개 시점에
            // 오므로 몸이 만들어질 때가 아니라 여기서 판정한다 — 그때 손에 든 것을 치운다.
            if (_agent != null && _agent.Role == Role.Seeker)
            {
                Douse();
                return;
            }

            EnsureProps();
            EnsureBeam();

            if (_beam == null) return;

            // 매치에서 빠진 몸은 불을 끈다. 시체 자리에서 계속 비추면 술래에게 "여기 누가
            // 있다" 를 영원히 알려 주는 표지가 된다.
            bool lit = _agent == null || _agent.InPlay;

            _beam.enabled = lit;
            if (_bodyLens != null) _bodyLens.enabled = lit;
            if (_viewLens != null) _viewLens.enabled = lit;

            if (!lit) return;

            float yaw = transform.eulerAngles.y;
            float pitch = _controller != null ? _controller.Pitch : 0f;
            Quaternion look = Quaternion.Euler(pitch, yaw, 0f);

            _beam.transform.SetPositionAndRotation(Origin(look), look);
        }

        /// 불을 끄고 손에서 치운다. 역할이 다시 바뀌면 <see cref="EnsureProps"/> 가 다시 만든다.
        private void Douse()
        {
            if (_beam != null) _beam.enabled = false;

            if (_bodyProp != null) Destroy(_bodyProp.gameObject);
            if (_viewProp != null) Destroy(_viewProp.gameObject);

            _bodyProp = _viewProp = null;
            _bodyLens = _viewLens = null;
        }

        /// 빔이 나오는 자리. **들고 있는 손이다** — 눈에서 쏘면 남이 볼 때 얼굴에서 빛이
        /// 나오고, 손에 든 물건과 빔이 따로 논다. 걸음에 따라 팔이 흔들리므로 출발점이
        /// 조금씩 움직이는데, 방향은 시선이 잡고 있으므로 빛 웅덩이가 살짝 미끄러질 뿐이다.
        ///
        /// 앞으로 조금 밀어낸다. 팔이 뒤로 갔을 때 광원이 몸통 뒤에 놓이면, 그림자를 켜 둔
        /// 이상 **자기 몸의 그림자를 정면으로 보게 된다.**
        ///
        /// 팔이 없는 몸(괴물 플랜이 왼팔을 다르게 만들 수 있다)은 눈높이로 돌아간다.
        private Vector3 Origin(Quaternion look)
        {
            Vector3 ahead = look * Vector3.forward;

            if (_bodyProp != null) return _bodyProp.position + (ahead * 0.22f);

            Vector3 eye = _agent != null ? _agent.HeadPosition : transform.position + Vector3.up * 1.6f;
            return eye + (look * LampOffset);
        }

        private void EnsureBeam()
        {
            if (_beam != null) return;

            var go = new GameObject("Flashlight Beam");

            // 몸에 매단다 — 위치는 매 프레임 직접 잡지만, 이 몸이 사라질 때 같이 사라져야 한다.
            go.transform.SetParent(transform, false);

            _beam = go.AddComponent<Light>();
            _beam.type = LightType.Spot;
            _beam.range = Range;
            _beam.spotAngle = SpotAngle;
            _beam.innerSpotAngle = InnerAngle;
            _beam.intensity = Intensity;
            _beam.color = new Color(0.93f, 0.95f, 1f);
            _beam.shadows = LightShadows.Hard;
            _beam.shadowStrength = 0.9f;

            // **그림자의 근평면을 램프 앞으로 민다.** 광원이 들고 있는 팔 안에 있으므로,
            // 이것이 없으면 그 팔이 자기 빔을 가려 빛 웅덩이 한가운데에 검은 덩어리가 생긴다.
            _beam.shadowNearPlane = 0.4f;
        }

        /// 손에 들린 물건. 리그가 다시 만들어지면(역할 공개의 괴물 몸 교체) 부모가 사라지므로
        /// 매 프레임 확인한다 — 비교 한 번이고, 놓치면 손전등이 허공에 남는다.
        private void EnsureProps()
        {
            if (_rig == null) return;

            if (_bodyProp == null || _bodyProp.parent != _rig.ArmL)
            {
                if (_bodyProp != null) Destroy(_bodyProp.gameObject);
                _bodyProp = BuildProp(_rig.ArmL, _rig.bodyLayer);
                _bodyLens = LensOf(_bodyProp);
            }

            if (_rig.ViewArmL != null && (_viewProp == null || _viewProp.parent != _rig.ViewArmL))
            {
                if (_viewProp != null) Destroy(_viewProp.gameObject);
                _viewProp = BuildProp(_rig.ViewArmL, _rig.armsLayer);
                _viewLens = LensOf(_viewProp);
            }
        }

        /// 팔 끝에 손전등을 놓는다. 팔의 피벗은 어깨이고 길이는 <see cref="BlockRig.ArmLength"/>
        /// 이므로, 손은 그만큼 아래다 — <c>HandR</c> 이 오른팔에서 쓰는 것과 같은 계산이다.
        private Transform BuildProp(Transform arm, int layer)
        {
            if (arm == null) return null;

            var root = new GameObject("Flashlight").transform;
            root.SetParent(arm, false);
            root.localPosition = new Vector3(0f, -_rig.ArmLength, 0f);
            root.localRotation = Quaternion.identity;
            root.gameObject.layer = layer;

            AddBlock(root, "Casing", new Vector3(0f, 0f, 0.05f), new Vector3(0.07f, 0.07f, 0.20f), layer, Casing);

            // 렌즈. 어두운 복도에서 **광원 자체가 보이는 것**이 빔만큼 중요하다 — 빔이 다른
            // 쪽을 향해 있어도 그 사람이 어디 있는지 점 하나로 읽힌다.
            AddBlock(root, "Lens", new Vector3(0f, 0f, 0.16f), new Vector3(0.055f, 0.055f, 0.02f), layer, Lens);

            return root;
        }

        private static MeshRenderer LensOf(Transform prop)
        {
            if (prop == null) return null;

            Transform lens = prop.Find("Lens");
            return lens != null ? lens.GetComponent<MeshRenderer>() : null;
        }

        private static void AddBlock(Transform parent, string name, Vector3 centre, Vector3 size, int layer, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.layer = layer;

            // 몸의 블록과 같은 규칙이다 — 캐릭터에 붙은 것은 콜라이더를 갖지 않는다.
            // 남겨 두면 자기 총알이 자기 손전등에 맞는다.
            var collider = cube.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var view = cube.GetComponent<MeshRenderer>();
            view.sharedMaterial = material;

            // 손전등 자신은 그림자를 만들지 않는다. 광원과 같은 자리에 있는 물건이라
            // 무엇을 가리든 그것은 빔 전체다.
            view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Transform t = cube.transform;
            t.SetParent(parent, false);
            t.localPosition = centre;
            t.localScale = size;
        }

        private static Material Casing =>
            _casingMaterial != null ? _casingMaterial : _casingMaterial = Build("Flashlight Casing", new Color(0.14f, 0.14f, 0.15f), false);

        private static Material Lens =>
            _lensMaterial != null ? _lensMaterial : _lensMaterial = Build("Flashlight Lens", new Color(1f, 0.97f, 0.88f), true);

        /// **정적으로 하나만 만든다.** 플레이어마다 만들면 매치마다 머티리얼이 쌓이고,
        /// 그것은 씬을 바꿔도 회수되지 않는다.
        private static Material Build(string name, Color colour, bool glowing)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            material.color = colour;

            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", glowing ? 0.4f : 0.2f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

            if (glowing)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", colour * 3.4f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            material.enableInstancing = true;
            return material;
        }
    }
}
