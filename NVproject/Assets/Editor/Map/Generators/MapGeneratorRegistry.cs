using System;
using System.Collections.Generic;
using NV.Client.Map;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools.Generators
{
    /// <summary>
    /// Every <see cref="IMapGenerator"/> in the project, found by type.
    ///
    /// **Found rather than listed.** An attribute or a hand-kept array is one more thing to forget,
    /// and forgetting it fails as "my new generator is not in the dropdown" — which reads as the
    /// tool being broken rather than as a missing registration. <c>TypeCache</c> is indexed at
    /// compile time, so this costs nothing.
    ///
    /// That is not in tension with <c>MapSceneTable</c> keeping its map↔scene pairs in code. That
    /// table is the source of a *pairing* nothing else knows; this is a list of types, which the
    /// compiler already knows.
    /// </summary>
    public static class MapGeneratorRegistry
    {
        private static List<IMapGenerator> _generators;

        public static IReadOnlyList<IMapGenerator> All => _generators ?? (_generators = Discover());

        /// <summary>Display names, in the order <see cref="All"/> holds them. For the dropdown.</summary>
        public static string[] DisplayNames()
        {
            var names = new string[All.Count];
            for (var index = 0; index < All.Count; index++) names[index] = All[index].DisplayName;
            return names;
        }

        /// <summary>
        /// A settings instance this generator can read, with the name it normally goes by.
        ///
        /// Not saved as an asset. The window works on a loose instance until somebody chooses to
        /// keep one, which means opening the tool never writes to the project.
        /// </summary>
        public static MapGeneratorSettings CreateSettings(IMapGenerator generator)
        {
            if (generator == null) return null;

            var settings = ScriptableObject.CreateInstance(generator.SettingsType) as MapGeneratorSettings;

            if (settings == null)
            {
                Debug.LogError($"[NV] {generator.GetType().Name}.SettingsType 이 " +
                               $"{generator.SettingsType?.Name} 인데 MapGeneratorSettings 가 아니다.");
                return null;
            }

            settings.name = generator.SettingsType.Name;
            settings.mapName = generator.DefaultMapName;

            return settings;
        }

        /// <summary>The generator that reads this settings type, or <c>null</c>.</summary>
        public static IMapGenerator ForSettings(MapGeneratorSettings settings)
        {
            if (settings == null) return null;

            for (var index = 0; index < All.Count; index++)
                if (All[index].SettingsType.IsInstanceOfType(settings)) return All[index];

            return null;
        }

        private static List<IMapGenerator> Discover()
        {
            var found = new List<IMapGenerator>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<IMapGenerator>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Debug.LogWarning($"[NV] {type.Name} 은 IMapGenerator 이지만 기본 생성자가 없어 " +
                                     "목록에 넣지 않는다.");
                    continue;
                }

                found.Add((IMapGenerator)Activator.CreateInstance(type));
            }

            // Stable order, so the dropdown does not reshuffle between domain reloads and the
            // index the window remembers keeps meaning the same generator.
            found.Sort((left, right) => string.CompareOrdinal(left.DisplayName, right.DisplayName));

            return found;
        }
    }
}
