using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// How dark the level is, answered per role.
    ///
    /// This is the same idea as <see cref="MatchLayers"/> — one world, two truths about it — with
    /// the lever moved from *what is rendered* to *how much of it you can make out*. The Runner is
    /// a person in a dead building with a torch. The Seeker is the thing that lives there, and it
    /// does not need one.
    ///
    /// **The map's own numbers are the Runner's.** Everything here is a multiplier on whatever the
    /// level applied to `RenderSettings` when it built, so a map with a grey-green palette
    /// (`backrooms-v2`) and one with a yellow-ochre palette (`backrooms`) both stay themselves.
    /// Hard-coding the two roles' colours would have made this one map's lighting wearing a second
    /// map's name.
    ///
    /// **The base is captured once.** Applying a multiplier to an already-multiplied value is the
    /// obvious bug here: roles are re-announced on every match start and the debug side-swap can
    /// fire mid-match, so a Seeker who swapped twice would be walking around in daylight.
    ///
    /// **This is client-side and therefore not a secret.** A modified build can light its own
    /// screen however it likes — unlike the escape door, which was closed by removing the *input*
    /// the client would need to compute it (`WriteObjectiveState`). Brightness cannot be closed the
    /// same way, because the client has to draw the room. What is being bought here is the feel of
    /// the two sides, not a guarantee about them.
    /// </summary>
    public static class RoleVision
    {
        /// 술래가 어둠에서 얼마나 더 보는가. **이것이 손전등을 대신한다.**
        ///
        /// 실행해 보고 맞춰야 하는 숫자다. 여기서 틀리는 두 방향의 값이 다르다 — 너무 밝으면
        /// 분위기를 잃고, 너무 어두우면 술래가 게임을 할 수 없다. 그래서 밝은 쪽으로 치우쳐
        /// 두었다.
        private const float SeekerAmbientGain = 4f;

        /// 술래의 시야가 안개에 덜 먹힌다 — 복도 끝이 Runner 보다 조금 더 일찍 드러난다.
        private const float SeekerFogRelief = 0.78f;

        /// 술래의 어둠은 차갑다. 밝기만 올리면 "모니터를 밝힌 것" 으로 보이므로, 색을 함께
        /// 식혀서 사람의 눈이 아닌 것으로 읽히게 한다.
        private static readonly Color SeekerTint = new Color(0.86f, 0.98f, 1.16f);

        private static bool _captured;
        private static Color _baseAmbient;
        private static float _baseFogDensity;
        private static Color _baseFogColor;

        /// <summary>
        /// Applies this client's own role to the scene's atmosphere. Called from
        /// <see cref="PlayerRoleLoadout"/>, which only ever runs on the local player — a remote
        /// body must not get a vote on how bright this machine's screen is.
        /// </summary>
        public static void Apply(Role role)
        {
            Capture();

            if (role == Role.Seeker)
            {
                RenderSettings.ambientLight = Scale(_baseAmbient, SeekerAmbientGain, SeekerTint);
                RenderSettings.fogColor = Scale(_baseFogColor, SeekerAmbientGain * 0.5f, SeekerTint);
                RenderSettings.fogDensity = _baseFogDensity * SeekerFogRelief;
                return;
            }

            // Runner 와 미배정은 맵이 지은 그대로다. 이 맵의 어둠은 손전등을 전제로 맞춰져
            // 있으므로, 여기서 더 손대면 두 곳에서 같은 값을 정하게 된다.
            RenderSettings.ambientLight = _baseAmbient;
            RenderSettings.fogColor = _baseFogColor;
            RenderSettings.fogDensity = _baseFogDensity;
        }

        /// <summary>
        /// Forgets the captured base. The next <see cref="Apply"/> reads the new level's numbers.
        ///
        /// **Static state outlives a scene load, and that is the whole reason this exists.** Carry
        /// `backrooms`' ochre base into `backrooms-v2` and the Seeker's screen is lit in the wrong
        /// colour for the map they are standing in.
        /// </summary>
        public static void Forget() => _captured = false;

        private static void Capture()
        {
            if (_captured) return;

            _baseAmbient = RenderSettings.ambientLight;
            _baseFogColor = RenderSettings.fogColor;
            _baseFogDensity = RenderSettings.fogDensity;
            _captured = true;
        }

        private static Color Scale(Color colour, float gain, Color tint)
        {
            return new Color(
                colour.r * gain * tint.r,
                colour.g * gain * tint.g,
                colour.b * gain * tint.b,
                colour.a);
        }
    }
}
