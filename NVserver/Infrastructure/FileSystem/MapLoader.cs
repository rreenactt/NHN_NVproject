using System;
using System.Collections.Generic;
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

        /// 검사 자체는 `Shared` 의 `MapDataValidator` 에 있다.
        ///
        /// **여기서 다시 쓰지 않는 이유.** 클라이언트도 export 하기 전에 같은 검사를 해야
        /// 하는데, 두 곳에 쓰면 갈린다. 실제로 갈려 있었다 — 서버가 네 가지를 보는 동안
        /// export 는 격자 하나만 봤고, 그래서 export 가 통과시킨 파일이 서버 기동을 멈출 수
        /// 있었다. 이 함수의 몫은 오류 목록을 예외로 바꾸고 어느 파일인지 붙이는 것뿐이다.
        ///
        /// 시뮬레이션 검산(`InspectSimulation`)은 부르지 않는다. 격자가 있는 맵에서 셀마다
        /// 겹침 해소를 부르므로 기동 시간에 얹을 비용이 아니고, 그것은 export 시점과
        /// `ExportedMapTests` 가 맡는다.
        private static void Validate(MapData data, string path)
        {
            var errors = new List<string>();

            if (MapDataValidator.TryValidateSchema(data, errors))
            {
                return;
            }

            throw new InvalidOperationException(
                $"맵 파일이 잘못됐다: {path}{Environment.NewLine}  · " +
                string.Join($"{Environment.NewLine}  · ", errors));
        }
    }
}
