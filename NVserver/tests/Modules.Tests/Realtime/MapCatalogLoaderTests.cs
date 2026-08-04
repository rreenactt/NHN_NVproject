using System;
using System.Collections.Generic;
using System.IO;
using NV.Infrastructure.FileSystem;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 맵 등록이 "디렉터리에 파일을 놓는 것" 인지 본다.
    ///
    /// **이 테스트가 지키는 것은 등록 경로가 하나라는 것이다.** 예전에는 `Game:Maps` 에 한 줄을
    /// 적는 것이 등록이었고, 빠뜨리면 export 한 맵으로 방을 만들 수 없었다 — 증상은
    /// `400 unknownMap` 이며 export 도구는 그것을 경고만 할 수 있었다.
    public sealed class MapCatalogLoaderTests : IDisposable
    {
        private readonly string _directory;

        public MapCatalogLoaderTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"nv-maps-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Fact]
        public void 디렉터리의_맵을_파일명으로_등록한다()
        {
            WriteMap("alpha");
            WriteMap("beta");

            var catalog = MapCatalogLoader.Load(_directory);

            Assert.Equal(new[] { "alpha", "beta" }, catalog.SortedIds());
            Assert.Equal("alpha", catalog.Maps["alpha"].Name);
        }

        /// `.json` 이 아닌 파일은 맵이 아니다. 옆에 `.meta` 나 편집기 백업이 있을 수 있다.
        [Fact]
        public void Json이_아닌_파일은_무시한다()
        {
            WriteMap("alpha");
            File.WriteAllText(Path.Combine(_directory, "alpha.json.meta"), "not a map");
            File.WriteAllText(Path.Combine(_directory, "notes.txt"), "not a map");

            var catalog = MapCatalogLoader.Load(_directory);

            Assert.Equal(new[] { "alpha" }, catalog.SortedIds());
        }

        /// 파일명과 `name` 이 갈리면 맵 id 가 무엇인지 답할 수 없다.
        ///
        /// 그대로 두면 파일명으로 등록된 id 로 방을 만들었을 때 클라이언트는 `name` 을 받아
        /// 다른 씬을 열려 하고, 증상은 맵 해시 불일치 하나다.
        [Fact]
        public void 파일명과_이름이_다르면_기동을_멈춘다()
        {
            WriteMap("alpha", name: "beta");

            var error = Assert.Throws<InvalidOperationException>(() => MapCatalogLoader.Load(_directory));

            Assert.Contains("beta", error.Message);
        }

        /// 깨진 파일을 조용히 건너뛰지 않는다. `MapLoader` 의 규칙을 그대로 물려받는다.
        [Fact]
        public void 못_읽는_파일은_기동을_멈춘다()
        {
            WriteMap("alpha");
            File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ this is not json");

            Assert.ThrowsAny<Exception>(() => MapCatalogLoader.Load(_directory));
        }

        [Fact]
        public void 없는_디렉터리는_기동을_멈춘다()
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => MapCatalogLoader.Load(Path.Combine(_directory, "nope")));
        }

        // ==================================================== 별칭

        [Fact]
        public void 값이_맵_id_면_별칭이다()
        {
            WriteMap("alpha");

            var catalog = MapCatalogLoader.Load(_directory, Declared("default", "alpha"));

            Assert.Equal(new[] { "alpha" }, catalog.SortedIds());
            Assert.Equal("alpha", catalog.Aliases["default"]);
        }

        /// 옛 설정 파일의 형태다 — `"default": "../MapData/backrooms.json"`.
        ///
        /// **다시 읽지 않고 별칭으로 바꾼다.** 같은 맵을 두 id 로 등록하면 id 와 이름이 다시
        /// 갈리고, 그것이 이 작업이 없애려는 상태다.
        [Fact]
        public void 값이_디렉터리_안의_경로면_별칭이_된다()
        {
            var path = WriteMap("alpha");

            var catalog = MapCatalogLoader.Load(_directory, Declared("default", path));

            Assert.Equal(new[] { "alpha" }, catalog.SortedIds());
            Assert.Equal("alpha", catalog.Aliases["default"]);
        }

        /// 디렉터리 밖의 맵도 경로로 하나 더 등록할 수 있다. 그때 id 는 여전히 파일명이다.
        [Fact]
        public void 디렉터리_밖의_경로는_등록이다()
        {
            WriteMap("alpha");

            var outside = Path.Combine(Path.GetTempPath(), $"nv-outside-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outside);

            try
            {
                var path = WriteMap("gamma", directory: outside);
                var catalog = MapCatalogLoader.Load(_directory, Declared("gamma", path));

                Assert.Equal(new[] { "alpha", "gamma" }, catalog.SortedIds());
                Assert.Empty(catalog.Aliases);
            }
            finally
            {
                Directory.Delete(outside, true);
            }
        }

        /// 매달린 별칭은 조용히 `unknownMap` 이 된다 — 설정은 그 맵이 있다고 말하고 서버는
        /// 없다고 답하며, 그 둘을 대조할 자리가 없다.
        [Fact]
        public void 없는_맵을_가리키는_별칭은_기동을_멈춘다()
        {
            WriteMap("alpha");

            var error = Assert.Throws<InvalidOperationException>(
                () => MapCatalogLoader.Load(_directory, Declared("default", "nope")));

            Assert.Contains("alpha", error.Message);
        }

        [Fact]
        public void 맵을_가리는_별칭은_기동을_멈춘다()
        {
            WriteMap("alpha");
            WriteMap("beta");

            Assert.Throws<InvalidOperationException>(
                () => MapCatalogLoader.Load(_directory, Declared("beta", "alpha")));
        }

        // ==================================================== 도구

        private static Dictionary<string, string> Declared(string key, string value)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value };
        }

        /// 최소한의 유효한 맵 하나. 격자는 없다 — 그것도 정상 응답이다.
        private string WriteMap(string file, string? name = null, string? directory = null)
        {
            var path = Path.Combine(directory ?? _directory, file + ".json");

            File.WriteAllText(
                path,
                "{ \"version\": 1, \"name\": \"" + (name ?? file) + "\", " +
                "\"boxes\": [ { \"minX\": -8, \"minY\": -1, \"minZ\": -8, " +
                "\"maxX\": 8, \"maxY\": 0, \"maxZ\": 8 } ], " +
                "\"spawns\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"yaw\": 0 } ] }");

            return path;
        }
    }
}
