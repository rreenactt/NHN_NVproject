using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        private readonly WorldMap _map;
        private readonly ILogger<GameLoopService> _logger;

        public GameLoopService(
            RoomRegistry rooms,
            NetworkConditionTransport transport,
            NetworkConditionSimulator network,
            WorldMap map,
            ILogger<GameLoopService> logger)
        {
            _rooms = rooms;
            _transport = transport;
            _network = network;
            _map = map;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "틱 루프 시작. {TickRate}Hz, 간격 {Interval}ms, 맵 {MapName} 해시 {MapHash:X8} 박스 {BoxCount}개",
                SimConstants.TickRate,
                TickInterval.TotalMilliseconds,
                _map.Name,
                _map.Hash,
                _map.Collision.BoxCount);

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

        /// 송신 지연은 룸 틱이 아니라 이 카운터를 기준으로 한다.
        /// 룸마다 틱이 다르므로 룸 틱을 쓰면 보류 목록의 해제 시점이 뒤섞인다.
        private uint _serverTick;

        private void RunTick()
        {
            _serverTick++;
            _transport.BeginTick(_serverTick);

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
