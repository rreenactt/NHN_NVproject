using System;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Realtime.Transport;
using NV.Shared.Transport;

namespace NV.Realtime
{
    /// 모듈의 유일한 공개 조립 지점. Api 는 이 둘만 호출한다.
    /// 서비스로 추출할 때 새 진입점에서 같은 호출을 그대로 쓴다.
    ///
    /// `RoomMaps` 와 `StaticRooms` 는 Api 가 등록한다. 둘 다 설정과 파일 IO 에서
    /// 나오며 그것은 컴포지션 루트의 일이다. 맵 타입이 `Shared` 에 있는 이유는
    /// 클라이언트도 같은 맵을 로드하기 때문이다.
    public static class RealtimeModule
    {
        public static IServiceCollection AddRealtime(
            this IServiceCollection services,
            Action<RealtimeOptions>? configure = null)
        {
            var options = new RealtimeOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<NetworkConditionSimulator>();
            services.AddSingleton<SessionRegistry>();
            services.AddSingleton<RoomRegistry>();

            // 맵 목록 응답은 기동 때 한 번 만든다. 맵은 로드 후 변하지 않으므로 요청마다
            // 직렬화할 이유가 없다. `RoomMaps` 는 Api 가 등록하며, 등록 순서는 상관없다 —
            // 컨테이너가 처음 이 타입을 요구할 때 해석된다.
            services.AddSingleton<MapListPayload>();
            services.AddSingleton<IRoomQuery>(provider => provider.GetRequiredService<RoomRegistry>());

            // 송신 경로는 항상 조건 주입 데코레이터를 지난다.
            // 설정에 따라 타입이 달라지면 틱 루프가 어느 구현을 받았는지 분기해야 한다.
            services.AddSingleton<WebSocketServerTransport>();
            services.AddSingleton<NetworkConditionTransport>();
            services.AddSingleton<IServerTransport>(provider => provider.GetRequiredService<NetworkConditionTransport>());

            services.AddHostedService<GameLoopService>();

            return services;
        }

        public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder endpoints)
        {
            RealtimeEndpoints.Map(endpoints);

            return endpoints;
        }
    }
}
