using System;
using System.Collections.Generic;
using System.IO;
using NV.Shared.Collision;

namespace NV.Infrastructure.FileSystem
{
    /// 디렉터리를 훑어 등록된 맵 전부를 만든다.
    ///
    /// **맵 등록이 설정 파일 편집이던 것을 대신한다.** 예전에는 `Game:Maps` 에 한 줄이 없으면
    /// `MapData/` 에 파일이 있어도 서버가 그 맵을 몰랐고, export 도구는 그것을 경고만 할 수
    /// 있었다(에디터가 서버 설정을 고치는 것은 되돌릴 자리가 없다). 그 조합의 결과가
    /// `backrooms2f` 였다 — export 는 성공하고 그 맵으로는 방을 만들 수 없다.
    ///
    /// **맵 id 는 파일명이고 그것이 맵 이름과 같아야 한다.** 다르면 기동을 멈춘다. 예전에는
    /// id(설정의 키)와 이름(파일 안의 `name`)이 서로 다른 공간이었고 — `default` 가
    /// `backrooms` 를 가리켰다 — 그래서 화면과 라우팅이 서로 다른 이름으로 말했다. 스캔으로
    /// 등록하면 id 를 정하는 자리가 파일명 하나뿐이므로, 그 파일명이 맞는지만 보면 된다.
    ///
    /// `Infrastructure` 에 있는 이유는 파일 IO 다. `MapLoader` 가 옆에 있고, 여기서 나온
    /// 결과를 `RoomMaps` 로 바꾸는 것은 컴포지션 루트의 일이다 — 이 어셈블리는 모듈을
    /// 참조하지 않는다.
    public static class MapCatalogLoader
    {
        /// 훑을 확장자. `.json` 만 본다 — 옆에 `.meta`, `.tmp`, 편집기 백업이 있어도 맵이 아니다.
        private const string MapFilePattern = "*.json";

        /// 디렉터리를 훑고, 선언된 항목(경로 또는 별칭)을 얹는다.
        ///
        /// <param name="directory">맵 디렉터리. 없으면 예외다.</param>
        /// <param name="declared">
        /// `Game:Maps` 의 내용. 값이 `.json` 으로 끝나면 **경로**(디렉터리 밖의 맵을 하나 더
        /// 등록한다), 아니면 이미 등록된 맵 id 를 가리키는 **별칭**이다.
        /// </param>
        public static MapCatalog Load(
            string directory,
            IReadOnlyDictionary<string, string>? declared = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("맵 디렉터리가 비어 있다.", nameof(directory));
            }

            var fullDirectory = Path.GetFullPath(directory);

            if (!Directory.Exists(fullDirectory))
            {
                // 조용히 빈 목록으로 올라가면 방을 만들 수 있는 맵이 하나도 없고, 증상은
                // "방 만들기가 안 된다" 하나로만 나타난다. 경로가 틀린 것을 여기서 말한다.
                throw new DirectoryNotFoundException(
                    $"맵 디렉터리를 찾지 못했다: {fullDirectory}. " +
                    "Game:MapDirectory 가 서버 실행 경로에서 맞는지 확인한다.");
            }

            var catalog = new MapCatalog(fullDirectory);

            foreach (var path in SortedFiles(fullDirectory))
            {
                catalog.AddFile(path);
            }

            if (declared == null)
            {
                return catalog;
            }

            foreach (var pair in declared)
            {
                ApplyDeclaration(catalog, pair.Key, pair.Value);
            }

            return catalog;
        }

        /// 파일 순서를 고정한다.
        ///
        /// `Directory.GetFiles` 의 순서는 파일 시스템에 달려 있다. 맵 목록 응답이 그 순서를
        /// 물려받으면 같은 서버가 기계마다 다른 순서로 답하고, 그 응답에 붙는 ETag 도
        /// 흔들린다. 정렬은 여기서 한 번 한다.
        private static string[] SortedFiles(string directory)
        {
            var files = Directory.GetFiles(directory, MapFilePattern);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        /// 설정에 적힌 한 줄을 얹는다. 경로면 등록, 아니면 별칭이다.
        private static void ApplyDeclaration(MapCatalog catalog, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Game:Maps:{key} 에 값이 없다.");
            }

            var trimmed = value.Trim();

            if (!trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                catalog.AddAlias(key, trimmed);
                return;
            }

            // 경로다. 디렉터리 안의 파일을 다시 가리키는 경우가 흔하다 — 예전 설정의
            // `"default": "../MapData/backrooms.json"` 이 그것이다. 그때는 다시 읽지 않고
            // **별칭으로 바꾼다.** 같은 맵을 두 id 로 등록하면 식별자 공간이 다시 둘이 된다.
            var registered = catalog.AddFile(Path.GetFullPath(trimmed));

            if (!string.Equals(key, registered, StringComparison.Ordinal))
            {
                catalog.AddAlias(key, registered);
            }
        }
    }

    /// 로드된 맵과 별칭. `RoomMaps` 로 바뀌기 전의 중간 형태다.
    ///
    /// `Infrastructure` 는 모듈을 참조하지 않으므로 `RoomMaps` 를 직접 만들 수 없다. 그
    /// 제약이 나쁜 것은 아니다 — 파일을 읽는 일과 그것을 모듈의 계약으로 바꾸는 일이 갈려
    /// 있으면, 스캔 규칙을 파일 시스템만으로 테스트할 수 있다.
    public sealed class MapCatalog
    {
        private readonly Dictionary<string, WorldMap> _maps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _idByPath = new(StringComparer.OrdinalIgnoreCase);

        public MapCatalog(string directory)
        {
            Directory = directory;
        }

        public string Directory { get; }

        public IReadOnlyDictionary<string, WorldMap> Maps => _maps;

        /// 별칭 → 맵 id.
        public IReadOnlyDictionary<string, string> Aliases => _aliases;

        /// 맵 파일 하나를 등록하고 그 맵 id 를 돌려준다.
        ///
        /// 같은 파일을 두 번 등록하지 않는다 — 스캔이 이미 읽은 파일을 설정이 경로로 다시
        /// 가리킬 수 있고, 그때 다시 읽으면 같은 지형이 두 벌의 메모리를 쓴다.
        public string AddFile(string fullPath)
        {
            if (_idByPath.TryGetValue(fullPath, out var known))
            {
                return known;
            }

            var map = MapLoader.Load(fullPath);
            var stem = Path.GetFileNameWithoutExtension(fullPath);

            if (!string.Equals(stem, map.Name, StringComparison.Ordinal))
            {
                // 손으로 복사한 파일이 이렇게 된다. 그대로 두면 파일명으로 등록된 id 로
                // 방을 만들었을 때 클라이언트는 `name` 을 받아 다른 씬을 열려 하고, 증상은
                // 맵 해시 불일치 하나다.
                throw new InvalidOperationException(
                    $"맵 파일명과 이름이 다르다: {fullPath} 의 name 이 '{map.Name}' 이다. " +
                    $"파일명을 '{map.Name}.json' 으로 맞추거나 맵을 다시 export 한다.");
            }

            if (_maps.ContainsKey(stem))
            {
                throw new InvalidOperationException(
                    $"맵 id '{stem}' 가 두 파일에서 나온다. 뒤에 온 것은 {fullPath} 다.");
            }

            _maps[stem] = map;
            _idByPath[fullPath] = stem;

            return stem;
        }

        /// 별칭을 붙인다. 가리키는 맵이 없으면 기동을 멈춘다.
        public void AddAlias(string alias, string mapId)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new InvalidOperationException("빈 별칭은 붙일 수 없다.");
            }

            if (!_maps.ContainsKey(mapId))
            {
                // 매달린 별칭은 조용히 `unknownMap` 이 된다. 그 실패는 방을 만드는 사람에게
                // "이 맵은 없다" 로 보이고, 설정이 그것을 있다고 말하고 있으므로 원인을
                // 찾을 자리가 없다.
                throw new InvalidOperationException(
                    $"별칭 '{alias}' 가 등록되지 않은 맵 '{mapId}' 를 가리킨다. " +
                    $"등록된 맵: {string.Join(", ", SortedIds())}");
            }

            if (_maps.ContainsKey(alias))
            {
                throw new InvalidOperationException(
                    $"별칭 '{alias}' 가 같은 이름의 맵을 가린다. 둘 중 하나의 이름을 바꾼다.");
            }

            _aliases[alias] = mapId;
        }

        public IReadOnlyList<string> SortedIds()
        {
            var ids = new List<string>(_maps.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }
    }
}
