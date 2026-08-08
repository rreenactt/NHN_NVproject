using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// What a block body does when it stops being a body: it comes apart, and the pieces tumble.
    ///
    /// **The pieces are copies, not the rig.** Detaching the real blocks would fight three things at
    /// once — <see cref="BlockCharacterAnimator"/> poses those joints every LateUpdate,
    /// <c>PlayerAgent.SetPresent</c> hides the body through a cached renderer array that would still
    /// hold them, and the next match needs the body back. Cloning costs eight meshes for a few
    /// seconds and leaves the rig untouched, so the corpse can be hidden the way it always was.
    ///
    /// **Integrated by hand, not by a Rigidbody.** These are debris on one machine — nothing about
    /// them is authoritative and nothing may touch a player. A Rigidbody brings colliders, and
    /// colliders here would shove the local `CharacterController` around and disagree with the
    /// server's idea of where that player is. `Bullet` avoids physics for the same reason; this is
    /// the same rule applied to something that only has to look right.
    ///
    /// The tumble is seeded from the body's name, so two people watching the same death watch the
    /// same pieces land in the same places.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockGibs : MonoBehaviour
    {
        /// 조각이 흩어지는 속도(m/s). 위로 뜨는 성분은 따로 있다.
        private const float BurstSpeed = 3.4f;

        /// 터질 때 위로 뜨는 성분(m/s). 이것이 없으면 조각이 바닥을 기어가듯 미끄러진다.
        private const float BurstLift = 2.9f;

        /// 조각이 도는 속도의 상한(도/초). 크게 잡는다 — 천천히 도는 파편은 무겁게 보인다.
        private const float SpinSpeed = 520f;

        private const float Gravity = 17f;

        /// 바닥에 닿았을 때 남는 속도의 비율. 블록은 튀기보다 굴러야 한다.
        private const float Bounce = 0.32f;

        /// 바닥과 스치는 속도가 매 번 깎이는 비율. 이것이 없으면 영원히 미끄러진다.
        private const float Friction = 0.72f;

        /// 이 속도 아래로 떨어지면 멈춘 것으로 본다(m/s).
        private const float RestSpeed = 0.55f;

        /// 조각이 남아 있는 시간(초). 시체가 오래 남으면 술래가 그 자리를 다시 확인하러 온다.
        private const float Lifetime = 5f;

        /// 바닥으로 가라앉으며 사라지는 시간(초).
        private const float SinkSeconds = 0.7f;

        private const float SinkDepth = 1.2f;

        private struct Piece
        {
            public Transform transform;
            public Vector3 velocity;
            public Vector3 spin;
            public bool resting;
            public float halfHeight;
        }

        private Piece[] _pieces;
        private float _age;
        private LayerMask _groundMask;

        /// <summary>
        /// Blows a copy of this body apart at its current pose.
        ///
        /// Called before the real body is hidden, because the pose it is standing in *is* the
        /// starting pose of the burst — take it afterwards and every piece starts from the T-pose
        /// the rig was built in.
        /// </summary>
        public static void Burst(BlockRig rig, string seedName)
        {
            if (rig == null || rig.Hips == null) return;

            var renderers = rig.Hips.GetComponentsInChildren<MeshRenderer>(false);
            if (renderers.Length == 0) return;

            var root = new GameObject("Gibs");
            var gibs = root.AddComponent<BlockGibs>();
            gibs.Build(renderers, seedName);
        }

        private void Build(MeshRenderer[] renderers, string seedName)
        {
            // 이름에서 씨드를 뽑는다. 같은 죽음을 보는 두 사람이 같은 자리에 떨어진 조각을 본다 —
            // `Random` 을 그냥 쓰면 화면마다 다른 곳에 눕는다.
            var random = new System.Random(seedName != null ? seedName.GetHashCode() : 0);

            // 중심은 조각들의 평균이다. 관절 하나를 기준으로 잡으면(엉덩이 등) 머리가 늘 같은
            // 방향으로 날아가고, 터진 것이 아니라 밀린 것으로 보인다.
            var centre = Vector3.zero;
            for (int i = 0; i < renderers.Length; i++) centre += renderers[i].transform.position;
            centre /= renderers.Length;

            _groundMask = ~((1 << 8) | (1 << 9));   // 뷰모델 팔과 플레이어 몸은 바닥이 아니다
            _pieces = new Piece[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                Transform source = renderers[i].transform;

                var piece = new GameObject(source.name);
                piece.transform.SetParent(transform, false);
                piece.transform.SetPositionAndRotation(source.position, source.rotation);

                // 월드 크기를 그대로 옮긴다. 부모를 잃으므로 `localScale` 로는 모양이 달라진다.
                piece.transform.localScale = source.lossyScale;

                var filter = piece.AddComponent<MeshFilter>();
                filter.sharedMesh = renderers[i].GetComponent<MeshFilter>().sharedMesh;

                var view = piece.AddComponent<MeshRenderer>();
                view.sharedMaterial = renderers[i].sharedMaterial;
                view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // **레이어를 물려받지 않는다.** 로컬 플레이어의 몸은 `PlayerBody`(9) 에 있고
                // 자기 카메라가 그 레이어를 컬링한다 — 자기 몸을 보지 않기 위한 것이다.
                // 그대로 복제하면 **죽은 본인만 자기 파편을 못 본다.** 파편은 더 이상 그
                // 사람의 몸이 아니므로 모두가 보는 레이어에 둔다.
                piece.layer = 0;

                // 중심에서 바깥으로. 수평 성분만 쓰고 위로 뜨는 것은 따로 더한다 — 그러지 않으면
                // 머리처럼 높이 있던 조각만 위로, 다리는 아래로 처박힌다.
                Vector3 outward = source.position - centre;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-4f) outward = new Vector3(Next(random, -1f, 1f), 0f, Next(random, -1f, 1f));
                outward = outward.normalized;

                _pieces[i] = new Piece
                {
                    transform = piece.transform,
                    velocity = outward * (BurstSpeed * Next(random, 0.55f, 1.35f))
                               + Vector3.up * (BurstLift * Next(random, 0.6f, 1.3f)),
                    spin = new Vector3(Next(random, -1f, 1f), Next(random, -1f, 1f), Next(random, -1f, 1f))
                           .normalized * (SpinSpeed * Next(random, 0.4f, 1f)),
                    halfHeight = Mathf.Max(0.04f, source.lossyScale.y * 0.5f),
                };
            }
        }

        private static float Next(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private void Update()
        {
            _age += Time.deltaTime;

            if (_age >= Lifetime + SinkSeconds)
            {
                Destroy(gameObject);
                return;
            }

            // 수명이 지나면 바닥으로 가라앉는다. 그냥 지우면 눈앞에서 사라지고, 페이드는
            // 머티리얼을 투명하게 바꿔야 해서 조각마다 사본이 하나씩 더 생긴다.
            if (_age >= Lifetime)
            {
                float sunk = (_age - Lifetime) / SinkSeconds;
                transform.position = Vector3.down * (sunk * sunk * SinkDepth);
                return;
            }

            float delta = Time.deltaTime;

            for (int i = 0; i < _pieces.Length; i++)
            {
                ref Piece piece = ref _pieces[i];
                if (piece.transform == null || piece.resting) continue;

                piece.velocity.y -= Gravity * delta;

                Vector3 from = piece.transform.position;
                Vector3 step = piece.velocity * delta;

                // 내려가는 동안에만 바닥을 본다. 위로 가는 조각까지 검사하면 천장에 붙는다.
                if (step.y < 0f
                    && Physics.Raycast(from, Vector3.down, out RaycastHit hit,
                                       piece.halfHeight - step.y, _groundMask, QueryTriggerInteraction.Ignore))
                {
                    piece.transform.position = new Vector3(from.x + step.x, hit.point.y + piece.halfHeight, from.z + step.z);

                    piece.velocity.y = -piece.velocity.y * Bounce;
                    piece.velocity.x *= Friction;
                    piece.velocity.z *= Friction;
                    piece.spin *= Friction;

                    // 다 구른 조각은 계산에서 뺀다. 남겨 두면 바닥에서 미세하게 떨리며
                    // 영원히 레이캐스트를 돈다.
                    if (piece.velocity.magnitude < RestSpeed)
                    {
                        piece.resting = true;
                        continue;
                    }
                }
                else
                {
                    piece.transform.position = from + step;
                }

                piece.transform.Rotate(piece.spin * delta, Space.World);
            }
        }
    }
}
