using System;
using System.IO;
using System.Text.Json;
using NV.Infrastructure.Json;
using NV.Shared.Collision;

namespace NV.Infrastructure.FileSystem
{
    /// 맵 콜리전 JSON 로더.
    ///
    /// 맵을 못 읽으면 기동을 실패시킨다. 빈 콜리전으로 조용히 올라가면
    /// 플레이어가 지형을 통과하고, 증상이 로직 버그처럼 보여 추적이 오래 걸린다.
    public static class MapLoader
    {
        public static WorldMap Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("맵 경로가 비어 있다.", nameof(path));
            }

            var fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"맵 파일을 찾지 못했다: {fullPath}", fullPath);
            }

            var json = File.ReadAllText(fullPath);

            MapData? data;
            try
            {
                data = JsonSerializer.Deserialize<MapData>(json, JsonDefaults.Options);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"맵 파일을 해석할 수 없다: {fullPath}", exception);
            }

            if (data == null)
            {
                throw new InvalidOperationException($"맵 파일이 비어 있다: {fullPath}");
            }

            Validate(data, fullPath);

            return new WorldMap(data);
        }

        private static void Validate(MapData data, string path)
        {
            if (data.Boxes == null || data.Boxes.Length == 0)
            {
                throw new InvalidOperationException($"맵에 콜리전 박스가 없다: {path}");
            }

            for (var index = 0; index < data.Boxes.Length; index++)
            {
                var box = data.Boxes[index];

                // min > max 인 박스는 스윕에서 조용히 무시되어 벽이 사라진 것처럼 보인다.
                if (box.MinX > box.MaxX || box.MinY > box.MaxY || box.MinZ > box.MaxZ)
                {
                    throw new InvalidOperationException(
                        $"박스 {index} 의 min 이 max 보다 크다: {path}");
                }
            }

            if (data.Spawns == null || data.Spawns.Length == 0)
            {
                throw new InvalidOperationException($"맵에 스폰 지점이 없다: {path}");
            }
        }
    }
}
