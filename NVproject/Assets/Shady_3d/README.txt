================================================================
LOW POLY APOCALYPTIC SURVIVOR & ASSASSIN CHARACTER
Publisher : Shady_3d
Version   : 1.0
Unity     : 6000.3.6f1 (Unity 6)
Pipeline  : Universal Render Pipeline (URP) ONLY
================================================================

OVERVIEW
--------
A cold-blooded, low poly apocalyptic raider built for post-apocalyptic
games. Low poly aesthetic with modern 2K PBR textures.
Fully rigged with Unity Humanoid Mecanim support and 3 modular variants.

----------------------------------------------------------------

TECHNICAL SPECS
---------------
Triangles               : 6,137
Bones                   : 49 (Unity Humanoid Mecanim)
Skinned Mesh Renderers  : 4
Texture Resolutions     : 512, 1K and 2K (all included)
Pipeline                : URP only

----------------------------------------------------------------

VARIANTS (3)
------------
1. Helmet + Hood
2. Helmet Only
3. Gas Mask + Hood

To switch variants, toggle Skinned Mesh Renderers on the prefab:
  - Enable/disable HELMET or GAS MASK renderer
  - Enable/disable MAIN HOOD renderer accordingly

----------------------------------------------------------------

TEXTURE MAPS
------------
Unity Pack (Textures/Unity/):
  - Albedo
  - MetallicSmoothness (Metallic in RGB, Smoothness in Alpha)
  - Normal Map
  - Height Map
  - Emission Map (glowing eyes)

Unreal Pack (Textures/Unreal/):
  - ORM (Occlusion R, Roughness G, Metallic B)
  - Albedo
  - Normal Map
  - Height Map
  - Emission Map

Both packs available in 1K and 2K resolutions.

----------------------------------------------------------------

SETUP
-----
1. Drag ApocalypticRaider.prefab into your scene
2. Assign URP Lit materials using textures from Textures/Unity/2K/
   (or 1K for performance-sensitive projects)
3. In the Animator component, assign any Humanoid animation controller
4. Toggle SMRs on the prefab for variant switching (see VARIANTS)
5. Enable Emission on the eye material and assign the Emission map
   for glowing eye effect

----------------------------------------------------------------

KNOWN LIMITATIONS
-----------------
- Shoulder rotation beyond ~90 degrees causes mesh deformation.
  Keep shoulder animations within normal anatomical range.
- Vest mesh may clip through the body at extreme poses.
  Best used with standard humanoid animation ranges.

----------------------------------------------------------------

COMPATIBILITY
-------------
Tested         : Unity 6000.3.6f1
Pipeline       : URP ONLY
                 Not compatible with Built-in RP or HDRP
                 without manual material conversion.
Animations     : Compatible with any Unity Humanoid animation pack.
Mecanim        : Full Humanoid Avatar support. Works with Unity's
                 built-in IK pass.

----------------------------------------------------------------


SUPPORT
-------
Publisher : Shady_3d
shadnan.ahnaf5@gmail.com

================================================================
