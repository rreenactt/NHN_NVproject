using System.Collections;
using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// What emptying the magazine costs. The Seeker fires three rounds; a chain then comes out of
    /// a point in the level, hauls them to it, and holds them there for three seconds before the
    /// reload even begins.
    ///
    /// This is the game's main balancing lever, and it is deliberately punishing: three shots is
    /// barely a burst, so the Seeker has to decide whether the third round is worth being pinned
    /// to a wall for the better part of four seconds while every Runner in earshot relocates.
    ///
    /// The drag is not a teleport. Being pulled — visibly, along a chain, over
    /// <see cref="GameConfig.chainDragTime"/> — is what makes it read as a penalty inflicted on
    /// the Seeker rather than as a repositioning tool. The look is left free throughout: the point
    /// is to watch the Runners get away.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChainDrag : MonoBehaviour
    {
        private static Material _chainMaterial;

        public PlayerAgent agent;
        public FirstPersonController controller;
        public WeaponController weapon;

        private Transform _chain;
        private Coroutine _routine;

        public bool Active { get; private set; }

        /// <summary>Seconds left of the hold, for the HUD. 0 when not chained.</summary>
        public float Remaining { get; private set; }

        private void Awake()
        {
            if (agent == null) agent = GetComponent<PlayerAgent>();
            if (controller == null) controller = GetComponent<FirstPersonController>();
            if (weapon == null) weapon = GetComponent<WeaponController>();
        }

        /// <summary>Called by the weapon the moment the last round leaves the magazine.</summary>
        public void Trigger()
        {
            if (Active || !isActiveAndEnabled) return;
            if (agent != null && !agent.InPlay) return;

            _routine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            MatchManager match = MatchManager.Instance;
            GameConfig config = match != null ? match.Config : null;
            if (config == null) yield break;

            Active = true;
            agent?.SetChained(true);
            if (weapon != null) weapon.FireBlocked = true;

            Vector3 anchor = FindAnchor(config, match);
            BuildChain(anchor);

            match?.Notify("CHAINED");

            // 1. the drag itself
            Vector3 start = transform.position;
            float travel = Mathf.Max(0.01f, config.chainDragTime);
            for (float t = 0f; t < travel; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / travel);
                Vector3 here = Vector3.Lerp(start, anchor, k);
                if (controller != null) controller.Teleport(here);
                else transform.position = here;

                UpdateChain(anchor);
                Remaining = config.chainWait + (travel - t);
                yield return null;
            }

            // 2. the wait. This is the part that hurts.
            for (float t = config.chainWait; t > 0f; t -= Time.deltaTime)
            {
                Remaining = t;
                UpdateChain(anchor);
                yield return null;
            }

            // 3. only now does the magazine come back
            Remaining = 0f;
            DestroyChain();
            agent?.SetChained(false);

            if (weapon != null)
            {
                weapon.FireBlocked = false;
                weapon.ForceReload(config.chainReloadTime);
            }

            Active = false;
            _routine = null;
        }

        /// <summary>
        /// Where the chain bites. It looks for a wall around the Seeker and takes the nearest one,
        /// so the drag is short and sideways rather than across the level — the penalty is the
        /// three seconds of standing still, and a hundred-metre yank would be a free escape.
        /// </summary>
        private Vector3 FindAnchor(GameConfig config, MatchManager match)
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            float best = float.MaxValue;
            Vector3 point = transform.position;
            bool found = false;

            for (int i = 0; i < 12; i++)
            {
                float angle = i * (Mathf.PI * 2f / 12f);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                if (!Physics.Raycast(origin, direction, out RaycastHit hit, config.chainAnchorRange,
                                     ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.distance < 1.2f) continue;                 // already against that wall
                if (hit.distance >= best) continue;

                best = hit.distance;
                point = hit.point - direction * 0.75f;              // stop short of the wall itself
                found = true;
            }

            if (!found) return transform.position;

            point.y = transform.position.y;

            // Never end up inside geometry: the grid knows what is standable, the raycast does not.
            if (match != null && match.Map != null
                && match.Map.TryNearestStandablePoint(point, out Vector3 standable))
                point = new Vector3(standable.x, point.y, standable.z);

            return point;
        }

        private void BuildChain(Vector3 anchor)
        {
            DestroyChain();

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Chain";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = ChainMaterial();
            _chain = go.transform;

            UpdateChain(anchor);
        }

        private void UpdateChain(Vector3 anchor)
        {
            if (_chain == null) return;

            Vector3 from = transform.position + Vector3.up * 1.1f;
            Vector3 to = anchor + Vector3.up * 1.1f;
            Vector3 mid = (from + to) * 0.5f;
            float length = Vector3.Distance(from, to);

            _chain.position = mid;
            _chain.rotation = length > 0.01f
                ? Quaternion.LookRotation((to - from).normalized)
                : Quaternion.identity;
            _chain.localScale = new Vector3(0.09f, 0.09f, Mathf.Max(0.05f, length));
        }

        private void DestroyChain()
        {
            if (_chain != null) Destroy(_chain.gameObject);
            _chain = null;
        }

        private void OnDisable()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            DestroyChain();
            Remaining = 0f;
            Active = false;
            agent?.SetChained(false);
            if (weapon != null) weapon.FireBlocked = false;
        }

        private static Material ChainMaterial()
        {
            if (_chainMaterial != null) return _chainMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _chainMaterial = new Material(shader)
            {
                name = "Chain",
                color = new Color(0.18f, 0.17f, 0.16f),
            };
            if (_chainMaterial.HasProperty("_Metallic")) _chainMaterial.SetFloat("_Metallic", 0.9f);
            if (_chainMaterial.HasProperty("_Smoothness")) _chainMaterial.SetFloat("_Smoothness", 0.6f);
            return _chainMaterial;
        }
    }
}
