using UnityEngine;
using UnityEngine.InputSystem;

namespace NV.Game
{
    /// <summary>
    /// The E key. Finds the nearest thing the local player may use — a device, or the door — and
    /// hands the HUD a prompt for it.
    ///
    /// It looks by *proximity*, not down the crosshair. A ray would be the obvious choice, but the
    /// door is invisible to the Seeker and only barely lit for a Runner, and a game where the
    /// objective needs precise aim to touch is a game where being chased means fumbling. What the
    /// player is facing is used only to break ties.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        public PlayerAgent agent;
        public Transform eye;

        [Tooltip("Metres searched for something to use. The per-object radius still has the " +
                 "final say — this only bounds the search.")]
        public float searchRadius = 3.2f;

        /// <summary>What the HUD should be showing, or null.</summary>
        public string Prompt { get; private set; }

        private IInteractable _target;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<PlayerAgent>();
            if (eye == null)
            {
                var controller = GetComponent<FirstPersonController>();
                eye = controller != null && controller.cameraTransform != null
                    ? controller.cameraTransform : transform;
            }
        }

        private void Update()
        {
            Prompt = null;
            _target = null;

            MatchManager match = MatchManager.Instance;
            if (match == null || agent == null || !agent.InPlay) return;
            if (match.Phase != MatchPhase.Playing) return;

            FindTarget(match);

            if (_target == null) return;

            Prompt = _target.Prompt(agent);
            if (Prompt == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame) _target.Interact(agent);
        }

        private void FindTarget(MatchManager match)
        {
            Vector3 here = transform.position;
            Vector3 facing = eye != null ? eye.forward : transform.forward;
            float bestScore = float.MaxValue;

            // The door first — it is the only object whose prompt is role-dependent, and it is
            // what the player is most likely to be standing at on purpose.
            Consider(match.Door, here, facing, ref bestScore);

            DeviceSystem devices = DeviceSystem.Instance;
            if (devices == null) return;

            for (int i = 0; i < devices.Devices.Count; i++)
                Consider(devices.Devices[i], here, facing, ref bestScore);
        }

        private void Consider(IInteractable candidate, Vector3 here, Vector3 facing, ref float bestScore)
        {
            if (candidate == null) return;

            // A destroyed device's MonoBehaviour is still alive; a destroyed GameObject's is not.
            if (candidate is MonoBehaviour behaviour && behaviour == null) return;

            Vector3 delta = candidate.Position - here;
            if (Mathf.Abs(delta.y) > 2.5f) return;

            float distance = new Vector2(delta.x, delta.z).magnitude;
            if (distance > Mathf.Min(searchRadius, candidate.UseRadius)) return;

            // Distance decides, but facing away from something you are standing on top of should
            // not steal the prompt from the thing you are looking at.
            float alignment = distance > 0.05f
                ? Vector3.Dot(facing.normalized, delta.normalized) : 1f;
            float score = distance - alignment * 0.5f;

            if (score >= bestScore) return;

            bestScore = score;
            _target = candidate;
        }
    }
}
