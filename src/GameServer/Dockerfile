# Build from the repository root, after build/jentl.sh has packed the engine into .jentl-packages:
#   docker build -f src/GameServer/Dockerfile -t game-server .
# The poker league keeps its owners, bots, ratings and matches in /app/poker-data: mount a volume there to keep them
# (docker run -v poker-data:/app/poker-data ...). Behind a TLS proxy, set JENTL_TRUST_PROXY=true.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props dependencies.props nuget.config ./
COPY .jentl-packages .jentl-packages
COPY src/Uno src/Uno
COPY src/Poker src/Poker
COPY src/Poker.Bots src/Poker.Bots
COPY src/GameServer src/GameServer
RUN dotnet publish src/GameServer -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
ENV JENTL_POKER_DATA=/app/poker-data
VOLUME /app/poker-data
EXPOSE 5080
ENTRYPOINT ["dotnet", "GameServer.dll"]
