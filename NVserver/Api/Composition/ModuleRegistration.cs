using System;
using System.Collections.Generic;
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
        /// 등록된 맵. `Game:Maps:{맵 id}` 가 그 맵의 파일이다.
        ///
        /// 키가 룸 id 가 아니라 맵 id 다. 룸은 초대 코드로 만들어지므로 설정 파일이
        /// 룸 id 를 미리 알 수 없고, 룸 id 로 맵을 찾는 구조에서는 모든 초대 코드
        /// 방이 조용히 기본 맵으로 열린다. 룸을 만들 때 맵 id 를 받는다.
        private const string MapsKey = "Game:Maps";

        /// 미리 열어 두는 룸. `Game:StaticRooms:{룸 id}` 가 그 룸의 맵 id 다.
        private const string StaticRoomsKey = "Game:StaticRooms";

        /// 단일 맵으로 쓸 때의 하위 호환 키.
        private const string LegacyMapPathKey = "Game:MapPath";

        /// 클라이언트가 실제로 그리는 레벨이다. Unity 의
        /// Tools ▸ NV Network ▸ Export Map Collision 이 이 파일을 만든다.
        private const string DefaultMapPath = "../MapData/backrooms.json";

        public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
        {
            // 맵 로드는 파일 IO 다. 컴포지션 루트가 하고 결과만 넘긴다.
            // 실패하면 기동을 멈춘다. 빈 콜리전으로 올라가면 지형을 통과한다.
            services.AddSingleton(LoadMaps(configuration));
            services.AddSingleton(LoadStaticRooms(configuration));

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

        /// 설정에 적힌 맵을 전부 읽는다.
        ///
        /// 하나라도 못 읽으면 기동을 멈춘다. 빈 콜리전이나 없는 맵으로 조용히 올라가면
        /// 플레이어가 지형을 통과하고, 증상이 클라이언트 버그처럼 보인다.
        ///
        /// `GetChildren()` 으로 읽는다. 사전 바인딩(`Get&lt;Dictionary&gt;`)을 쓰면 키 대소문자
        /// 처리가 설정 제공자에 맡겨지는데, 맵 id 는 소문자만 쓰므로 원문 그대로 받아야 한다.
        private static RoomMaps LoadMaps(IConfiguration configuration)
        {
            var section = configuration.GetSection(MapsKey);
            var byMapId = new Dictionary<string, WorldMap>(StringComparer.Ordinal);

            foreach (var child in section.GetChildren())
            {
                if (string.IsNullOrWhiteSpace(child.Value))
                {
                    throw new InvalidOperationException($"{MapsKey}:{child.Key} 에 맵 경로가 없다.");
                }

                byMapId[child.Key] = MapLoader.Load(child.Value);
            }

            if (!byMapId.ContainsKey(RoomMaps.DefaultMapId))
            {
                // Maps 를 쓰지 않는 설정과 예전 설정 파일을 위한 경로다.
                var legacyPath = configuration[LegacyMapPathKey] ?? DefaultMapPath;
                byMapId[RoomMaps.DefaultMapId] = MapLoader.Load(legacyPath);
            }

            return new RoomMaps(byMapId);
        }

        private static StaticRooms LoadStaticRooms(IConfiguration configuration)
        {
            var section = configuration.GetSection(StaticRoomsKey);
            var mapByRoom = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var child in section.GetChildren())
            {
                if (string.IsNullOrWhiteSpace(child.Value))
                {
                    throw new InvalidOperationException($"{StaticRoomsKey}:{child.Key} 에 맵 id 가 없다.");
                }

                mapByRoom[child.Key] = child.Value;
            }

            return new StaticRooms(mapByRoom);
        }
    }
}
