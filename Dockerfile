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

# The SQLite database lives in /data. A new named volume copies this directory's owner on first use, so the
# non-root user can write there while the root filesystem stays read-only.
RUN mkdir /data && chown $APP_UID /data

USER $APP_UID

# The health file is written by the running bot; see Bot.Core/Health. This only reads it.
HEALTHCHECK --interval=60s --timeout=30s --start-period=60s --retries=3 CMD ["dotnet", "Bot.Host.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "Bot.Host.dll"]
