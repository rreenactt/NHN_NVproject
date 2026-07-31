using UnityEngine;
using UnityEngine.AI;

namespace NV.Game
{
    /// <summary>
    /// One participant in the match — the local player, a practice Runner, and later a networked
    /// one. This holds only *state*: whose side, alive or not, bleeding or not, keys in hand.
    ///
    /// It deliberately decides nothing. Every rule that reads this state lives in
    /// <see cref="MatchManager"/>, because the ruleset is host-authoritative and a rule split
    /// across every player object is a rule that will eventually disagree with itself. The one
    /// thing that arrives here first is the bullet — <see cref="OnHit"/> is what
    /// <see cref="Bullet"/>'s <c>SendMessageUpwards</c> lands on — and all it does is forward.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAgent : MonoBehaviour
    {
        [Tooltip("Shown in the HUD and the kill feed.")]
        public string displayName = "Player";

        [Tooltip("True for the one agent this client is playing. Drives the HUD and the camera's " +
                 "role-based culling.")]
        public bool isLocalPlayer;

        [Tooltip("Eye/head transform, used as the origin for the Seeker camera feed and as the " +
                 "point a map marker is drawn at. Falls back to the transform itself.")]
        public Transform head;

        [Tooltip("May this Runner pick keys up? Off for practice Runners: they wander, so they " +
                 "would sweep the level clean of keys they can never insert, and the objective " +
                 "would be unwinnable within a minute of the match starting.")]
        public bool collectsKeys = true;

        [Tooltip("Set for a human-controlled agent; null on a practice Runner.")]
        public FirstPersonController controller;

        [Tooltip("Set on a practice Runner; null on a human.")]
        public NavMeshAgent navAgent;

        public Role Role { get; private set; } = Role.Unassigned;
        public bool Alive { get; private set; } = true;
        public bool Escaped { get; private set; }
        public bool Bleeding { get; private set; }
        public int Hits { get; private set; }
        public int CarriedKeys { get; private set; }

        /// <summary>Wall-clock time the last hit landed. The immunity window is measured from it.</summary>
        public float LastHitTime { get; private set; } = -999f;

        /// <summary>Seconds this Runner has stood in the open doorway. Owned by the manager.</summary>
        internal float EscapeHold;

        /// <summary>Earliest time the next key may go into the door. Owned by the manager.</summary>
        internal float NextInsertTime;

        /// <summary>In the match and able to act: not dead, not escaped, not still in the lobby.</summary>
        public bool InPlay => Alive && !Escaped;

        public Vector3 HeadPosition => head != null ? head.position : transform.position + Vector3.up * 1.6f;

        /// <summary>Ground position. The rig hangs the body off the transform, which sits at the feet.</summary>
        public Vector3 FeetPosition => transform.position;

        private BloodTrail _trail;
        private bool _frozen;
        private bool _chained;
        private Renderer[] _renderers;
        private Collider[] _colliders;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<FirstPersonController>();
            if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
            if (head == null && controller != null) head = controller.cameraTransform;
        }

        private void OnEnable() => MatchManager.Instance?.Register(this);
        private void OnDisable() => MatchManager.Instance?.Unregister(this);

        /// <summary>
        /// Where a round lands. <see cref="Bullet"/> raises this with <c>SendMessageUpwards</c>,
        /// so the collider that was struck may be several levels below this component.
        ///
        /// The damage number is ignored on purpose: this game counts *hits*, not health. Two hits
        /// kill a Runner whether the round was a graze or a headshot.
        /// </summary>
        private void OnHit(float damage)
        {
            MatchManager.Instance?.ReportHit(this);
        }

        // ============================================================ authority-only mutators
        // Everything below is called by MatchManager and nothing else. They are public because
        // the manager is a separate object, not because anyone may call them.

        internal void SetRole(Role role) => Role = role;

        internal void ResetForMatch()
        {
            Alive = true;
            Escaped = false;
            Hits = 0;
            CarriedKeys = 0;
            LastHitTime = -999f;
            EscapeHold = 0f;
            NextInsertTime = 0f;
            SetBleeding(false);
            _chained = false;
            SetFrozen(false);
            SetPresent(true);
        }

        internal void RegisterHit()
        {
            Hits++;
            LastHitTime = Time.time;
        }

        internal void SetBleeding(bool bleeding)
        {
            Bleeding = bleeding;

            if (bleeding)
            {
                if (_trail == null) _trail = gameObject.AddComponent<BloodTrail>();
                _trail.Begin(this);
            }
            else if (_trail != null)
            {
                _trail.Stop();
            }
        }

        internal void AddKeys(int count) => CarriedKeys = Mathf.Max(0, CarriedKeys + count);

        internal void MarkEscaped()
        {
            Escaped = true;
            CarriedKeys = 0;
            SetBleeding(false);
            SetPresent(false);
        }

        internal void Kill()
        {
            Alive = false;
            CarriedKeys = 0;
            SetBleeding(false);
            SetPresent(false);
        }

        /// <summary>
        /// Takes an agent out of the world without taking it out of the match. The GameObject
        /// stays enabled deliberately: deactivating it would fire OnDisable, which unregisters the
        /// agent — in the middle of the manager's own loop over the roster, and while the win
        /// conditions still need to count it as a Runner who is out.
        /// </summary>
        private void SetPresent(bool present)
        {
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
            if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = present;

            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null && !(_colliders[i] is CharacterController))
                    _colliders[i].enabled = present;

            ApplyLock();
        }

        /// <summary>
        /// Moves the agent. Humans go through the controller (which has to disable its
        /// CharacterController across the assignment); a NavMeshAgent has to be warped or it
        /// snaps back to where the navmesh thought it was on the very next frame.
        /// </summary>
        internal void TeleportTo(Vector3 feetPosition)
        {
            if (controller != null) { controller.Teleport(feetPosition); return; }

            if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh) { navAgent.Warp(feetPosition); return; }

            transform.position = feetPosition;
        }

        /// <summary>Freeze device, role reveal, end of match. Look is left alone deliberately.</summary>
        internal void SetFrozen(bool frozen)
        {
            _frozen = frozen;
            ApplyLock();
        }

        /// <summary>Held by the chain. Kept separate from the freeze so one cannot release the other.</summary>
        internal void SetChained(bool chained)
        {
            _chained = chained;
            ApplyLock();
        }

        private void ApplyLock()
        {
            bool locked = _frozen || _chained || !InPlay;
            if (controller != null) controller.MovementLocked = locked;
            if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh) navAgent.isStopped = locked;
        }

        public bool IsFrozen => _frozen;
        public bool IsChained => _chained;
    }
}
