# Build from the repository root: docker build -f src/Jentl.Server/Dockerfile -t jentl-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props dependencies.props ./
COPY src/Jentl.Net src/Jentl.Net
COPY src/Jentl.Multiplayer src/Jentl.Multiplayer
COPY src/Uno src/Uno
COPY src/Jentl.Server src/Jentl.Server
RUN dotnet publish src/Jentl.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "Jentl.Server.dll"]
