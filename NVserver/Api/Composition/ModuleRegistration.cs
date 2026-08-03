using System;
using System.Collections.Generic;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

        /// 브라우저에서 방 만들기·조회를 호출할 수 있게 하는 정책 이름.
        public const string CorsPolicy = "nv-web";

        /// 허용 오리진 목록. 비어 있으면 전부 허용한다(개발).
        private const string AllowedOriginsKey = "Cors:AllowedOrigins";

        private const string CreatePerMinuteKey = "RateLimit:CreatePerMinute";
        private const string CodeAttemptsPerMinuteKey = "RateLimit:CodeAttemptsPerMinute";

        /// 한 IP 가 분당 만들 수 있는 방. 빈 방이 60초 뒤 회수되므로 이 값이 곧
        /// 한 클라이언트가 동시에 잡아 둘 수 있는 방 수에 가깝다.
        private const int DefaultCreatePerMinute = 10;

        /// 한 IP 가 분당 시도할 수 있는 코드(조회 + 접속). 사람이 코드를 받아 적고
        /// 들어오는 데는 몇 번이면 충분하고, 찍어 보기에는 턱없이 부족한 값이어야 한다.
        ///
        /// 한 IP 뒤에 여러 클라이언트가 있는 경우(같은 기계의 에디터 + 빌드, 한 NAT
        /// 안의 8명)를 감안해야 한다. 초당 한 번이면 그 경우도 넉넉하다.
        private const int DefaultCodeAttemptsPerMinute = 60;

        public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
        {
            // 맵 로드는 파일 IO 다. 컴포지션 루트가 하고 결과만 넘긴다.
            // 실패하면 기동을 멈춘다. 빈 콜리전으로 올라가면 지형을 통과한다.
            services.AddSingleton(LoadMaps(configuration));
            services.AddSingleton(LoadStaticRooms(configuration));

            services.AddRealtime(options => configuration
                .GetSection(RealtimeOptions.SectionName)
                .Bind(options));

            services.AddNvRateLimiter(configuration);

            services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy =>
            {
                var origins = ReadAllowedOrigins(configuration);

                if (origins.Length == 0)
                {
                    policy.AllowAnyOrigin();
                }
                else
                {
                    policy.WithOrigins(origins);
                }

                policy.AllowAnyHeader().WithMethods("GET", "POST");
            }));

            return services;
        }

        /// 룸 엔드포인트의 요청 제한.
        ///
        /// 동시 룸 수 상한을 없앤 자리를 이것이 대신한다. 룸은 `POST /rooms` 로만
        /// 생기고 비면 60초 뒤 회수되므로, 분당 허용량이 곧 한 클라이언트가 잡아 둘 수
        /// 있는 룸 수의 상한이 된다.
        ///
        /// 나누는 기준은 원격 IP 다. **리버스 프록시 뒤에서는 프록시의 IP 하나로 묶인다** —
        /// 전체가 한 양동이를 쓰게 되므로, 그런 배포에서는 전달 헤더를 신뢰 목록과 함께
        /// 설정해야 한다. 신뢰 목록 없이 헤더를 믿으면 헤더를 위조해 제한을 우회한다.
        private static IServiceCollection AddNvRateLimiter(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var createPerMinute = configuration.GetValue(CreatePerMinuteKey, DefaultCreatePerMinute);
            var codeAttemptsPerMinute = configuration.GetValue(
                CodeAttemptsPerMinuteKey,
                DefaultCodeAttemptsPerMinute);

            services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                limiter.AddPolicy(
                    RateLimitPolicies.RoomCreate,
                    context => FixedWindowFor(context, createPerMinute));

                limiter.AddPolicy(
                    RateLimitPolicies.CodeAttempt,
                    context => FixedWindowFor(context, codeAttemptsPerMinute));
            });

            return services;
        }

        private static RateLimitPartition<string> FixedWindowFor(HttpContext context, int permitsPerMinute)
        {
            var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),

                // 대기열을 두지 않는다. 넘친 요청을 붙잡고 있으면 클라이언트는 느린
                // 서버와 거절을 구분할 수 없고, 화면에는 "접속 중" 만 남는다.
                QueueLimit = 0,
            });
        }

        /// WebGL 빌드는 브라우저 XHR 로 방 만들기·조회를 호출하므로 CORS 가 필요하다.
        ///
        /// WebSocket 은 CORS 적용 대상이 아니라서 지금까지 이 문제가 드러나지 않았다.
        /// HTTP 조회가 들어오는 순간 처음 나타나고, 증상은 콘솔의 CORS 오류 한 줄과
        /// 빈 응답이라 서버 쪽을 의심하기 어렵다.
        public static string[] ReadAllowedOrigins(IConfiguration configuration)
        {
            return configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? Array.Empty<string>();
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
