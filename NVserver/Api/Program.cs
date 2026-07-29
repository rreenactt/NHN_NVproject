using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

// 게임 트래픽은 이 미들웨어를 지나 모듈 엔드포인트로 간다.
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.MapGet("/health", () => Results.Text("ok"));
app.MapModules();

app.Run();
