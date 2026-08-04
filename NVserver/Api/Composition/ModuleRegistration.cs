using System;
using System.Collections.Generic;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        /// Tools ▸ NV ▸ Map ▸ Export Map Collision 이 이 파일을 만든다.
        private const string DefaultMapPath = "../MapData/backrooms.json";

        /// 브라우저에서 방 만들기·조회를 호출할 수 있게 하는 정책 이름.
        public const string CorsPolicy = "nv-web";

        /// 허용 오리진 목록. 비어 있으면 전부 허용한다(개발).
        private const string AllowedOriginsKey = "Cors:AllowedOrigins";

        private const string CreatePerMinuteKey = "RateLimit:CreatePerMinute";
        private const string CodeAttemptsPerMinuteKey = "RateLimit:CodeAttemptsPerMinute";
        private const string ListPerMinuteKey = "RateLimit:ListPerMinute";

        /// 한 IP 가 분당 만들 수 있는 방. 빈 방이 60초 뒤 회수되므로 이 값이 곧
        /// 한 클라이언트가 동시에 잡아 둘 수 있는 방 수에 가깝다.
        private const int DefaultCreatePerMinute = 10;

        /// 한 IP 가 분당 시도할 수 있는 코드(조회 + 접속). 사람이 코드를 받아 적고
        /// 들어오는 데는 몇 번이면 충분하고, 찍어 보기에는 턱없이 부족한 값이어야 한다.
        ///
        /// 한 IP 뒤에 여러 클라이언트가 있는 경우(같은 기계의 에디터 + 빌드, 한 NAT
        /// 안의 8명)를 감안해야 한다. 초당 한 번이면 그 경우도 넉넉하다.
        private const int DefaultCodeAttemptsPerMinute = 60;

        /// 한 IP 가 분당 조회할 수 있는 공개 방 목록.
        ///
        /// 로비는 자동 폴링을 하지 않는다 — 화면에 들어올 때 한 번, 그 뒤로는 사람이
        /// 새로고침을 누를 때만이고 클라이언트가 3초 쿨다운을 건다. 그러면 분당 20회가
        /// 상한이므로 30 이면 정상 사용에는 걸리지 않고, 스크립트로 긁는 것은 막힌다.
        private const int DefaultListPerMinute = 30;

        public static IServiceCollection AddModules(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            // 맵 로드는 파일 IO 다. 컴포지션 루트가 하고 결과만 넘긴다.
            // 실패하면 기동을 멈춘다. 빈 콜리전으로 올라가면 지형을 통과한다.
            services.AddSingleton(LoadMaps(configuration));
            services.AddSingleton(LoadStaticRooms(configuration));

            services.AddRealtime(options =>
            {
                configuration.GetSection(RealtimeOptions.SectionName).Bind(options);

                GuardDevelopmentOnlyOptions(options, environment);
            });

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

        /// 개발 전용 설정이 개발 환경 밖에서 켜져 있으면 **기동을 멈춘다.**
        ///
        /// 봇 참가자가 그것이다. 소켓 없는 참가자를 명단에 넣고 술래 선정에 개입하는
        /// 기능이며, 실제 서비스에서는 존재할 이유가 없다.
        ///
        /// 조용히 꺼 주지 않는 이유는 설정이 거짓말을 하게 되기 때문이다. 켜라고 적혀
        /// 있는데 꺼져 있으면 "봇이 왜 안 나오는지" 를 찾게 되고, 그 답이 코드 안에
        /// 숨어 있다. 반대로 조용히 켜 두는 것은 더 나쁘다 — 실제 사용자가 `BOT 2` 와
        /// 같은 방에 들어간다.
        ///
        /// `CreateStaticRooms` 가 잘못된 맵 id 에 예외를 던지는 것과 같은 규칙이고,
        /// 클라이언트가 원격 호스트 + `secure` 꺼짐 빌드를 거부하는 것과 같은 판단이다 —
        /// 배포 시점에 시끄럽게 실패하는 편이 운영 중에 조용히 이상해지는 것보다 낫다.
        ///
        /// 방어선은 이것 하나가 아니다. `appsettings.json` 의 기본값이 꺼짐이고, 봇은
        /// 정적 룸에서만 생기므로 `Game:StaticRooms` 가 비어 있는 배포에서는 켜져 있어도
        /// 생길 룸이 없다. 이 검사는 그 둘을 동시에 어긴 설정을 잡는다.
        ///
        /// 환경 변수로도 끌 수 있다 — `Realtime__Bots__Enabled=false` 다(`__` 가 절
        /// 구분자다). 컨테이너 배포에서는 파일을 고치지 않고 이쪽을 쓴다.
        private static void GuardDevelopmentOnlyOptions(RealtimeOptions options, IHostEnvironment environment)
        {
            if (environment.IsDevelopment() || !options.Bots.Enabled)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Realtime:Bots:Enabled 는 개발 환경에서만 켤 수 있다. 지금 환경은 '{environment.EnvironmentName}' 다. " +
                "환경 변수 Realtime__Bots__Enabled=false 로 끄거나 설정에서 제거한다.");
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
            var listPerMinute = configuration.GetValue(ListPerMinuteKey, DefaultListPerMinute);

            services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                limiter.AddPolicy(
                    RateLimitPolicies.RoomCreate,
                    context => FixedWindowFor(context, createPerMinute));

                limiter.AddPolicy(
                    RateLimitPolicies.CodeAttempt,
                    context => FixedWindowFor(context, codeAttemptsPerMinute));

                limiter.AddPolicy(
                    RateLimitPolicies.RoomList,
                    context => FixedWindowFor(context, listPerMinute));
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

        /// 로드한 맵을 전부 남긴다.
        ///
        /// **룸마다 맵이 다를 수 있으므로 하나만 찍어서는 아무 말도 못 한다.** 클라이언트가
        /// 맵 해시 불일치를 보고했을 때 그 해시를 어느 맵과 비교해야 할지 알아야 하고, 그
        /// 답이 로그에 없으면 서버를 다시 띄워 확인하는 것 말고 방법이 없다.
        ///
        /// 격자 유무를 함께 찍는 이유는 그것이 조용히 사라지는 값이기 때문이다. 격자 없는
        /// 맵도 정상 로드되고 이동 판정도 정상이라, 없는 것은 "매치에 열쇠도 문도 생기지
        /// 않는다" 로만 드러난다.
        ///
        /// `AddModules` 가 아니라 여기서 하는 이유는 로거다 — 맵 로드는 컨테이너를 만드는
        /// 중에 일어나고 그 시점에는 아직 로거가 없다.
        public static WebApplication LogLoadedMaps(this WebApplication app)
        {
            var maps = app.Services.GetRequiredService<RoomMaps>();

            foreach (var pair in maps.ByMap)
            {
                var map = pair.Value;
                var grid = map.HasGrid
                    ? $"격자 {map.Data.Grid.Floors}층 {map.Data.Grid.Width}×{map.Data.Grid.Depth}, " +
                      $"몸이 들어가는 셀 {map.Grid.FreeFloorCount}개"
                    : "격자 없음";

                app.Logger.LogInformation(
                    "맵 로드: id={MapId} 이름={MapName} 박스={BoxCount} 스폰={SpawnCount} {Grid} 해시={MapHash:X8}",
                    pair.Key,
                    map.Name,
                    map.Collision.BoxCount,
                    map.SpawnCount,
                    grid,
                    map.Hash);
            }

            return app;
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
