using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NV.Infrastructure.FileSystem;
using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Shared.Collision;

namespace NV.Api.Composition
{
    /// 컴포지션 루트. 모듈이 무엇을 등록하는지는 모듈이 안다.
    /// 여기서는 AddXxx / MapXxx 를 부르고, 모듈이 만들지 않는 것만 준비한다.
    internal static class ModuleRegistration
    {
        private const string MapPathKey = "Game:MapPath";
        private const string DefaultMapPath = "../MapData/arena.json";

        public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
        {
            // 맵 로드는 파일 IO 다. 컴포지션 루트가 하고 결과만 넘긴다.
            // 실패하면 기동을 멈춘다. 빈 콜리전으로 올라가면 지형을 통과한다.
            var mapPath = configuration[MapPathKey] ?? DefaultMapPath;
            services.AddSingleton<WorldMap>(MapLoader.Load(mapPath));

            services.AddRealtime(options => configuration
                .GetSection(RealtimeOptions.SectionName)
                .Bind(options));

            return services;
        }

        public static WebApplication MapModules(this WebApplication app)
        {
            app.MapRealtime();

            return app;
        }
    }
}
