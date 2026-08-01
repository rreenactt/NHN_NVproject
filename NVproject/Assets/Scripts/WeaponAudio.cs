using UnityEngine;

/// <summary>
/// The gunshot, synthesised in code like the footsteps and the level's hum.
///
/// **It is heard three times as far as a footstep, and that is the design.** The Seeker's magazine
/// already costs them the chain; the noise is what it costs them in *information*. A shot fired
/// anywhere near the middle of the level tells every Runner in a sixty-metre radius roughly where
/// the Seeker is and that they have one fewer round left. Firing should never be free.
///
/// The falloff is therefore shaped differently from <see cref="FootstepAudio"/>'s. A footstep needs
/// a hard edge — beyond eighteen metres it must be *gone*, or "faintly audible everywhere" tells a
/// Seeker nothing. A gunshot is the opposite: it should still be clearly there at thirty metres and
/// only fade out at the very end, so the sound carries the news across the building.
/// </summary>
[DisallowMultipleComponent]
public class WeaponAudio : MonoBehaviour
{
    [Header("Hearing")]
    [Tooltip("Metres. Past this the shot is silent. Deliberately far — this is the Seeker's " +
             "loudest tell, and shortening it makes shooting nearly free.")]
    public float hearingRange = 60f;

    [Tooltip("Metres. Inside this the shot plays at full volume.")]
    public float fullVolumeRange = 3f;

    [Tooltip("Volume of somebody else's shot at full volume.")]
    [Range(0f, 1f)] public float volume = 1f;

    [Tooltip("Your own shot. Loud — it is a pistol at arm's length — but not so loud that it " +
             "buries the footsteps you are listening for immediately afterwards.")]
    [Range(0f, 1f)] public float selfVolume = 0.55f;

    [Tooltip("On for the agent this client is playing. Own shots are heard flat rather than positioned.")]
    public bool isLocalListener;

    private static AudioClip[] _shotClips;

    private AudioSource _source;
    private int _lastClip = -1;

    private void Awake() => EnsureSource();

    /// <summary>Applied here as well as in Awake: a spawner may set <see cref="isLocalListener"/> after AddComponent.</summary>
    private void Start() => ApplySettings();

    /// <summary>Fired by <see cref="WeaponController"/> the moment a round leaves the barrel.</summary>
    public void PlayShot()
    {
        EnsureSource();
        AudioClip clip = NextClip();
        if (clip == null || _source == null) return;

        if (!Mathf.Approximately(_source.spatialBlend, isLocalListener ? 0f : 1f)) ApplySettings();

        // Barely any pitch variation. A pistol is a machine; three shots in a row should sound
        // like the same machine three times, not like three different guns.
        _source.pitch = Random.Range(0.97f, 1.03f);
        _source.PlayOneShot(clip, isLocalListener ? selfVolume : volume);
    }

    private AudioClip NextClip()
    {
        AudioClip[] clips = ShotClips();
        if (clips.Length == 0) return null;

        int index = Random.Range(0, clips.Length);
        if (index == _lastClip) index = (index + 1) % clips.Length;
        _lastClip = index;
        return clips[index];
    }

    private void EnsureSource()
    {
        if (_source != null) return;

        Transform existing = transform.Find("Gunshot");
        var go = existing != null ? existing.gameObject : new GameObject("Gunshot");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.4f;   // roughly where the gun is held

        _source = go.GetComponent<AudioSource>();
        if (_source == null) _source = go.AddComponent<AudioSource>();

        ApplySettings();
    }

    private void ApplySettings()
    {
        if (_source == null) return;

        _source.playOnAwake = false;
        _source.loop = false;
        _source.dopplerLevel = 0f;
        _source.spread = 20f;
        _source.spatialBlend = isLocalListener ? 0f : 1f;
        _source.minDistance = fullVolumeRange;
        _source.maxDistance = hearingRange;
        _source.rolloffMode = AudioRolloffMode.Custom;
        _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, BuildRolloff());
    }

    /// <summary>
    /// Shallow through the middle, hard zero at the end. Compare
    /// <see cref="FootstepAudio"/>: this one is still at a third of its volume halfway out.
    /// </summary>
    private AnimationCurve BuildRolloff()
    {
        float plateau = Mathf.Clamp01(fullVolumeRange / Mathf.Max(0.01f, hearingRange));

        var curve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(plateau, 1f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.15f), 0.62f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.30f), 0.36f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.50f), 0.18f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.75f), 0.07f),
            new Keyframe(1f, 0f));

        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0.6f);
        return curve;
    }

    // ============================================================ synthesis

    private static AudioClip[] ShotClips()
    {
        if (_shotClips != null && _shotClips.Length > 0 && _shotClips[0] != null) return _shotClips;

        _shotClips = new AudioClip[3];
        for (int i = 0; i < _shotClips.Length; i++)
            _shotClips[i] = BuildShot("Gunshot " + i, 7717 + i * 613, bodyHz: 118f + i * 9f);

        return _shotClips;
    }

    /// <summary>
    /// A pistol going off indoors, in four parts that all start at the same instant and die at
    /// wildly different rates — which is most of what makes a bang a bang:
    /// <list type="bullet">
    /// <item>the <b>crack</b>, differentiated noise gone in three milliseconds — the supersonic
    /// snap, and the part that makes it read as a gun rather than a drum;</item>
    /// <item>the <b>blast</b>, broadband noise over about forty;</item>
    /// <item>the <b>body</b>, a low sine that gives it a chest;</item>
    /// <item>the <b>tail</b> — heavily low-passed noise decaying over half a second. This level is
    /// concrete corridors, and without the room answering back the shot sounds like it was fired
    /// outdoors in a field.</item>
    /// </list>
    /// </summary>
    private static AudioClip BuildShot(string name, int seed, float bodyHz)
    {
        const int sampleRate = 44100;
        const float length = 0.75f;

        int count = Mathf.RoundToInt(sampleRate * length);
        var samples = new float[count];

        var random = new System.Random(seed);
        float previousWhite = 0f;
        float lowPassed = 0f;
        float tailPassed = 0f;

        int attack = Mathf.Max(1, sampleRate / 4000);   // a quarter of a millisecond

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;

            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            float bright = white - previousWhite;
            previousWhite = white;

            lowPassed = Mathf.Lerp(lowPassed, white, 0.45f);
            tailPassed = Mathf.Lerp(tailPassed, white, 0.05f);

            float crack = bright * Mathf.Exp(-t * 380f) * 0.9f;
            float blast = lowPassed * Mathf.Exp(-t * 42f) * 0.85f;
            float body = Mathf.Sin(2f * Mathf.PI * bodyHz * t) * Mathf.Exp(-t * 26f) * 0.5f;

            // The room. Slightly modulated so it does not read as a synthesiser pad.
            float wobble = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 7.3f * t);
            float tail = tailPassed * Mathf.Exp(-t * 7f) * 0.32f * wobble;

            float envelope = i < attack ? i / (float)attack : 1f;
            samples[i] = (crack + blast + body + tail) * envelope;
        }

        // Normalise: four components peaking together overshoot 1.0 and SetData clips that into
        // distortion — the same trap the footsteps fell into.
        float loudest = 0f;
        for (int i = 0; i < count; i++) loudest = Mathf.Max(loudest, Mathf.Abs(samples[i]));
        if (loudest > 1e-4f)
        {
            float scale = 0.95f / loudest;
            for (int i = 0; i < count; i++) samples[i] *= scale;
        }

        int fade = Mathf.Min(count, sampleRate / 200);
        for (int i = 0; i < fade; i++) samples[count - fade + i] *= 1f - i / (float)fade;

        var clip = AudioClip.Create(name, count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
