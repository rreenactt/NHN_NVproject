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

            SetWeaponVisible(seeker);
        }

        private void SetWeaponVisible(bool visible)
        {
            var rig = GetComponent<BlockRig>();
            if (rig == null) return;

            if (rig.BodyWeapon != null) rig.BodyWeapon.gameObject.SetActive(visible);
            if (rig.ViewWeapon != null) rig.ViewWeapon.gameObject.SetActive(visible);

            var animator = GetComponent<BlockCharacterAnimator>();
            if (animator != null) animator.Armed = visible;
        }
    }
}
