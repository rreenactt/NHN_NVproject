using System;
using System.Collections.Generic;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        /// 맵이 사는 디렉터리. **여기 있는 `*.json` 전부가 등록된 맵이다.**
        ///
        /// 예전에는 `Game:Maps` 에 한 줄을 적는 것이 등록이었고, 그것을 빠뜨리면 export 한
        /// 맵으로 방을 만들 수 없었다 — 증상은 `400 unknownMap` 이고, export 도구는 그것을
        /// 경고할 수만 있었다(에디터가 서버 설정을 고치는 것은 되돌릴 자리가 없다).
        /// 이제 파일을 놓는 것이 등록이다.
        private const string MapDirectoryKey = "Game:MapDirectory";

        /// 별칭. `Game:Maps:{별칭} = {맵 id}` 다. **등록이 아니라 이름표다.**
        ///
        /// 값이 `.json` 으로 끝나면 예전처럼 경로로 읽는다 — 디렉터리 밖의 맵을 하나 더
        /// 등록하는 경로이고, 옛 설정 파일이 그 형태다.
        private const string MapsKey = "Game:Maps";

        /// 미리 열어 두는 룸. `Game:StaticRooms:{룸 id}` 가 그 룸의 맵 id(문자열)거나
        /// 프로필(절 — `Map` + `Bots` 오버라이드)이다.
        private const string StaticRoomsKey = "Game:StaticRooms";

        /// 등록된 모든 맵에 `test-{맵 id}` 룸을 자동으로 열 것인가. 기본은 끔이고
        /// 개발 설정(`appsettings.Development.json`)이 켠다.
        private const string TestRoomsPerMapKey = "Game:TestRoomsPerMap";

        /// 단일 맵으로 쓸 때의 하위 호환 키. `default` 별칭의 대상으로 읽는다.
        private const string LegacyMapPathKey = "Game:MapPath";

        /// 클라이언트가 실제로 그리는 레벨이 사는 곳이다. Unity 의
        /// Tools ▸ NV ▸ Map ▸ Export Map Collision 이 이 디렉터리에 파일을 만든다.
        private const string DefaultMapDirectory = "../MapData";

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
            services.AddSingleton(LoadStaticRooms(configuration, environment));

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
        /// **맵 이름을 따로 찍지 않는다.** id 가 곧 이름이고(`MapCatalogLoader` 가 그것을
        /// 검사한다) 두 번 찍으면 다음에 한쪽만 고치게 된다. 대신 별칭을 찍는다 — 그쪽이
        /// 이제 "이 맵을 다른 이름으로도 부를 수 있는가" 에 답하는 값이다.
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
                    "맵 로드: id={MapId}{Aliases} 박스={BoxCount} 스폰={SpawnCount}(Runner {RunnerSpawnCount}, Seeker 전용 {SeekerSpawnCount}) {Grid} 해시={MapHash:X8}",
                    pair.Key,
                    DescribeAliases(maps, pair.Key),
                    map.Collision.BoxCount,
                    map.SpawnCount,
                    map.RunnerSpawnCount,
                    map.SeekerSpawnCount,
                    grid,
                    map.Hash);
            }

            return app;
        }

        /// 이 맵을 가리키는 별칭들. 없으면 빈 문자열.
        ///
        /// **찍어야 하는 값이다.** `default` 가 어느 맵을 가리키는지는 설정 세 곳(디렉터리의
        /// 파일, `Game:Maps`, 하위 호환 키)이 합쳐져 정해지고, 맵을 지정하지 않은 요청 전부가
        /// 그 답으로 열린다.
        private static string DescribeAliases(RoomMaps maps, string mapId)
        {
            var names = new List<string>();

            foreach (var pair in maps.Aliases)
            {
                if (string.Equals(pair.Value, mapId, StringComparison.Ordinal))
                {
                    names.Add(pair.Key);
                }
            }

            if (names.Count == 0)
            {
                return string.Empty;
            }

            names.Sort(StringComparer.Ordinal);

            return $"(별칭 {string.Join(", ", names)})";
        }

        public static WebApplication MapModules(this WebApplication app)
        {
            app.MapRealtime();

            return app;
        }

        /// 맵 디렉터리를 훑고 설정의 별칭을 얹는다.
        ///
        /// 하나라도 못 읽으면 기동을 멈춘다. 빈 콜리전이나 없는 맵으로 조용히 올라가면
        /// 플레이어가 지형을 통과하고, 증상이 클라이언트 버그처럼 보인다. **디렉터리를 훑게 된
        /// 뒤로는 그 판단이 한 가지를 더 뜻한다** — 반쯤 쓰인 실험용 파일을 그 폴더에 두면
        /// 서버가 뜨지 않는다. 그래도 조용히 건너뛰지 않는다. 그 폴더의 파일은 사고가 아니라
        /// 누군가 export 를 돌린 결과이고, export 는 원자적으로 쓰므로(`MapExportPipeline`)
        /// 정상 경로에서 반쯤 쓰인 파일이 생기지 않는다.
        ///
        /// `GetChildren()` 으로 읽는다. 사전 바인딩(`Get&lt;Dictionary&gt;`)을 쓰면 키 대소문자
        /// 처리가 설정 제공자에 맡겨지는데, 맵 id 는 소문자만 쓰므로 원문 그대로 받아야 한다.
        private static RoomMaps LoadMaps(IConfiguration configuration)
        {
            var directory = configuration[MapDirectoryKey];

            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = DefaultMapDirectory;
            }

            var catalog = MapCatalogLoader.Load(directory, ReadDeclarations(configuration));
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in catalog.Aliases)
            {
                aliases[pair.Key] = pair.Value;
            }

            EnsureDefaultAlias(catalog, aliases);

            return new RoomMaps(catalog.Maps, aliases);
        }

        /// `Game:Maps` 와 하위 호환 키를 한 사전으로 모은다.
        private static Dictionary<string, string> ReadDeclarations(IConfiguration configuration)
        {
            var declared = new Dictionary<string, string>(StringComparer.Ordinal);
            var legacyPath = configuration[LegacyMapPathKey];

            if (!string.IsNullOrWhiteSpace(legacyPath))
            {
                // 단일 맵 설정이다. 그 파일이 곧 기본 맵이므로 `default` 의 대상으로 읽는다.
                declared[RoomMaps.DefaultMapId] = legacyPath!;
            }

            foreach (var child in configuration.GetSection(MapsKey).GetChildren())
            {
                if (string.IsNullOrWhiteSpace(child.Value))
                {
                    throw new InvalidOperationException($"{MapsKey}:{child.Key} 에 값이 없다.");
                }

                declared[child.Key] = child.Value!;
            }

            return declared;
        }

        /// `default` 가 아무것도 가리키지 않는 설정을 여기서 잡는다.
        ///
        /// 맵이 하나뿐이면 그것을 가리킨다 — 단일 맵 배포에서 별칭 한 줄을 요구할 이유가 없다.
        /// 둘 이상이면 **고르지 않는다.** 이름 순으로 첫 번째를 잡는 것 같은 규칙을 두면 맵을
        /// 하나 추가하는 것이 기본 맵을 바꿀 수 있고, 그 변화는 어디에도 적히지 않는다.
        ///
        /// `RoomMaps` 의 생성자가 같은 것을 다시 검사한다. 여기서 먼저 하는 이유는 메시지다 —
        /// 그쪽은 등록된 맵 목록을 모르므로 무엇을 고를 수 있는지 말해 줄 수 없다.
        private static void EnsureDefaultAlias(MapCatalog catalog, Dictionary<string, string> aliases)
        {
            if (catalog.Maps.ContainsKey(RoomMaps.DefaultMapId)
                || aliases.ContainsKey(RoomMaps.DefaultMapId))
            {
                return;
            }

            var ids = catalog.SortedIds();

            if (ids.Count == 1)
            {
                aliases[RoomMaps.DefaultMapId] = ids[0];
                return;
            }

            throw new InvalidOperationException(
                $"기본 맵이 정해지지 않았다. {MapsKey}:{RoomMaps.DefaultMapId} 에 맵 id 를 적는다. " +
                $"등록된 맵: {string.Join(", ", ids)}");
        }

        /// 정적 룸 설정을 읽는다. 값이 문자열이면 맵 id 하나(옛 형태), 절이면 프로필이다.
        ///
        /// **모르는 키는 기동 거부다.** 구성 바인딩은 오타를 조용히 무시하고, 그 증상은
        /// "프로필이 안 먹는다" 로만 나타난다 — 그것을 대조할 자리가 여기뿐이다.
        ///
        /// **개발 환경 밖도 기동 거부다.** 정적 룸은 방장이 없고 만료되지 않는 공개 방이며,
        /// 준비 게이트를 건너뛰고 전원의 시작을 받아 준다(`Room.IsAuthorized`) — 전부
        /// 2클라 개발 루프를 위한 성질이고, 운영에서는 로비 목록과 빠른 입장이 실제
        /// 사용자를 그 방에 떨어뜨린다. 실제로 기본 설정의 `test` 룸이 배포까지 따라갔다.
        /// 조용히 걸러 주지 않는 이유는 봇 가드(`GuardDevelopmentOnlyOptions`)와 같다 —
        /// 설정이 거짓말을 하게 된다.
        private static StaticRooms LoadStaticRooms(IConfiguration configuration, IHostEnvironment environment)
        {
            var section = configuration.GetSection(StaticRoomsKey);
            var profiles = new Dictionary<string, TestRoomProfile>(StringComparer.Ordinal);

            foreach (var child in section.GetChildren())
            {
                profiles[child.Key] = ReadRoomProfile(child);
            }

            var perMap = configuration.GetValue(TestRoomsPerMapKey, false);

            if (!environment.IsDevelopment() && (profiles.Count > 0 || perMap))
            {
                throw new InvalidOperationException(
                    $"{StaticRoomsKey} 와 {TestRoomsPerMapKey} 는 개발 환경에서만 쓸 수 있다. " +
                    $"지금 환경은 '{environment.EnvironmentName}' 다. 정적 룸은 방장 없이 만료되지 않는 " +
                    "공개 방이라 운영 서버에 있을 이유가 없다 — 설정에서 제거한다.");
            }

            return new StaticRooms(profiles, perMap);
        }

        private static TestRoomProfile ReadRoomProfile(IConfigurationSection room)
        {
            if (room.Value != null)
            {
                if (string.IsNullOrWhiteSpace(room.Value))
                {
                    throw new InvalidOperationException($"{room.Path} 에 맵 id 가 없다.");
                }

                return new TestRoomProfile(room.Value);
            }

            string? mapId = null;
            int? fillTo = null;
            BotBehavior? behavior = null;
            BotRolePreference? role = null;
            uint? seed = null;

            foreach (var child in room.GetChildren())
            {
                if (KeyIs(child, "Map"))
                {
                    mapId = child.Value;
                }
                else if (KeyIs(child, "Bots"))
                {
                    foreach (var bot in child.GetChildren())
                    {
                        if (KeyIs(bot, "FillTo"))
                        {
                            fillTo = ParseInt(bot);
                        }
                        else if (KeyIs(bot, "Behavior"))
                        {
                            behavior = ParseEnum<BotBehavior>(bot);
                        }
                        else if (KeyIs(bot, "Role"))
                        {
                            role = ParseEnum<BotRolePreference>(bot);
                        }
                        else if (KeyIs(bot, "Enabled"))
                        {
                            // 봇 스위치는 전역 하나여야 `GuardDevelopmentOnlyOptions` 가
                            // 방어선으로 성립한다. 프로필은 오버라이드만 갖는다.
                            throw new InvalidOperationException(
                                $"{bot.Path} — 봇 스위치는 {RealtimeOptions.SectionName}:Bots:Enabled 만이 정한다. " +
                                "프로필에는 FillTo·Behavior·Role·Seed 만 쓸 수 있다.");
                        }
                        else if (KeyIs(bot, "Seed"))
                        {
                            seed = ParseUInt(bot);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"{bot.Path} 는 모르는 키다. FillTo·Behavior·Role·Seed 만 쓸 수 있다.");
                        }
                    }
                }
                else
                {
                    throw new InvalidOperationException($"{child.Path} 는 모르는 키다. Map·Bots 만 쓸 수 있다.");
                }
            }

            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new InvalidOperationException($"{room.Path}:Map 에 맵 id 가 없다.");
            }

            return new TestRoomProfile(mapId!, fillTo, behavior, role, seed);
        }

        /// 설정 키는 대소문자를 가리지 않는다 — 구성 시스템의 규칙과 같게 둔다.
        private static bool KeyIs(IConfigurationSection section, string key)
        {
            return string.Equals(section.Key, key, StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseInt(IConfigurationSection section)
        {
            return int.TryParse(section.Value, out var value)
                ? value
                : throw new InvalidOperationException($"{section.Path} 의 값 '{section.Value}' 를 정수로 읽을 수 없다.");
        }

        private static uint ParseUInt(IConfigurationSection section)
        {
            return uint.TryParse(section.Value, out var value)
                ? value
                : throw new InvalidOperationException($"{section.Path} 의 값 '{section.Value}' 를 부호 없는 정수로 읽을 수 없다.");
        }

        private static TEnum ParseEnum<TEnum>(IConfigurationSection section)
            where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(section.Value, ignoreCase: true, out var value)
                && Enum.IsDefined(typeof(TEnum), value))
            {
                return value;
            }

            throw new InvalidOperationException(
                $"{section.Path} 의 값 '{section.Value}' 를 읽을 수 없다. " +
                $"가능한 값: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}");
        }
    }
}
