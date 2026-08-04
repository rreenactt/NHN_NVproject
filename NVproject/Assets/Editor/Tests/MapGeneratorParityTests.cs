using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NV.Client.EditorTools;
using NV.Client.EditorTools.Generators;
using NV.Client.Map;
using NV.Client.Net;
using UnityEngine;

namespace NV.Client.Tests
{
    /// <summary>
    /// **The completion test for moving map generation into the editor.**
    ///
    /// A level built the new way — settings → generator → blueprint → baked asset →
    /// <see cref="BakedMapSource"/> → <c>MapExport</c> → serialization — has to describe exactly
    /// the same terrain as the level built the old way, which is the terrain sitting in the
    /// committed <c>NVserver/MapData/test-room.json</c>.
    ///
    /// **Why compare against the file rather than against <c>TestRoomMap</c>.** The file is what
    /// the server actually judges movement with. It also costs nothing to check: comparing it puts
    /// the box order, every float, the spawn ring and the absence of a grid under one assertion,
    /// and a single wrong digit anywhere shows up as a named line rather than as a hash that
    /// differs for no visible reason. Instantiating <c>TestRoomMap</c> instead would run its
    /// <c>Awake</c>, which builds a whole level into the test scene and rewrites
    /// <c>RenderSettings</c>.
    ///
    /// **Only the terrain sections are compared, not the whole file.** The committed file predates
    /// the schema version and the provenance block, so it carries neither. Those two are excluded
    /// from the map hash for exactly the same reason they are excluded here — they are not terrain.
    /// </summary>
    public sealed class MapGeneratorParityTests
    {
        private const string GoldenDirectory = "/../../NVserver/MapData/";

        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = 0; index < _spawned.Count; index++)
            {
                if (_spawned[index] != null) Object.DestroyImmediate(_spawned[index]);
            }

            _spawned.Clear();
        }

        [Test]
        public void 테스트룸_생성기가_배포된_맵과_같은_지형을_내놓는다()
        {
            var golden = ReadGolden("test-room");
            var produced = MapExportPipeline.Serialize(BuildThroughNewPipeline());

            AssertSectionsMatch(golden, produced, "boxes");
            AssertSectionsMatch(golden, produced, "spawns");
        }

        /// <summary>
        /// The same check for the level that actually matters.
        ///
        /// **This is the assertion the Backrooms port exists to pass.** Its grid solver draws from
        /// one seeded <c>System.Random</c>, and a single extra or missing draw — a rejected room
        /// candidate that stops consuming its four values, a loop roll moved inside a branch —
        /// produces a completely different but perfectly plausible level. Nothing about the result
        /// looks wrong; the terrain simply stops being the terrain the server was given.
        ///
        /// The grid rides along: it is in the map hash whenever it is present, so a solver that
        /// drifted would show up here even if every box happened to land in the same place.
        /// </summary>
        [Test]
        public void 백룸_생성기가_배포된_맵과_같은_지형을_내놓는다()
        {
            var golden = ReadGolden("backrooms");
            var data = BuildBackrooms();
            var produced = MapExportPipeline.Serialize(data);

            AssertSectionsMatch(golden, produced, "boxes");
            AssertSectionsMatch(golden, produced, "spawns");

            Assert.AreEqual(GridCells(golden), GridCells(produced),
                "격자 셀이 다르다. 격자는 있을 때 맵 해시에 들어가므로 이것만 어긋나도 접속이 경고를 낸다.");
        }

        /// <summary>
        /// Everything else <c>MapData.ComputeHash</c> mixes in: the name, and the grid's shape and
        /// origin.
        ///
        /// **Checked as inputs rather than by recomputing the shipped file's hash**, which would
        /// need a second reader for this schema — and refusing to have one of those is a deliberate
        /// decision the export pipeline already made, for the good reason that a second reader is a
        /// second place for the schema to drift. Together with the boxes and cells above, these are
        /// the whole of the hash: matching all of them is matching it.
        /// </summary>
        [Test]
        public void 백룸_생성기가_해시에_들어가는_나머지_값도_맞춘다()
        {
            var golden = ReadGolden("backrooms");
            var data = BuildBackrooms();

            var produced = MapExportPipeline.Serialize(data);

            Assert.AreEqual("backrooms", data.Name);
            StringAssert.Contains("\"name\": \"backrooms\"", golden);

            foreach (var field in new[] { "floors", "width", "depth", "cellSize", "floorHeight", "originX", "originZ" })
            {
                Assert.AreEqual(Scalar(golden, field), Scalar(produced, field), $"격자의 {field} 가 다르다.");
            }
        }

        /// <summary>
        /// The arena offers no grid, and that is the right answer rather than a gap: it never runs
        /// the match rules, so nothing asks where a player can stand. Filling one in would be worse
        /// than useless — a blanket "all cells walkable" grid over a room with a centre platform and
        /// four cover blocks would declare the inside of those blocks to be floor.
        ///
        /// It also matters to the hash: a map with no grid contributes nothing extra to it, which is
        /// what keeps <c>test-room.json</c> stable.
        /// </summary>
        [Test]
        public void 테스트룸은_격자를_내놓지_않는다()
        {
            var data = BuildThroughNewPipeline();

            Assert.IsNull(data.Grid, "테스트 룸이 격자를 내놓았다. 커버 블록 안쪽이 바닥이 된다.");
            Assert.IsFalse(MapExportPipeline.Serialize(data).Contains("\"grid\""),
                "격자가 없는데 grid 블록이 쓰였다.");
        }

        /// <summary>
        /// Nothing about the arena draws randomness, so the seed switch cannot make two generations
        /// differ — and refusing the export over it would be a lie that costs somebody an afternoon.
        /// The empty-name case is a different matter and still refuses.
        /// </summary>
        [Test]
        public void 난수를_뽑지_않는_레벨은_씨드_무작위화가_export_를_막지_않는다()
        {
            var settings = NewSettings();
            settings.randomizeSeed = true;

            Assert.IsNull(new TestRoomGenerator().Generate(settings).Blocker);

            settings.mapName = string.Empty;
            Assert.IsNotNull(new TestRoomGenerator().Generate(settings).Blocker,
                "이름이 없는데 통과했다. 그 파일은 어디에도 쓸 수 없다.");
        }

        /// <summary>
        /// The registry finds generators by type rather than from a list, so a new one appears in
        /// the window by existing. This is the assertion that catches a generator with no default
        /// constructor, which the registry can only skip.
        /// </summary>
        [Test]
        public void 생성기_목록이_테스트룸을_담는다()
        {
            var found = false;

            for (var index = 0; index < MapGeneratorRegistry.All.Count; index++)
            {
                if (MapGeneratorRegistry.All[index] is TestRoomGenerator) found = true;
            }

            Assert.IsTrue(found, "TypeCache 가 TestRoomGenerator 를 찾지 못했다.");

            var settings = MapGeneratorRegistry.CreateSettings(new TestRoomGenerator());
            Assert.IsInstanceOf<TestRoomSettings>(settings);
            Assert.AreEqual("test-room", settings.mapName,
                "새 설정이 기본 맵 이름을 갖지 않는다. 빈 이름은 export 에서 거절된다.");

            _spawned.Add(settings);
        }

        // ==================================================== 도구

        /// <summary>
        /// Runs the whole new path, not a shortcut through it. If the baked asset drops the spawn
        /// yaws or <see cref="BakedMapSource"/> hands back the wrong list, this is where it shows.
        /// </summary>
        private NV.Shared.Collision.MapData BuildThroughNewPipeline()
        {
            return Bake(new TestRoomGenerator().Generate(NewSettings()), "Test Room");
        }

        /// <summary>
        /// The Backrooms at its shipped settings — which is to say at every default, since
        /// <c>SampleScene</c>'s serialized values match the code's.
        /// </summary>
        private NV.Shared.Collision.MapData BuildBackrooms()
        {
            var settings = ScriptableObject.CreateInstance<BackroomsSettings>();
            settings.mapName = "backrooms";
            _spawned.Add(settings);

            return Bake(new BackroomsGenerator().Generate(settings), "Backrooms");
        }

        private NV.Shared.Collision.MapData Bake(MapBlueprint blueprint, string generatorName)
        {
            Assert.IsNull(blueprint.Blocker, "이 설정으로는 구울 수 없다: " + blueprint.Blocker);

            var asset = ScriptableObject.CreateInstance<MapBakedAsset>();
            asset.Fill(blueprint, generatorName, "1970-01-01T00:00:00Z");
            _spawned.Add(asset);

            var host = new GameObject("BakedMapSourceUnderTest");
            _spawned.Add(host);

            var source = host.AddComponent<BakedMapSource>();
            source.asset = asset;

            return MapExport.BuildMapData(source);
        }

        private TestRoomSettings NewSettings()
        {
            var settings = ScriptableObject.CreateInstance<TestRoomSettings>();
            settings.mapName = "test-room";
            _spawned.Add(settings);

            return settings;
        }

        private static string ReadGolden(string mapName)
        {
            var path = Path.GetFullPath(Application.dataPath + GoldenDirectory + mapName + ".json");

            Assert.IsTrue(File.Exists(path),
                $"배포된 맵 파일이 없다: {path}. NVproject 와 NVserver 가 같은 저장소에 나란히 있는지 본다.");

            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        /// <summary>
        /// The grid's base64 cell block. Compared as text on purpose — it is base64, so equal text
        /// is equal bytes and there is nothing to round-trip.
        /// </summary>
        private static string GridCells(string text)
        {
            var open = text.IndexOf("\"cells\": \"", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(open, 0, "격자의 cells 를 찾지 못했다.");

            open += "\"cells\": \"".Length;
            var close = text.IndexOf('"', open);

            return text.Substring(open, close - open);
        }

        /// <summary>One <c>"name": value</c> as written, without the trailing comma.</summary>
        private static string Scalar(string text, string name)
        {
            var open = text.IndexOf($"\"{name}\": ", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(open, 0, $"\"{name}\" 를 찾지 못했다.");

            open += name.Length + 4;
            var close = text.IndexOfAny(new[] { ',', '\n' }, open);

            return text.Substring(open, close - open).Trim();
        }

        /// <summary>
        /// Compares one named array of the serialization row by row, **by value rather than by
        /// spelling**.
        ///
        /// Row by row so a failure names the offending entry — "box 6 differs" is a place to look,
        /// and "the two strings are not equal" over 70 KB is not.
        ///
        /// **By value because the same float has more than one round-trip spelling, and the shipped
        /// file uses the other one.** The committed <c>test-room.json</c> writes the first spawn's
        /// yaw as <c>3.1415927</c>; this editor's runtime writes <c>Mathf.PI</c> as
        /// <c>3.14159274</c> — measured, and measured for <c>TestRoomMap</c>'s own expression too,
        /// so it is the file that predates the current toolchain and not the generator that is
        /// wrong. Both parse to the identical float, which is why the map hash never noticed: the
        /// hash is computed from the values, and spawns are not in it at all.
        ///
        /// Comparing text would therefore fail on a difference that is not a difference, and
        /// tempt somebody into "fixing" a generator that is already right.
        /// </summary>
        private static void AssertSectionsMatch(string golden, string produced, string section)
        {
            var expected = Section(golden, section);
            var actual = Section(produced, section);

            Assert.AreEqual(expected.Count, actual.Count,
                $"{section} 의 개수가 다르다. 순서는 맵 해시에 그대로 들어간다.");

            for (var index = 0; index < expected.Count; index++)
            {
                var want = Numbers(expected[index]);
                var got = Numbers(actual[index]);

                Assert.AreEqual(want.Count, got.Count,
                    $"{section}[{index}] 의 필드 수가 다르다.\n  {expected[index]}\n  {actual[index]}");

                for (var field = 0; field < want.Count; field++)
                {
                    // Bit-exact. These are floats written round-trip, so "close enough" would let a
                    // real one-ulp difference through — and one ulp of a wall position is a map-hash
                    // mismatch on every connect.
                    Assert.AreEqual(want[field], got[field],
                        $"{section}[{index}] 의 {field}번째 값이 다르다." +
                        $"\n  기대 {expected[index]}\n  실제 {actual[index]}");
                }
            }
        }

        /// <summary>
        /// The numbers in one serialized row, in order. Keys carry no digits, so scanning for
        /// numeric tokens is enough and needs no schema.
        /// </summary>
        private static List<float> Numbers(string row)
        {
            var found = new List<float>();

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(row, @"-?\d+(\.\d+)?([eE][-+]?\d+)?"))
            {
                found.Add(float.Parse(match.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            return found;
        }

        /// <summary>
        /// The rows of one <c>"name": [ … ]</c> array, trimmed and without the trailing comma.
        ///
        /// A hand-rolled reader rather than a JSON parser because Unity ships none, and adding one
        /// here would be a second description of this file's schema — which is the thing the export
        /// pipeline deliberately refuses to do for exactly this reason.
        /// </summary>
        private static List<string> Section(string text, string name)
        {
            var rows = new List<string>();
            var open = text.IndexOf($"\"{name}\": [", System.StringComparison.Ordinal);

            Assert.GreaterOrEqual(open, 0, $"\"{name}\" 배열을 찾지 못했다.");

            var close = text.IndexOf(']', open);
            Assert.Greater(close, open, $"\"{name}\" 배열이 닫히지 않았다.");

            var body = text.Substring(open, close - open).Split('\n');

            for (var index = 1; index < body.Length; index++)
            {
                var row = body[index].Trim().TrimEnd(',');
                if (row.Length > 0) rows.Add(row);
            }

            return rows;
        }
    }
}
