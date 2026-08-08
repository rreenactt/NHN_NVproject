using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// Turns a role into a body. One Seeker carries a three-round pistol and sees blood; every
    /// Runner has empty hands and sees the door. Everything that differs between the two sides on
    /// *this machine* is applied here, in one pass, when the roles are handed out.
    ///
    /// It is a separate component from <see cref="PlayerAgent"/> because the agent is pure state —
    /// the same state a remote player will have — while this is local presentation and input:
    /// weapons, camera masks, the chain. A remote Runner needs the first and none of the second.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRoleLoadout : MonoBehaviour
    {
        public PlayerAgent agent;
        public WeaponController weapon;
        public WeaponSwitcher weaponSwitcher;
        public ChainDrag chain;
        public Camera viewCamera;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<PlayerAgent>();
            if (weapon == null) weapon = GetComponent<WeaponController>();
            if (weaponSwitcher == null) weaponSwitcher = GetComponent<WeaponSwitcher>();
            if (chain == null) chain = GetComponent<ChainDrag>();
            if (viewCamera == null)
            {
                var controller = GetComponent<FirstPersonController>();
                if (controller != null && controller.cameraTransform != null)
                    viewCamera = controller.cameraTransform.GetComponent<Camera>();
            }
            if (viewCamera == null) viewCamera = Camera.main;
        }

        private void OnEnable()
        {
            if (MatchManager.Instance != null) MatchManager.Instance.RolesAssigned += Apply;
        }

        private void OnDisable()
        {
            if (MatchManager.Instance != null) MatchManager.Instance.RolesAssigned -= Apply;
        }

        public void Apply()
        {
            MatchManager match = MatchManager.Instance;
            if (match == null || agent == null) return;

            bool seeker = agent.Role == Role.Seeker;

            MatchLayers.ApplyRoleVisibility(viewCamera, agent.Role);

            if (weaponSwitcher != null)
            {
                // Straight to the right hands, with no swap animation: at the role reveal there is
                // nothing to hide, and playing the lower-raise motion here would leave a Runner
                // briefly holding a pistol.
                weaponSwitcher.startArmed = seeker;
                weaponSwitcher.enabled = seeker;   // a Runner must not be able to press 2
            }

            if (weapon != null)
            {
                weapon.Armed = seeker;
                weapon.enabled = seeker;
                weapon.SetMagazineSize(match.Config.seekerMagazine);
                weapon.Refill();

                // The chain owns the empty magazine for a Seeker, and nothing owns it otherwise.
                weapon.onMagazineEmpty = seeker && chain != null
                    ? (System.Action)chain.Trigger
                    : null;
            }

            if (chain != null) chain.enabled = seeker;

            ApplyRole(gameObject, agent.Role);
        }

        /// <summary>
        /// What a role looks like on a body: the Seeker *is* the monster, everyone else is the
        /// humanoid they picked in the lobby. Static for the same reason <see cref="ShowWeapon"/>
        /// is — a remote body needs the identical answer and must not get a second copy of it;
        /// <c>RemotePlayerPuppet</c> calls this when the server tells it what a body is.
        ///
        /// The rebind-and-repaint block runs only on a real body swap: <c>Rebuild</c> reports
        /// whether anything changed, and roles are re-announced on every match start.
        /// </summary>
        public static void ApplyRole(GameObject body, Role role)
        {
            if (body == null) return;

            var rig = body.GetComponent<BlockRig>();
            if (rig == null) return;

            bool seeker = role == Role.Seeker;

            if (rig.Rebuild(seeker ? SeekerModelCatalog.Default : null))
            {
                // The lobby paint died with the old blocks; clearing the applied id is what
                // lets the poll dress the humanoid again on the way back from the monster.
                var appearance = body.GetComponent<CharacterAppearance>();
                if (appearance != null) appearance.OnRigRebuilt();

                // The weapon cached the old viewmodel muzzle in Start; that transform is
                // destroyed now, and a stale muzzle silently fires from the eye instead.
                var weapon = body.GetComponent<WeaponController>();
                if (weapon != null) weapon.RebindMuzzle();
            }

            ShowWeapon(body, seeker);
        }

        /// <summary>
        /// What being armed looks like: the pistol in the hand and the arm pose that holds it.
        ///
        /// **Static because a remote body needs the same answer and must not get a second copy of
        /// it.** This component is the local player's — it also owns the camera's culling mask, the
        /// weapon switcher and the chain, none of which a puppet has any business with. Only the
        /// *appearance* is common, so only the appearance is shared; `RemotePlayerPuppet` calls this
        /// when the server tells it what role that body is.
        ///
        /// Without it every remote body kept the rig's default — armed — so Runners walked around
        /// holding pistols and the one thing that identifies the Seeker across a foggy room was
        /// carried by everybody.
        /// </summary>
        public static void ShowWeapon(GameObject body, bool visible)
        {
            if (body == null) return;

            var rig = body.GetComponent<BlockRig>();
            if (rig == null) return;

            if (rig.BodyWeapon != null) rig.BodyWeapon.gameObject.SetActive(visible);
            if (rig.ViewWeapon != null) rig.ViewWeapon.gameObject.SetActive(visible);

            var animator = body.GetComponent<BlockCharacterAnimator>();
            if (animator != null) animator.Armed = visible;
        }
    }
}
