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
    /// WorldMap 은 Api 가 등록한다. 파일 IO 는 컴포지션 루트의 일이고
    /// 클라이언트도 같은 맵을 로드하므로 타입은 Shared 에 있다.
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
