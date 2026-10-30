# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore first so the dependency layer is cached between code changes
COPY ["mortys-twitch-discord-schedulebot.csproj", "."]
RUN dotnet restore "mortys-twitch-discord-schedulebot.csproj"

COPY . .
RUN dotnet publish "mortys-twitch-discord-schedulebot.csproj" -c Release --no-restore -o /app/publish

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
# The runtime image already ships tzdata (needed for Europe/Berlin) and a non-root "app" user.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "mortys-twitch-discord-schedulebot.dll"]
