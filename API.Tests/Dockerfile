FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["API.Tests/API.Tests.csproj", "API.Tests/packages.lock.json", "API.Tests/"]
COPY ["API/API.csproj", "API/packages.lock.json", "API/"]
RUN dotnet restore "API.Tests/API.Tests.csproj" --locked-mode
COPY . .
WORKDIR "/src/API.Tests"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./API.Tests.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "API.Tests.dll"]
