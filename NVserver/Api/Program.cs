using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NV.Api.Composition;
using NV.Api.Middlewares;
using NV.Infrastructure.Logging;

// 컴포지션 루트. 컨트롤러를 두지 않는다.
// 엔드포인트는 각 모듈이 MapXxx() 로 등록하고 여기서는 호출만 한다.
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddNvLogging();
builder.Services.AddModules(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandling();

// 오리진을 지정하지 않은 배포는 전부 허용으로 올라간다. 방 만들기가 아무 페이지에서나
// 호출될 수 있다는 뜻이므로, 조용히 넘기지 않고 기동 로그에 남긴다.
if (ModuleRegistration.ReadAllowedOrigins(builder.Configuration).Length == 0)
{
    app.Logger.LogWarning(
        "Cors:AllowedOrigins 가 비어 있어 모든 오리진을 허용한다. 배포 환경에서는 지정한다.");
}

app.UseCors(ModuleRegistration.CorsPolicy);

// 게임 트래픽은 이 미들웨어를 지나 모듈 엔드포인트로 간다.
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.MapGet("/health", () => Results.Text("ok"));
app.MapModules();

app.Run();
