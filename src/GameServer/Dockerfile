# Build from the repository root, after build/jentl.sh has packed the engine into .jentl-packages:
#   docker build -f src/GameServer/Dockerfile -t game-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props dependencies.props nuget.config ./
COPY .jentl-packages .jentl-packages
COPY src/Uno src/Uno
COPY src/GameServer src/GameServer
RUN dotnet publish src/GameServer -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "GameServer.dll"]
