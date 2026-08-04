using System;
using NV.Client.Map;

namespace NV.Client.EditorTools.Generators
{
    /// <summary>
    /// Turns settings into a level. **Makes no <see cref="UnityEngine.Object"/> doing it.**
    ///
    /// That restriction is the whole design. A generator that creates GameObjects can only run
    /// where dumping a level into the open scene is acceptable, which is why the existing
    /// <c>BackroomsMapGenerator</c> carries a second collision-only path through its geometry pass,
    /// kept in step with the first by replaying the same seeded random in the same order — a
    /// contract that lives in a comment and nowhere else. A generator that only produces data has
    /// one path, and what to do with the result is somebody else's problem.
    ///
    /// Adding a generator is one file. <see cref="MapGeneratorRegistry"/> finds it by type, so
    /// there is no list to forget to update.
    /// </summary>
    public interface IMapGenerator
    {
        /// <summary>What the type dropdown shows.</summary>
        string DisplayName { get; }

        /// <summary>
        /// The map name a fresh settings instance starts with.
        ///
        /// Settings do not default their own name — a blank preset that silently claimed an
        /// existing map's name would overwrite that map's file on the first export. The generator
        /// is the one place that knows what this kind of level is normally called.
        /// </summary>
        string DefaultMapName { get; }

        /// <summary>The settings type this generator reads. Must derive from <see cref="MapGeneratorSettings"/>.</summary>
        Type SettingsType { get; }

        /// <summary>
        /// Solves the level.
        ///
        /// Must be deterministic for given settings — apart from
        /// <see cref="MapGeneratorSettings.randomizeSeed"/>, which is precisely why that flag
        /// blocks export.
        /// </summary>
        MapBlueprint Generate(MapGeneratorSettings settings);
    }
}
