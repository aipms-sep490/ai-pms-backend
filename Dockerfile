FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/AIPMS.Api/AIPMS.Api.csproj -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# Match the existing WSL user's UID/GID so private config and bind mounts stay private.
RUN groupmod --gid 1000 app && usermod --uid 1000 --gid 1000 app \
    && apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/aipms/files /var/lib/aipms/keys /var/log/aipms \
    && chown -R app:app /var/lib/aipms /var/log/aipms /home/app
WORKDIR /app
COPY --from=build /publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "AIPMS.Api.dll"]
