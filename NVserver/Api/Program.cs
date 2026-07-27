using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NV.Api.Middlewares;

// 컴포지션 루트. 컨트롤러를 두지 않는다.
// 엔드포인트는 각 모듈이 MapXxx() 로 등록하고 여기서는 호출만 한다.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseExceptionHandling();

app.MapGet("/health", () => Results.Text("ok"));

app.Run();
