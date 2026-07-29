using System;
using System.IO;
using System.Numerics;
using NV.Infrastructure.FileSystem;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// Unity 에서 export 한 실제 맵을 검사한다.
    ///
    /// 스폰이 지형에 파묻히거나 바닥이 없으면 증상이 "접속하면 끝없이 떨어짐" 또는
    /// "스폰 직후 벽에 끼임" 으로 나타난다. 그때는 이미 클라이언트를 의심하고 있게 되므로
    /// 맵 파일 단계에서 잡는다.
    public sealed class ExportedMapTests
    {
        /// 서버가 로드할 수 있는 맵 전부. 어느 쪽으로 돌려도 플레이 가능해야 한다.
        /// `backrooms` 는 게임, `test-room` 은 멀티플레이 확인용이다.
        public static TheoryData<string, string> Maps => new TheoryData<string, string>
        {
            { "backrooms.json", "backrooms" },
            { "test-room.json", "test-room" },
        };

        [Theory]
        [MemberData(nameof(Maps))]
        public void Export된_맵이_로드되고_해시가_0이_아니다(string file, string name)
        {
            var map = Load(file);

            Assert.Equal(name, map.Name);
            Assert.True(map.Collision.BoxCount > 0, "박스가 없다.");
            Assert.NotEqual(0u, map.Hash);
            Assert.Equal(8, map.SpawnCount);
        }

        [Theory]
        [MemberData(nameof(Maps))]
        public void 모든_스폰이_지형과_겹치지_않는다(string file, string name)
        {
            _ = name;
            var map = Load(file);

            for (var index = 0; index < map.SpawnCount; index++)
            {
                var state = PlayerState.Spawn(map.SpawnPosition(index), map.SpawnYaw(index), 100);
                var resolved = map.Collision.Depenetrate(state.BoxCenter, state.BoxHalfExtents);

                Assert.True(
                    resolved == state.BoxCenter,
                    $"스폰 {index} 가 지형에 파묻혀 {resolved - state.BoxCenter} 만큼 밀려났다.");
            }
        }

        [Theory]
        [MemberData(nameof(Maps))]
        public void 스폰에서_가만히_있으면_바닥에_선다(string file, string name)
        {
            _ = name;
            var map = Load(file);

            for (var index = 0; index < map.SpawnCount; index++)
            {
                var state = PlayerState.Spawn(map.SpawnPosition(index), map.SpawnYaw(index), 100);
                var neutral = new InputFrame(
                    ButtonFlags.None,
                    0,
                    0,
                    Quantization.ToFixedYaw(state.Yaw),
                    0);

                // 중력이 적용되고 착지 판정이 서기까지 몇 틱이 필요하다.
                for (var tick = 0; tick < 10; tick++)
                {
                    state = PlayerMovement.Step(state, neutral, map.Collision);
                }

                Assert.True(state.IsGrounded, $"스폰 {index} 에서 착지하지 못했다. y = {state.Position.Y}");
                Assert.True(
                    Math.Abs(state.Position.Y) < 0.05f,
                    $"스폰 {index} 의 발밑이 바닥에서 {state.Position.Y} 만큼 떨어져 있다.");
            }
        }

        [Theory]
        [MemberData(nameof(Maps))]
        public void 스폰_지점에서_앞으로_걸어도_지형을_통과하지_않는다(string file, string name)
        {
            _ = name;
            var map = Load(file);
            var state = PlayerState.Spawn(map.SpawnPosition(0), map.SpawnYaw(0), 100);

            // 전진 입력 60틱. 2초면 벽에 닿고 미끄러진다.
            var forward = new InputFrame(
                ButtonFlags.None,
                0,
                127,
                Quantization.ToFixedYaw(state.Yaw),
                0);

            for (var tick = 0; tick < 60; tick++)
            {
                state = PlayerMovement.Step(state, forward, map.Collision);

                var resolved = map.Collision.Depenetrate(state.BoxCenter, state.BoxHalfExtents);
                Assert.True(
                    Vector3.Distance(resolved, state.BoxCenter) < 0.05f,
                    $"틱 {tick} 에서 지형에 {Vector3.Distance(resolved, state.BoxCenter)} 만큼 파묻혔다.");
            }
        }

        private static WorldMap Load(string file)
        {
            return MapLoader.Load(FindMapPath(file));
        }

        /// 테스트는 artifacts/ 아래에서 실행되므로 저장소 루트를 거슬러 찾는다.
        private static string FindMapPath(string file)
        {
            var relative = Path.Combine("MapData", file);
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException($"MapData/{file} 를 찾지 못했다.");
        }
    }
}
