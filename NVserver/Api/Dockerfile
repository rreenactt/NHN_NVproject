FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 루트 빌드 설정을 먼저 복사한다. 출력 경로 리디렉션과 패키지 버전 중앙 관리가
# 여기 없으면 restore 단계에서 경로와 버전이 달라진다.
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "./"]
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
COPY ["Modules/Realtime/Realtime.csproj", "Modules/Realtime/"]
COPY ["Api/Api.csproj", "Api/"]
RUN dotnet restore "Api/Api.csproj"

COPY Shared/ Shared/
COPY Infrastructure/ Infrastructure/
COPY Modules/ Modules/
COPY Api/ Api/

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Api/Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Api.dll"]
