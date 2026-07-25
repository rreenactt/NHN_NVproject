---
name: fps-weapon-system
description: >
  Build and balance guns and weapons for a 3D first-person shooter in Unity via MCP — hitscan
  and projectile firing, raycast hit detection, fire rate, recoil, spread, ammo/reload, weapon
  switching, muzzle flash and impact hookups, and damage/health. Use whenever the user works on
  weapons: "총 만들어", "사격/발사", "반동", "탄약/재장전", "무기 교체", "데미지", shooting,
  gun feel, hit registration, "총 쐈는데 안 맞아". Assumes unity-mcp-ops for tool calls.
  For thrown weapons (grenades) use a manual trajectory, NOT Rigidbody.
---

# FPS Weapon System

Read **unity-mcp-ops** first. Weapons are the highest-leverage "feel" system in a shooter — small
numbers change everything, so expose them as serialized fields and tune by replay.

## Hitscan vs projectile — pick per weapon

- **Hitscan** (raycast, instant): rifles, pistols, SMGs. Fire a ray from the camera center, apply
  damage where it hits. Cheap, precise, what most shooters use.
- **Projectile** (travels over time): rockets, grenade launchers, slow "energy" weapons, anything
  where travel time and arc are part of the design.

Default to hitscan unless the design calls for visible travel.

## Weapon component layout

```
Main Camera
└── WeaponHolder            (empty; holds the current weapon, drives sway/bob)
    └── Rifle               (mesh + Weapon script + muzzle Transform child)
        └── MuzzlePoint     (empty at the barrel tip; VFX + ray origin for tracers)
```

Fire the damage ray from the **camera** (so it hits dead-center of the crosshair); spawn visual
tracers/flash from **MuzzlePoint** (so they look right). Mixing these up makes shots that visually
miss but register, or vice-versa.

## Hitscan firing (cached, rate-limited, no per-frame waste)

```csharp
using UnityEngine;

public class HitscanWeapon : MonoBehaviour
{
    [Header("Ballistics")]
    [SerializeField] private float damage = 20f;
    [SerializeField] private float range = 200f;
    [SerializeField] private float fireRate = 10f;        // shots per second
    [SerializeField] private float spread = 0.01f;        // radians of cone at full auto
    [SerializeField] private LayerMask hitMask;

    [Header("Ammo")]
    [SerializeField] private int magazineSize = 30;
    [SerializeField] private float reloadTime = 1.8f;

    [Header("Refs")]
    [SerializeField] private Camera fpsCamera;            // wire via update_component
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private GameObject impactPrefab;

    private float nextFireTime;
    private int ammo;
    private bool isReloading;

    private void Awake() => ammo = magazineSize;

    private void Update()
    {
        // Only cheap gating in Update; the actual shot is an event, not per-frame math.
        if (isReloading) return;

        if (Input.GetKey(KeyCode.Mouse0) && Time.time >= nextFireTime && ammo > 0)
        {
            nextFireTime = Time.time + 1f / fireRate;
            Fire();
        }
        if (Input.GetKeyDown(KeyCode.R) && ammo < magazineSize)
            StartCoroutine(Reload());
    }

    private void Fire()
    {
        ammo--;
        if (muzzleFlash) muzzleFlash.Play();

        // Cone spread applied to the camera-centered ray.
        Vector3 dir = fpsCamera.transform.forward;
        dir += (Vector3)(Random.insideUnitCircle * spread);
        dir.Normalize();

        if (Physics.Raycast(fpsCamera.transform.position, dir, out RaycastHit hit, range, hitMask))
        {
            if (hit.collider.TryGetComponent(out Health target))
                target.TakeDamage(damage);
            if (impactPrefab)
                Instantiate(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal));
        }
    }

    private System.Collections.IEnumerator Reload()
    {
        isReloading = true;
        yield return new WaitForSeconds(reloadTime);       // coroutine, not an Update timer
        ammo = magazineSize;
        isReloading = false;
    }
}
```

## Recoil that feels good

Recoil is a camera kick that recovers. Don't add it inside the raycast — drive a small
`recoilTarget` (pitch/yaw offset) on fire and Slerp the camera pivot back toward zero each frame.
Keep the pattern data-driven (a curve or a per-shot Vector2) so you can tune "spray" without code
changes. This lives on the camera pivot / a dedicated `RecoilController`, separate from firing.

## Spread & crosshair

Spread grows with sustained fire and while moving, shrinks when still/ADS. Feed the same `spread`
value to both the ray cone and the crosshair gap so what the player sees matches what they get.

## Thrown weapons (grenades) — manual trajectory, no Rigidbody

Per project convention, do NOT throw with `Rigidbody.AddForce`. Integrate the arc yourself so the
path is deterministic and tunable:

```csharp
// Called each frame while the grenade is in flight (from a coroutine or a lightweight mover):
velocity += Physics.gravity * Time.deltaTime;             // manual gravity
Vector3 next = transform.position + velocity * Time.deltaTime;
if (Physics.Linecast(transform.position, next, out RaycastHit hit, collisionMask))
    { transform.position = hit.point; Explode(); }        // detonate/bounce on contact
else
    transform.position = next;
```

This gives you full control over bounce, cook time, and blast without the solver's variability.

## Damage & health target

Weapons need something to hit. A minimal `Health` MonoBehaviour with `TakeDamage(float)`,
a `currentHealth`, and a death event is enough to close the loop and prove hit registration.

## Weapon switching

Keep weapons as children of `WeaponHolder`, enable one at a time. Store an index, cycle on scroll /
number keys, disable the old and enable the new (don't destroy/instantiate — pooling matters once
you have several). Reset `nextFireTime` on switch so you can't rate-bypass by swapping.

## "I shot but it didn't hit" — debugging

1. `hitMask` excludes the target's layer, or the target has no collider → fix layer/mask.
2. Ray originates from the muzzle (off-center) instead of the camera → fire damage ray from camera.
3. Target has no `Health` component → `TryGetComponent` returns false silently. Add one.
4. `Debug.DrawRay(fpsCamera.transform.position, dir * range, Color.red, 1f)` then replay and read
   the Scene view / logs to see where the ray actually goes.
