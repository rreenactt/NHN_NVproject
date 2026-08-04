using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using NV.Client.EditorTools;
using NV.Client.Net;
using NV.Shared.Collision;
using UnityEngine;
using UnityEngine.TestTools;

namespace NV.Client.Tests
{
    /// <summary>
    /// EditMode tests for the **export gate** — the checks that decide whether a level may become
    /// a map file at all.
    ///
    /// **Why these live here rather than on the server.** The server's `ExportedMapTests` already
    /// checks the shipped map files, but it runs after the file is committed, and it can only tell
    /// you that the file you already have is broken. These tests check the gate itself: that a
    /// broken level is *refused* before anything is written. The two together are what stops a bad
    /// export from reaching the repository.
    ///
    /// The synthetic sources below are deliberately minimal — the point is one defect per source,
    /// so a failure names the defect rather than "the map is wrong somewhere".
    /// </summary>
    public sealed class MapExportValidationTests
    {
        [Test]
        public void 정상적인_레벨은_검사를_통과한다()
        {
            var data = MapExport.BuildMapData(new FlatRoom(), out var report);

            Assert.IsTrue(report.GridAttached, "격자가 실리지 않았다.");
            Assert.Greater(report.FreeFloorCells, 0, "몸이 들어가는 셀이 없다.");

            AssertClean(data);
        }

        /// <summary>
        /// A level may legitimately offer no grid — <c>test-room</c> does. The report has to say
        /// "not offered" rather than "rejected", because the exporter refuses the second and allows
        /// the first, and collapsing them would either block a valid map or wave a broken one
        /// through.
        /// </summary>
        [Test]
        public void 격자를_내놓지_않는_레벨도_통과하고_거절과_구별된다()
        {
            var data = MapExport.BuildMapData(new FlatRoom { OfferGrid = false }, out var report);

            Assert.IsFalse(report.GridOffered, "내놓지 않은 격자를 내놓았다고 보고했다.");
            Assert.IsNull(report.GridError);
            Assert.IsFalse(report.GridAttached);
            Assert.IsNull(data.Grid);

            AssertClean(data);
        }

        /// <summary>
        /// A grid whose cell count disagrees with its own dimensions is dropped, and the report
        /// carries the reason. Without that distinction the exporter would write a gridless file
        /// and the match would open with no keys and no door.
        /// </summary>
        [Test]
        public void 크기가_어긋난_격자는_거절되고_이유가_남는다()
        {
            // `AttachGrid` 는 이것을 콘솔 에러로도 남긴다. 그것이 의도이므로(런타임에서 격자가
            // 사라진 것을 사람이 알아야 한다) 테스트가 그 로그를 기대한다고 말해 둔다 —
            // 말하지 않으면 프레임워크가 예상 못한 에러 로그로 테스트를 실패시킨다.
            LogAssert.Expect(LogType.Error, new Regex("격자가 잘못됐다"));

            MapExport.BuildMapData(new FlatRoom { BreakGridSize = true }, out var report);

            Assert.IsTrue(report.GridOffered);
            Assert.IsNotNull(report.GridError, "격자를 버렸으면서 이유를 남기지 않았다.");
            Assert.IsFalse(report.GridAttached);
        }

        /// <summary>
        /// **This is the coordinate-system test, and the one that found a hole.** The grid is pushed
        /// clear of the level while its size, its flags and the map hash all stay valid.
        ///
        /// The interesting part is that <c>MarkFreeFloor</c> marks *every* one of those cells as
        /// walkable, because it only asks whether a player box overlaps geometry and empty space
        /// overlaps nothing. So a shifted grid does not show up as "no free floor" — it shows up as
        /// a full set of candidates hanging in the void, which is why the check has to also ask
        /// whether there is floor beneath.
        /// </summary>
        [Test]
        public void 지형_밖으로_밀린_격자는_발밑_검사에서_걸린다()
        {
            var data = MapExport.BuildMapData(new FlatRoom { ShiftGridOrigin = true }, out var report);

            Assert.IsTrue(report.GridAttached, "격자는 자기 크기와는 맞으므로 실려야 한다.");

            // 겹침만 보는 판정은 허공을 통과시킨다. 이 값이 0 이 아닌 것이 요점이다 —
            // 그래서 셀 수만으로는 이 결함을 알 수 없다.
            Assert.Greater(report.FreeFloorCells, 0,
                "허공의 셀이 겹침 판정을 통과하지 못했다. 이 테스트의 전제가 바뀌었다.");

            var errors = new List<string>();
            var warnings = new List<string>();

            MapDataValidator.InspectSimulation(data, errors, warnings);
            Assert.Greater(errors.Count, 0, "좌표계 어긋남을 검사가 통과시켰다.");
        }

        /// <summary>
        /// A spawn buried in geometry passes every schema check — the box list is well formed and
        /// the hash is stable. The simulation pass is the only thing that sees it, and it has to,
        /// because the symptom is a player wedged in a wall the moment they connect.
        /// </summary>
        [Test]
        public void 지형에_파묻힌_스폰은_검사에서_걸린다()
        {
            var data = MapExport.BuildMapData(new FlatRoom { BurySpawn = true }, out _);

            var schema = new List<string>();
            Assert.IsTrue(MapDataValidator.TryValidateSchema(data, schema),
                "스키마 검사는 통과해야 한다 — 이 결함은 스키마로 보이지 않는다.");

            var errors = new List<string>();
            var warnings = new List<string>();

            MapDataValidator.InspectSimulation(data, errors, warnings);
            Assert.Greater(errors.Count, 0, "파묻힌 스폰을 검사가 통과시켰다.");
        }

        /// <summary>
        /// A spawn with no floor under it also survives the schema pass. The check runs the real
        /// movement function for a few ticks rather than looking for a box below, because "is there
        /// floor here" is a question only the simulation answers the same way the server will.
        /// </summary>
        [Test]
        public void 바닥이_없는_스폰은_검사에서_걸린다()
        {
            var data = MapExport.BuildMapData(new FlatRoom { SpawnOffTheFloor = true }, out _);

            var errors = new List<string>();
            var warnings = new List<string>();

            MapDataValidator.InspectSimulation(data, errors, warnings);
            Assert.Greater(errors.Count, 0, "허공의 스폰을 검사가 통과시켰다.");
        }

        /// <summary>
        /// A box with min past max is silently skipped by the sweep, so the wall it describes simply
        /// is not there. This one *is* visible to the schema pass, which is why the server refuses to
        /// start on it — the test pins that the client's copy of the check sees it too.
        ///
        /// It is built by hand rather than through a level, because <see cref="Bounds"/> normalises a
        /// negative size and so cannot express the defect. That is worth knowing: the export path
        /// cannot produce this file, but a hand-edited or copied one can, and both reach the server.
        /// </summary>
        [Test]
        public void 뒤집힌_박스는_스키마_검사에서_걸린다()
        {
            var data = new MapData
            {
                Name = "flipped",
                Boxes = new[]
                {
                    new MapBox { MinX = 1f, MinY = 0f, MinZ = 1f, MaxX = -1f, MaxY = 1f, MaxZ = 2f },
                },
                Spawns = new[]
                {
                    new MapSpawn { X = 0f, Y = 0f, Z = 0f, Yaw = 0f },
                },
            };

            var errors = new List<string>();
            Assert.IsFalse(MapDataValidator.TryValidateSchema(data, errors));
            Assert.Greater(errors.Count, 0);
        }

        /// <summary>
        /// Two exports of the same level must produce the same hash. If they do not, every connect
        /// reports a map-hash mismatch and the cause is invisible — this is what
        /// <c>DescribeExportBlocker</c> exists to refuse, and this test proves the honest case.
        /// </summary>
        [Test]
        public void 같은_레벨을_두_번_export_하면_해시가_같다()
        {
            var first = MapExport.BuildMapData(new FlatRoom()).ComputeHash();
            var second = MapExport.BuildMapData(new FlatRoom()).ComputeHash();

            Assert.AreEqual(first, second);
        }

        /// <summary>
        /// The scene scan must report *all* sources. Returning the first one is what let a scene
        /// holding two levels with the same <c>MapName</c> export whichever the scan happened to
        /// reach first — and only one of them offered a grid.
        /// </summary>
        [Test]
        public void 씬의_레벨을_하나만_돌려주지_않는다()
        {
            var found = new List<INetworkMapSource>();
            MapExport.FindAllInScene(found);

            // 이 테스트 씬에는 레벨이 없다. 요점은 목록을 채우는 API 가 있다는 것과, 하나만
            // 고르는 경로가 남아 있지 않다는 것이다.
            Assert.IsNotNull(found);
            Assert.AreEqual(0, found.Count);
            Assert.IsNull(MapExport.FindInScene());
        }

        // ==================================================== 씬 볼륨

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void DestroySpawned()
        {
            for (var index = 0; index < _spawned.Count; index++)
            {
                if (_spawned[index] != null)
                {
                    Object.DestroyImmediate(_spawned[index]);
                }
            }

            _spawned.Clear();
        }

        /// <summary>
        /// A marked box collider reaches the box list. Without this the server never learns about
        /// anything placed by hand in the scene, and the symptom is being blocked by nothing (or
        /// walking through something) while the map hash still matches — the hash only sees the
        /// exported list, so it cannot catch terrain that was never exported.
        /// </summary>
        [Test]
        public void 표시된_씬_볼륨이_박스_목록에_들어간다()
        {
            var withoutVolume = MapExport.BuildMapData(new FlatRoom(), out var before);

            Volume(new Vector3(4f, 0.5f, 4f), new Vector3(1f, 1f, 1f), Quaternion.identity);

            var withVolume = MapExport.BuildMapData(new FlatRoom(), out var after);

            Assert.AreEqual(0, before.SceneVolumes);
            Assert.AreEqual(1, after.SceneVolumes);
            Assert.AreEqual(withoutVolume.Boxes.Length + 1, withVolume.Boxes.Length);
            Assert.AreNotEqual(withoutVolume.ComputeHash(), withVolume.ComputeHash());
        }

        /// <summary>
        /// A rotated volume is skipped and reported. Skipping is the only choice that keeps client and
        /// server agreeing — the server knows nothing but the exported list, so leaving it out on both
        /// sides is consistent. The export refusing is what stops such a scene from shipping.
        /// </summary>
        [Test]
        public void 회전한_씬_볼륨은_건너뛰고_보고된다()
        {
            Volume(new Vector3(4f, 0.5f, 4f), new Vector3(1f, 1f, 1f), Quaternion.Euler(0f, 30f, 0f));

            MapExport.BuildMapData(new FlatRoom(), out var report);

            Assert.AreEqual(0, report.SceneVolumes, "회전한 볼륨이 실렸다.");
            Assert.IsNotNull(report.RejectedVolumes, "건너뛰면서 이유를 남기지 않았다.");
            Assert.AreEqual(1, report.RejectedVolumes.Count);
        }

        /// <summary>
        /// **This is the determinism test, and it is why the volumes are sorted.**
        ///
        /// <c>FindObjectsByType</c> does not specify its order, and the box list's order goes straight
        /// into the map hash (<c>ComputeHash</c> folds the boxes in sequence). Without a sort, the file
        /// exported from a scene and the hash the client computes at runtime could disagree from run to
        /// run, and the symptom is a map-hash mismatch that does not reproduce.
        /// </summary>
        [Test]
        public void 씬_볼륨의_순서가_해시를_바꾸지_않는다()
        {
            Volume(new Vector3(-6f, 0.5f, -6f), Vector3.one, Quaternion.identity);
            Volume(new Vector3(6f, 0.5f, 6f), Vector3.one, Quaternion.identity);
            Volume(new Vector3(0f, 0.5f, 6f), Vector3.one, Quaternion.identity);

            var first = MapExport.BuildMapData(new FlatRoom()).ComputeHash();

            DestroySpawned();

            // 반대 순서로 만든다. 스캔 순서가 만든 순서를 따르는 구현에서는 이것이 다른
            // 순서를 준다 — 정렬이 없으면 해시가 달라진다.
            Volume(new Vector3(0f, 0.5f, 6f), Vector3.one, Quaternion.identity);
            Volume(new Vector3(6f, 0.5f, 6f), Vector3.one, Quaternion.identity);
            Volume(new Vector3(-6f, 0.5f, -6f), Vector3.one, Quaternion.identity);

            var second = MapExport.BuildMapData(new FlatRoom()).ComputeHash();

            Assert.AreEqual(first, second);
        }

        /// <summary>
        /// A volume standing on a walkable cell must remove it from the placement candidates. That is
        /// the whole reason the volumes are appended before the grid is attached — <c>FreeFloor</c> is
        /// judged against the box list, so a prop added after would not block anything.
        /// </summary>
        [Test]
        public void 씬_볼륨이_그_자리의_배치_후보를_없앤다()
        {
            MapExport.BuildMapData(new FlatRoom(), out var before);

            // 셀 중심은 origin(-12) + (n + 0.5) × 3 이므로 (-10.5, 0, -10.5) 가 한 셀의 중심이다.
            Volume(new Vector3(-10.5f, 1f, -10.5f), new Vector3(2f, 2f, 2f), Quaternion.identity);

            MapExport.BuildMapData(new FlatRoom(), out var after);

            Assert.AreEqual(before.FreeFloorCells - 1, after.FreeFloorCells);
        }

        private void Volume(Vector3 position, Vector3 size, Quaternion rotation)
        {
            var host = new GameObject("volume");
            host.transform.SetPositionAndRotation(position, rotation);

            var collider = host.AddComponent<BoxCollider>();
            collider.size = size;

            host.AddComponent<NVCollisionVolume>();

            _spawned.Add(host);
        }

        // ==================================================== 스키마 드리프트

        /// <summary>
        /// **Every writable property on the schema types must appear in the exported JSON.**
        ///
        /// The serialiser writes key names as string literals, because <c>Shared</c> cannot reference
        /// <c>System.Text.Json</c> — Unity has no such assembly. That means adding a property and
        /// forgetting the writer compiles cleanly, and the server then reads the field as its default.
        /// The symptom is the feature that needed it simply not working, with nothing to point at.
        ///
        /// The rule is <c>CanWrite</c>: a property with a setter is part of the file, a getter-only
        /// property is computed (<c>HasGrid</c>, <c>CellCount</c>) and must not be written. That is
        /// not a convention invented for this test — it is what the deserialiser on the server side
        /// can actually populate.
        /// </summary>
        [Test]
        public void 스키마의_모든_쓰기_가능_프로퍼티가_JSON_에_쓰인다()
        {
            var data = MapExport.BuildMapData(new FlatRoom());

            data.Version = MapSchema.Current;
            data.Source = new MapSourceInfo
            {
                Scene = "scene",
                Component = "component",
                ExportedAtUtc = "1970-01-01T00:00:00Z",
                ExporterVersion = MapExportPipeline.ExporterVersion,
            };

            var json = MapExportPipeline.Serialize(data);

            AssertEveryWritablePropertyIsWritten(typeof(MapData), json);
            AssertEveryWritablePropertyIsWritten(typeof(MapBox), json);
            AssertEveryWritablePropertyIsWritten(typeof(MapSpawn), json);
            AssertEveryWritablePropertyIsWritten(typeof(MapGridData), json);
            AssertEveryWritablePropertyIsWritten(typeof(MapSourceInfo), json);
        }

        /// <summary>
        /// A getter-only property must NOT be written. The server cannot deserialise into one, so a
        /// key it does not recognise is either ignored (best case, dead bytes) or — with a stricter
        /// option set later — a parse failure at startup.
        /// </summary>
        [Test]
        public void 계산되는_프로퍼티는_JSON_에_쓰이지_않는다()
        {
            var json = MapExportPipeline.Serialize(MapExport.BuildMapData(new FlatRoom()));

            Assert.IsFalse(json.Contains("\"hasGrid\""), "HasGrid 는 계산되는 값이다.");
            Assert.IsFalse(json.Contains("\"cellCount\""), "CellCount 는 계산되는 값이다.");
        }

        private static void AssertEveryWritablePropertyIsWritten(System.Type type, string json)
        {
            foreach (var property in type.GetProperties())
            {
                if (!property.CanWrite)
                {
                    continue;
                }

                var key = "\"" + char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1) + "\"";

                Assert.IsTrue(
                    json.Contains(key),
                    $"{type.Name}.{property.Name} 이 JSON 에 {key} 로 쓰이지 않았다. " +
                    "프로퍼티를 늘리고 직렬화를 잊으면 서버가 그 필드를 기본값으로 읽는다.");
            }
        }

        private static void AssertClean(MapData data)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            Assert.IsTrue(MapDataValidator.TryValidateSchema(data, errors),
                string.Join("; ", errors));

            MapDataValidator.InspectSimulation(data, errors, warnings);

            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
        }

        /// <summary>
        /// One open room with a floor slab and four walls, plus a grid over the interior. Each flag
        /// injects exactly one defect so a failing test names it.
        ///
        /// It is a plain class, not a MonoBehaviour: nothing here needs a scene, and building the
        /// data is what the exporter actually does.
        /// </summary>
        private sealed class FlatRoom : INetworkMapSource
        {
            private const float Half = 12f;
            private const float WallHeight = 4f;
            private const int Cells = 8;
            private const float CellSize = 3f;

            public bool OfferGrid { get; set; } = true;
            public bool BreakGridSize { get; set; }
            public bool ShiftGridOrigin { get; set; }
            public bool BurySpawn { get; set; }
            public bool SpawnOffTheFloor { get; set; }

            public string MapName => "flat-room";

            public IReadOnlyList<Bounds> CollisionBoxes => new List<Bounds>();

            public IReadOnlyList<Bounds> ComputeCollision()
            {
                var boxes = new List<Bounds>
                {
                    // 바닥 슬래브. 윗면이 y = 0 이다 — 서버의 위치 규약이 발밑 기준이다.
                    new Bounds(new Vector3(0f, -0.1f, 0f), new Vector3(Half * 2f, 0.2f, Half * 2f)),

                    new Bounds(new Vector3(0f, WallHeight * 0.5f, Half),
                        new Vector3(Half * 2f, WallHeight, 0.5f)),
                    new Bounds(new Vector3(0f, WallHeight * 0.5f, -Half),
                        new Vector3(Half * 2f, WallHeight, 0.5f)),
                    new Bounds(new Vector3(Half, WallHeight * 0.5f, 0f),
                        new Vector3(0.5f, WallHeight, Half * 2f)),
                    new Bounds(new Vector3(-Half, WallHeight * 0.5f, 0f),
                        new Vector3(0.5f, WallHeight, Half * 2f)),
                };

                if (BurySpawn)
                {
                    // 스폰 자리를 막는 기둥. 콜리전으로는 정상인 박스다.
                    boxes.Add(new Bounds(new Vector3(0f, WallHeight * 0.5f, 0f),
                        new Vector3(2f, WallHeight, 2f)));
                }

                return boxes;
            }

            public void GetSpawns(List<(Vector3 position, float yaw)> into)
            {
                var y = SpawnOffTheFloor ? 40f : 0f;

                into.Add((new Vector3(0f, y, 0f), 0f));
                into.Add((new Vector3(3f, y, 3f), 0f));
            }

            public MapGridData BuildGrid()
            {
                if (!OfferGrid)
                {
                    return null;
                }

                var origin = ShiftGridOrigin ? Half * 4f : -(Cells * CellSize * 0.5f);

                var grid = new MapGridData
                {
                    Floors = 1,
                    Width = Cells,
                    Depth = Cells,
                    CellSize = CellSize,
                    FloorHeight = 3.2f,
                    OriginX = origin,
                    OriginZ = origin,
                    Cells = new byte[BreakGridSize ? (Cells * Cells) - 1 : Cells * Cells],
                };

                for (var index = 0; index < grid.Cells.Length; index++)
                {
                    grid.Cells[index] = (byte)MapCellFlags.Standable;
                }

                return grid;
            }

            public string DescribeExportBlocker() => null;
        }
    }
}
