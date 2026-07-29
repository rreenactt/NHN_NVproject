using UnityEngine;

/// <summary>
/// A real projectile: it exists in the world, it takes time to get there, and you can watch it
/// fly. This replaces the old hitscan, where the shot resolved instantly on the frame you
/// clicked and the "tracer" was only ever a decorative line drawn after the fact.
///
/// The trajectory is integrated by hand rather than handed to a Rigidbody — same rule as thrown
/// objects in this project. A Rigidbody bullet would need continuous collision detection, a
/// physics material, and would still jitter; integrating one vector is simpler and exact.
///
/// **The swept test is the whole trick.** At 120 m/s a bullet covers 2 m in a single 60 fps
/// frame, so testing "is there a wall where the bullet now is" finds nothing — it has already
/// teleported past the 0.25 m wall. Instead each step raycasts along the segment the bullet
/// *travelled*, so nothing can be tunnelled through no matter how fast it goes.
///
/// The bullet carries no collider of its own; it only ever asks about the world. That means
/// nothing can hit it, and it can never shove the player who fired it.
/// </summary>
public class Bullet : MonoBehaviour
{
    [Tooltip("Metres per second. Fast enough to feel like a bullet, slow enough to see.")]
    public float speed = 120f;

    [Tooltip("Damage dealt to whatever it hits.")]
    public float damage = 20f;

    [Tooltip("Seconds before an unobstructed round gives up and disappears.")]
    public float lifetime = 3f;

    [Tooltip("Downward acceleration in m/s². Left at 0 the round flies dead straight and the " +
             "crosshair never lies; turn it on and shots start dropping at range.")]
    public float gravity = 0f;

    [Tooltip("Impulse applied to any rigidbody it strikes.")]
    public float impactForce = 300f;

    public LayerMask hitMask = ~0;

    private Vector3 _velocity;
    private float _age;
    private float _travelled;
    private System.Action<RaycastHit> _onImpact;
    private static Material _sharedMaterial;

    /// <summary>
    /// Builds and launches a round. Made here rather than from a prefab so the weapon does not
    /// need one wired up — the same reason nothing else in this project has prefabs.
    /// </summary>
    /// <param name="onImpact">Raised when the round lands, so the shooter can mark the hit.</param>
    public static Bullet Spawn(Vector3 origin, Vector3 direction, float speed, float damage,
        float gravity, LayerMask hitMask, float lifetime = 3f,
        System.Action<RaycastHit> onImpact = null)
    {
        var go = new GameObject("Bullet");
        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation(direction);

        // A short bright sliver, stretched along flight — reads as a tracer round in a dark map.
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Tracer";
        Object.Destroy(visual.GetComponent<Collider>());   // the bullet sweeps; it never collides
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = new Vector3(0.035f, 0.035f, 0.34f);
        visual.GetComponent<MeshRenderer>().sharedMaterial = SharedMaterial();

        var bullet = go.AddComponent<Bullet>();
        bullet.speed = speed;
        bullet.damage = damage;
        bullet.gravity = gravity;
        bullet.hitMask = hitMask;
        bullet.lifetime = lifetime;
        bullet._velocity = direction.normalized * speed;
        bullet._onImpact = onImpact;
        return bullet;
    }

    private void Update()
    {
        float step = Time.deltaTime;
        if (step <= 0f) return;
        _age += step;

        if (gravity != 0f) _velocity += Vector3.up * (gravity * step);

        Vector3 from = transform.position;
        Vector3 travel = _velocity * step;
        float distance = travel.magnitude;

        // Sweep the segment actually covered this frame, not the endpoint.
        if (distance > 0f
            && Physics.Raycast(from, travel / distance, out RaycastHit hit, distance,
                               hitMask, QueryTriggerInteraction.Ignore))
        {
            _travelled += hit.distance;
            Impact(hit);
            return;
        }

        _travelled += distance;
        transform.position = from + travel;
        if (_velocity.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(_velocity);

        if (_age >= lifetime) Destroy(gameObject);
    }

    private void Impact(RaycastHit hit)
    {
        if (hit.rigidbody != null)
            hit.rigidbody.AddForceAtPosition(_velocity.normalized * impactForce, hit.point);

        hit.collider.SendMessageUpwards("OnHit", damage, SendMessageOptions.DontRequireReceiver);
        _onImpact?.Invoke(hit);

        // Report the whole flight, not hit.distance — that is only the part of this one frame's
        // step that was covered before impact, which is misleadingly tiny.
        Debug.Log($"[Bullet] Hit '{hit.collider.name}' after {_travelled:F1} m of flight " +
                  $"({_age * 1000f:F0} ms).");
        Destroy(gameObject);
    }

    private static Material SharedMaterial()
    {
        if (_sharedMaterial != null) return _sharedMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _sharedMaterial = new Material(shader) { name = "Bullet Tracer" };
        var glow = new Color(1f, 0.86f, 0.45f);
        _sharedMaterial.color = glow;
        _sharedMaterial.EnableKeyword("_EMISSION");
        _sharedMaterial.SetColor("_EmissionColor", glow * 4f);
        _sharedMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        _sharedMaterial.enableInstancing = true;
        return _sharedMaterial;
    }
}
