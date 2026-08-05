using NUnit.Framework;
using NV.Client.EditorTools.Generators;
using NV.Client.Map;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.EditorTools.Tests
{
    /// <summary>
    /// The Backrooms V2 solver's contract, pinned.
    ///
    /// Determinism is checked byte-for-byte because that is what the map hash sees; the layout
    /// invariants (connectivity, spawn count, the altar's clear centre) are checked across many
    /// seeds because a solver bug that shows on one seed in fifty would otherwise ship the first
    /// time somebody rolls an unlucky number.
    /// </summary>
    public class BackroomsV2GeneratorTests
    {
        private const int SeedSweep = 100;

        private static MapBlueprint Generate(int seed)
        {
            var generator = new BackroomsV2Generator();
            var settings = ScriptableObject.CreateInstance<BackroomsV2Settings>();

            try
            {
                settings.mapName = generator.DefaultMapName;
                settings.randomizeSeed = false;
                settings.seed = seed;

                return generator.Generate(settings);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SameSeedProducesIdenticalLevel()
        {
            var first = Generate(4242);
            var second = Generate(4242);

            Assert.AreEqual(first.Pieces.Count, second.Pieces.Count);

            for (var index = 0; index < first.Pieces.Count; index++)
            {
                Assert.AreEqual(first.Pieces[index].Bounds, second.Pieces[index].Bounds,
                    $"piece {index} ({first.Pieces[index].Name}) drifted between runs");
                Assert.AreEqual(first.Pieces[index].Collides, second.Pieces[index].Collides);
            }

            Assert.AreEqual(first.Spawns.Count, second.Spawns.Count);

            for (var index = 0; index < first.Spawns.Count; index++)
            {
                Assert.AreEqual(first.Spawns[index].Position, second.Spawns[index].Position);
                Assert.AreEqual(first.Spawns[index].Yaw, second.Spawns[index].Yaw);
            }

            Assert.AreEqual(first.Grid.Cells, second.Grid.Cells);
        }

        [Test]
        public void DifferentSeedsProduceDifferentLevels()
        {
            var first = Generate(1);
            var second = Generate(2);

            var identical = first.Pieces.Count == second.Pieces.Count;

            if (identical)
            {
                for (var index = 0; index < first.Pieces.Count; index++)
                {
                    if (first.Pieces[index].Bounds != second.Pieces[index].Bounds)
                    {
                        identical = false;
                        break;
                    }
                }
            }

            Assert.IsFalse(identical, "two seeds solved to the same level — the seed is not reaching the solver");
        }

        [Test]
        public void EverySeedConnectsAndKeepsTheContract()
        {
            for (var seed = 0; seed < SeedSweep; seed++)
            {
                var blueprint = Generate(seed);

                // A non-null blocker here is the solver's own connectivity flood-fill (or a
                // failed pass) reporting a bug — the settings offer it no reason to refuse.
                Assert.IsNull(blueprint.Blocker, $"seed {seed}: {blueprint.Blocker}");

                Assert.AreEqual(8, blueprint.Spawns.Count,
                    $"seed {seed}: the server picks spawns by PlayerId and the exported map tests assert eight");

                Assert.IsNotNull(blueprint.Grid, $"seed {seed}: no walkability grid, no match");
                Assert.IsTrue(blueprint.Grid.TryValidate(out var gridError), $"seed {seed}: {gridError}");

                var colliding = blueprint.CollidingPieceCount;
                Assert.LessOrEqual(colliding, 1100,
                    $"seed {seed}: {colliding} colliding boxes crosses the validator's review threshold");
                Assert.GreaterOrEqual(colliding, 50,
                    $"seed {seed}: {colliding} colliding boxes — the interior did not generate");
            }
        }

        [Test]
        public void SpawnsStandOnOpenCellsFacingIn()
        {
            for (var seed = 0; seed < SeedSweep; seed += 10)
            {
                var blueprint = Generate(seed);
                var grid = blueprint.Grid;

                foreach (var spawn in blueprint.Spawns)
                {
                    Assert.IsTrue(
                        grid.TryWorldToCell(
                            new System.Numerics.Vector3(spawn.Position.x, spawn.Position.y, spawn.Position.z),
                            out var floor, out var x, out var z),
                        $"seed {seed}: spawn {spawn.Position} is outside the grid");

                    Assert.IsTrue(grid.Has(floor, x, z, MapCellFlags.Standable),
                        $"seed {seed}: spawn {spawn.Position} sits on a blocked cell");

                    Assert.GreaterOrEqual(spawn.Yaw, 0f);
                    Assert.Less(spawn.Yaw, 2f * Mathf.PI);
                }
            }
        }

        [Test]
        public void GridCentreStaysClearForTheAltar()
        {
            for (var seed = 0; seed < SeedSweep; seed += 10)
            {
                var blueprint = Generate(seed);
                var grid = blueprint.Grid;
                var centre = grid.Width / 2;

                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        Assert.IsTrue(grid.Has(0, centre + dx, centre + dz, MapCellFlags.Standable),
                            $"seed {seed}: cell ({centre + dx}, {centre + dz}) near the grid centre is blocked — " +
                            "the altar searches outward from there and needs standable ground");
                    }
                }
            }
        }

        [Test]
        public void GridMatchesTheDeclaredShape()
        {
            var blueprint = Generate(7);
            var grid = blueprint.Grid;

            Assert.AreEqual(1, grid.Floors, "V2 is single-storey by design — no StairLink, no shaft");
            Assert.AreEqual(grid.Floors * grid.Width * grid.Depth, grid.Cells.Length);

            var standable = 0;
            foreach (var cell in grid.Cells)
                if ((cell & (byte)MapCellFlags.Standable) != 0) standable++;

            // FreeFloor is computed at export from these — the match needs 64 spread cells and
            // the sweep above already proves connectivity, so a healthy majority must be open.
            Assert.GreaterOrEqual(standable, grid.Width * grid.Depth / 2,
                $"only {standable} of {grid.Cells.Length} cells standable");

            foreach (var cell in grid.Cells)
                Assert.AreEqual(0, cell & (byte)MapCellFlags.StairLink, "single storey must not link stairs");
        }
    }
}
