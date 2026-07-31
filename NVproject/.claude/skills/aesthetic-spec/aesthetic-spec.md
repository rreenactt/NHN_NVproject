# Backrooms Aesthetic Spec

The liminal "Backrooms" feeling comes from a few specific, consistent choices.
Apply all of them — the mood is what separates this from a generic gray maze.
Values are starting points tuned for Unity 6 (URP or Built-in); adjust to taste
but keep the *relationships* (warm, dim, flat, humming, slightly damp).

## Palette

| Surface        | Base color (hex) | Notes |
|----------------|------------------|-------|
| Walls          | `#C9B36B`        | Mono-yellow damp wallpaper. Slightly uneven, low-gloss. |
| Wall trim/base | `#A8924E`        | Darker skirting where wall meets carpet. |
| Carpet floor   | `#8A7F52`        | Worn, muddy yellow-brown, matte, faint stains. |
| Ceiling tiles  | `#D8CFA8`        | Yellowed drop-ceiling panels, matte. |
| Fluorescent    | `#FFF6D6` emissive| Warm-white tubes, slightly green-yellow tint. |

Keep everything **low saturation and warm**. No pure whites, no cool blues, no
strong colored accents. The monotony is the point.

## Materials

- **Walls**: matte, roughness ~0.85, metallic 0. Add a subtle tiling wallpaper
  normal/albedo texture with faint water stains if available; flat color is an
  acceptable placeholder.
- **Carpet**: roughness ~0.95, metallic 0. A low-contrast noise texture sells
  the "old office carpet" read far better than flat color.
- **Ceiling**: matte panels with a faint grid seam every cell so the dropped-tile
  grid is legible.
- Avoid glossy or reflective surfaces entirely — reflections break the flat,
  airless feeling.

## Lighting

- **Fluorescent panels** on a regular grid (every `lightSpacing` cells), mounted
  flush in the ceiling. Use **real-time or mixed** lights, warm-white
  (`#FFF6D6`), range ~`cellSize * 2.5`, intensity moderate (not bright — the
  space should feel dim and endless).
- **Flicker**: give ~15–20% of the lights a subtle flicker/buzz script
  (randomized intensity dips). Don't flicker all of them — occasional is eerier
  than constant.
- **Ambient light**: low, warm, flat. In URP set ambient to a dim yellow-gray;
  avoid strong directional light (there is no sun in the Backrooms).
- **Shadows**: soft, short. Hard black shadows read as "dungeon"; keep them faint.

## Post-processing / environment

- **Fog**: enabled, warm gray-yellow (`#B7AC7E`), light density so distant
  corridors fade — this reinforces the "endless" feeling and hides the grid edge.
- **Color grading**: pull saturation down ~15–20%, push a faint yellow tint,
  slightly lift the blacks (nothing is truly dark). Mild film grain if available.
- **Vignette**: very subtle. Overdoing it looks like a horror filter; the
  Backrooms unease is flat and fluorescent, not cinematic.

## Audio

- **Ambient hum**: a constant low fluorescent buzz / HVAC drone on a looping,
  spatial-blend-0 (2D) AudioSource parented to the map root. This single sound
  does enormous work for the mood.
- Optional: occasional distant creaks/room-tone one-shots on a long random timer.
- Footsteps on carpet should be soft and muffled, not sharp.

## Ceiling height & proportions

- Ceiling height ~`floorHeight - 0.2` (default ~3.0 m) — low enough to feel
  enclosed, not cramped.
- Doorway openings between rooms/corridors are **open passages** (no doors),
  ~1 cell wide, full height minus a small header. Doorless openings keep the
  maze-like flow.
- Pillars: optional square pillars in larger rooms reinforce the office-grid
  look and break sightlines.

## Quick checklist

- [ ] Walls mono-yellow, matte, no gloss
- [ ] Carpet worn yellow-brown, matte
- [ ] Dropped ceiling with visible tile grid
- [ ] Fluorescent grid lighting, warm-white, dim
- [ ] Some lights flicker/buzz
- [ ] Warm fog fading distant halls
- [ ] Saturation reduced, yellow tint
- [ ] Constant low hum ambience
- [ ] No pure white, no cool colors, no reflections
