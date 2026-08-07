using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NV.Realtime.Contracts;
using NV.Realtime.Transport;
using NV.Shared.Collision;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation
{
    /// 30Hz 고정 틱. 이 루프 안에서 소켓이나 DB 를 직접 만지지 않는다.
    /// 송신은 채널에 넣고 실제 전송은 세션 펌프가 한다.
    internal sealed class GameLoopService : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(SimConstants.TickIntervalSeconds);

        private readonly RoomRegistry _rooms;
        private readonly NetworkConditionTransport _transport;
        private readonly NetworkConditionSimulator _network;
        private readonly RoomMaps _maps;
        private readonly ILogger<GameLoopService> _logger;

        public GameLoopService(
            RoomRegistry rooms,
            NetworkConditionTransport transport,
            NetworkConditionSimulator network,
            RoomMaps maps,
            ILogger<GameLoopService> logger)
        {
            _rooms = rooms;
            _transport = transport;
            _network = network;
            _maps = maps;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "틱 루프 시작. {TickRate}Hz, 간격 {Interval}ms",
                SimConstants.TickRate,
                TickInterval.TotalMilliseconds);

            // 로드된 맵을 전부 남긴다. 클라이언트가 보고할 해시와 대조할 대상이며,
            // 룸별로 다른 맵을 쓰므로 하나만 찍으면 어느 쪽과 비교해야 할지 알 수 없다.
            foreach (var pair in _maps.ByMap)
            {
                LogMap(pair.Key, pair.Value);
            }

            if (_network.Enabled)
            {
                _logger.LogWarning(
                    "네트워크 조건 주입기가 켜져 있다. 지연 {Delay}틱, 지터 ±{Jitter}틱. 개발 전용 설정이다.",
                    _network.BaseDelayTicks,
                    _network.JitterTicks);
            }

            // Task.Delay 루프는 드리프트가 누적된다.
            using var timer = new PeriodicTimer(TickInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        RunTick();
                    }
                    catch (Exception exception)
                    {
                        // BackgroundServiceExceptionBehavior 기본값이 StopHost 다.
                        // 여기서 막지 않으면 틱 하나의 예외가 서버 전체를 내린다.
                        _logger.LogError(exception, "틱 처리 중 예외");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }

            _logger.LogInformation("틱 루프 종료.");
        }

        private void LogMap(string mapId, WorldMap map)
        {
            // Runner 조회는 목록을 감으므로 정원보다 스폰이 적으면 두 사람이 같은 자리에
            // 겹친다. 스키마 검증은 정원을 모르고(Shared 는 모듈 상수를 못 본다) 정원은
            // 이 모듈의 것이므로 비교도 여기서 한다. team 필드가 스폰을 Seeker 전용으로
            // 빼 가면 "스폰 8개 ≥ 정원 5" 가 조용히 무너질 수 있는 값이 된다.
            if (map.RunnerSpawnCount < RealtimeConstants.Rooms.MaxPlayers)
            {
                _logger.LogWarning(
                    "맵 {MapId}: Runner 가 설 수 있는 스폰이 {RunnerSpawnCount}개로 정원 {MaxPlayers}명보다 적다. " +
                    "가득 찬 방에서 두 참가자가 같은 자리에 스폰된다.",
                    mapId,
                    map.RunnerSpawnCount,
                    RealtimeConstants.Rooms.MaxPlayers);
            }

            _logger.LogInformation(
                "맵 {MapId}: {MapName} 해시 {MapHash:X8} 박스 {BoxCount}개 스폰 {SpawnCount}개",
                mapId,
                map.Name,
                map.Hash,
                map.Collision.BoxCount,
                map.SpawnCount);
        }

        /// 송신 지연은 룸 틱이 아니라 이 카운터를 기준으로 한다.
        /// 룸마다 틱이 다르므로 룸 틱을 쓰면 보류 목록의 해제 시점이 뒤섞인다.
        private uint _serverTick;

        private void RunTick()
        {
            _serverTick++;
            _transport.BeginTick(_serverTick);

            // 회수를 먼저 한다. 이 틱에 사라질 룸을 진행시킬 이유가 없고,
            // 순회 중에 목록이 바뀌는 것도 피한다.
            _rooms.Sweep(_serverTick);

            foreach (var room in _rooms.All)
            {
                room.Advance();
                room.Broadcast(_transport);
            }

            // 지연으로 보류된 송신을 내보낸다. 주입기가 꺼져 있으면 즉시 반환한다.
            _transport.Flush();
        }
    }
}
