# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first so the dependency layer is cached between code changes
COPY ["Bot.Host/Bot.Host.csproj", "Bot.Host/"]
COPY ["Bot.Core/Bot.Core.csproj", "Bot.Core/"]
COPY ["Modules/TwitchSchedule/Modules.TwitchSchedule.csproj", "Modules/TwitchSchedule/"]
RUN dotnet restore "Bot.Host/Bot.Host.csproj"

COPY . .
RUN dotnet publish "Bot.Host/Bot.Host.csproj" -c Release --no-restore -o /app/publish

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
# The runtime image already ships tzdata (needed for Europe/Berlin) and a non-root "app" user.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "Bot.Host.dll"]
