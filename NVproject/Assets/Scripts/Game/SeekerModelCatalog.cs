using System.Collections.Generic;
using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// Everything a monster body is, as data: the block geometry, the palette, the gait and
    /// idle profile, and which footstep set it makes. <see cref="BlockRig"/> builds from it,
    /// <see cref="BlockCharacterAnimator"/> tunes itself from it, <see cref="FootstepAudio"/>
    /// picks its clip set from it.
    ///
    /// **A null plan means the humanoid** — the eight lobby characters all share the one
    /// hard-coded figure in <see cref="BlockRig"/>, painted per character. A plan only exists
    /// for bodies that are *not* people, which today is the Seeker.
    ///
    /// The plan deliberately says nothing about hitboxes: the server judges every shot and
    /// every move against its fixed 1.8 m × 0.8 m box (`SimConstants.PlayerHeight/Radius`),
    /// so a plan whose visible mass strays far outside that box makes shots *look* wrong.
    /// Keep the meat inside the box; only thin dressing may poke out.
    /// </summary>
    public sealed class BodyPlan
    {
        public string id;
        public string label;

        // ---- Geometry, in the rig's 32-per-figure pixel grid (32 px = totalHeight) ----

        /// <summary>Leg length in px. Also the hip height — the gait maths reads it as L.</summary>
        public float legLengthPx = 12f;
        public float legThickPx = 4f;

        /// <summary>Torso length in px, measured *along* the tilted spine, not vertically.</summary>
        public float torsoLengthPx = 12f;
        public float torsoWidthPx = 8f;
        public float torsoDepthPx = 4f;

        /// <summary>
        /// Forward pitch of the spine, in degrees. Baked into the block placement under the
        /// Torso joint, never into the joint's rotation — the animator composes every joint's
        /// localRotation from scratch each frame and would silently erase a baked rotation.
        /// </summary>
        public float torsoTiltDeg;

        /// <summary>Shoulder hump block size in px. The monster's highest point; 0 = none.</summary>
        public float humpPx;

        /// <summary>Head cube edge in px. Small heads read as wrong, which is the point.</summary>
        public float headPx = 8f;

        /// <summary>How far below the collar the head hangs, in px, so the hump stays highest.</summary>
        public float headDropPx;

        /// <summary>Arm length in px. Independent of the legs — the humanoid's 12/12 was a coincidence.</summary>
        public float armLengthPx = 12f;
        public float armThickPx = 4f;

        /// <summary>Emissive eye block edge in px; 0 = no eyes. Two, on the head's front face.</summary>
        public float eyePx;

        /// <summary>Viewmodel framing for this body's own first person. Longer arms need their own solve.</summary>
        public Vector3 viewmodelOffset = new Vector3(-0.1692f, -0.0028f, 0.1027f);

        // ---- Palette. The plan owns its colours; CharacterAppearance must not repaint it. ----

        public Color bodyColor = Color.white;
        public Color eyeColor = Color.white;

        /// <summary>Emission strength of the eyes. They must read through the fog before the body does.</summary>
        public float eyeGlow = 1f;

        // ---- Gait and idle, applied by BlockCharacterAnimator over its serialized defaults ----

        public float walkStrideRate = 0.95f;
        public float sprintStrideRate = 1.55f;
        public float armSwingRatio = 0.75f;

        /// <summary>0 lets the head roll with every step — the level gaze is what makes a walk read as human.</summary>
        public float headStabilise = 0.55f;

        public float breathAmount = 1.1f;

        /// <summary>Left-leg swing multiplier. Below 1 the figure limps; 1 is symmetric.</summary>
        public float limpScale = 1f;

        /// <summary>Seconds between idle head snaps; 0 = no twitch. Runs only while still.</summary>
        public float twitchInterval;

        /// <summary>Degrees the head snaps sideways when the twitch fires.</summary>
        public float twitchAngle;

        /// <summary>Where the weapon arm points while armed, in body/camera space.</summary>
        public Vector3 armedDirectionR = new Vector3(-0.12f, -0.155f, 1f);

        /// <summary>The off arm. A monster's hangs rather than gripping — the gun grew out of the other one.</summary>
        public Vector3 armedDirectionL = new Vector3(0.80f, -0.170f, 1f);

        // ---- Sound ----

        /// <summary>Picks FootstepAudio's heavy clip set. The falloff curve is a balance value and never changes.</summary>
        public bool heavySteps;

        // ---- Materials: one per plan, lazily made, shared by every body wearing the plan. ----
        // The null re-check is the domain-reload guard, same as CharacterAppearance.Palette:
        // a reload during play wipes the field without re-running anything, and the next body
        // would otherwise render pink.

        private Material _body;
        private Material _eye;

        public Material BodyMaterial
        {
            get
            {
                if (_body == null) _body = MakeLit(label + " Body", bodyColor);
                return _body;
            }
        }

        public Material EyeMaterial
        {
            get
            {
                if (_eye == null)
                {
                    _eye = MakeLit(label + " Eye", eyeColor);
                    _eye.EnableKeyword("_EMISSION");
                    _eye.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    _eye.SetColor("_EmissionColor", eyeColor * eyeGlow);
                }
                return _eye;
            }
        }

        private static Material MakeLit(string name, Color colour)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = colour };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
            return material;
        }
    }

    /// <summary>
    /// The Seeker bodies, picked whole — the same catalog pattern as
    /// <see cref="NV.Client.Lobby.LobbyCharacterCatalog"/>, and for the same reason: a monster
    /// is a finished design, not a bag of tunables to mix per player.
    ///
    /// **One entry today, and index 0 is the game's whole answer.** Nothing about the choice
    /// is on the wire and the server knows nothing about it: every client derives "the Seeker
    /// looks like <see cref="Default"/>" on its own, so every screen agrees for free. When a
    /// second monster is added, *who decides which one appears* becomes a wire question —
    /// answer it then, not now.
    /// </summary>
    public static class SeekerModelCatalog
    {
        private static List<BodyPlan> _all;

        public static IReadOnlyList<BodyPlan> All => _all ??= Build();

        public static int Count => All.Count;

        /// <summary>The monster every Seeker becomes until model choice is a real feature.</summary>
        public static BodyPlan Default => All[0];

        private static List<BodyPlan> Build() => new List<BodyPlan>
        {
            Lurker(),
        };

        /// <summary>
        /// The hunched tall one. Fills the server box vertically (hump at ~1.74 m against the
        /// 1.8 m box), shoulders above where the head should be, arms down to the knees, a
        /// too-small head with sickly glowing eyes. Every proportion is a broken human one —
        /// in this fog the outline is read long before any detail, so the outline carries it.
        /// </summary>
        private static BodyPlan Lurker() => new BodyPlan
        {
            id = "lurker",
            label = "LURKER",

            legLengthPx = 14f,
            legThickPx = 3f,
            torsoLengthPx = 15f,
            torsoWidthPx = 9f,
            torsoDepthPx = 4f,
            torsoTiltDeg = 28f,
            humpPx = 4f,
            headPx = 5f,
            headDropPx = 3f,
            armLengthPx = 20f,
            armThickPx = 3f,
            eyePx = 1.2f,

            // Long arms hang lower on screen; lifted and pushed out relative to the humanoid
            // solve. First guess — re-solved against the rasteriser in the animation pass.
            viewmodelOffset = new Vector3(-0.16f, -0.055f, 0.16f),

            // Near-black warm grey: in the mono-yellow fog a dark mass with two lit points
            // reads from much further than any colour would.
            bodyColor = new Color(0.09f, 0.085f, 0.078f),
            eyeColor = new Color(1f, 0.93f, 0.72f),
            eyeGlow = 3.2f,

            walkStrideRate = 0.75f,     // long, slow strides — the swing angle follows from speed
            sprintStrideRate = 1.35f,
            armSwingRatio = 0.5f,       // stiff arms that mostly hang
            headStabilise = 0.1f,       // the head rolls with the body; a level gaze is human
            breathAmount = 2.4f,        // heaving, visible across a corridor
            limpScale = 0.55f,          // drags the left leg
            twitchInterval = 4.5f,
            twitchAngle = 75f,

            armedDirectionR = new Vector3(-0.10f, -0.20f, 1f),
            armedDirectionL = new Vector3(0.25f, -0.85f, 0.30f),   // hangs, claw down

            heavySteps = true,
        };
    }
}
