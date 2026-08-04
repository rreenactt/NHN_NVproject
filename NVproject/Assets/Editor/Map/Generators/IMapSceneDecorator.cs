using NV.Client.Map;
using UnityEngine;

namespace NV.Client.EditorTools.Generators
{
    /// <summary>
    /// An <see cref="IMapGenerator"/> that also has something to say about the runtime scene.
    ///
    /// Optional, and separate from <see cref="IMapGenerator"/> on purpose. Most levels have nothing
    /// to add — the test arena wants no fog, no hum and no flickering lamps, and giving every
    /// generator an empty method to implement would make "nothing to do here" look like an
    /// oversight. The Backrooms has three whole categories of thing that cannot be baked, so it
    /// implements this and says what to bolt on.
    /// </summary>
    public interface IMapSceneDecorator
    {
        /// <summary>
        /// Adds the runtime components a baked level of this kind needs.
        ///
        /// Called after the geometry is in place and the baked asset is wired, so the components
        /// added here can read it. Runs at bake time, so whatever is added is saved into the prefab.
        /// </summary>
        void Decorate(GameObject root, MapBlueprint blueprint);
    }
}
