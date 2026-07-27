using Microsoft.Extensions.Logging;

namespace NV.Infrastructure.Logging
{
    /// 내장 로깅 설정. 구조화 로그 수집기가 없는 동안 프로바이더를 늘리지 않는다.
    public static class LoggingSetup
    {
        public static ILoggingBuilder AddNvLogging(this ILoggingBuilder logging)
        {
            logging.ClearProviders();

            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.IncludeScopes = false;

                // 타임스탬프 없이 남기면 틱 지연을 로그로 추적할 수 없다.
                options.TimestampFormat = "HH:mm:ss.fff ";
                options.UseUtcTimestamp = true;
            });

            // 틱마다 찍히는 요청 로그가 게임 로그를 덮는다.
            logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

            return logging;
        }
    }
}
