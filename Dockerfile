# Inkwell — container image. Multi-stage: build with the SDK, run on the smaller ASP.NET runtime.
#
# Build:  docker build -t inkwell .
# Run:    docker run -d -p 8080:8080 \
#           -e ConnectionStrings__DefaultConnection="Server=host;Database=blog;User Id=sa;Password=...;TrustServerCertificate=True;" \
#           -v inkwell-keys:/data/keys -e DataProtection__KeysPath=/data/keys \
#           -v inkwell-uploads:/app/wwwroot/uploads \
#           --name inkwell inkwell
#
# For a complete stack (SQL Server + automatic TLS via Caddy), use docker-compose.yml instead —
# see the "Deploy with Docker" section of README.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on just the project files, so this layer is cached across code-only changes.
COPY Directory.Build.props ./
COPY Blog.Core/Blog.Core.csproj Blog.Core/
COPY Blog.Infrastructure/Blog.Infrastructure.csproj Blog.Infrastructure/
COPY Blog.Web/Blog.Web.csproj Blog.Web/
RUN dotnet restore Blog.Web/Blog.Web.csproj

# Now the rest of the source. wwwroot/uploads is excluded by .dockerignore — uploads are tenant
# content, mounted as a volume at runtime, never baked into the image (see docker-compose.yml).
COPY Blog.Core/ Blog.Core/
COPY Blog.Infrastructure/ Blog.Infrastructure/
COPY Blog.Web/ Blog.Web/
RUN dotnet publish Blog.Web/Blog.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl for the HEALTHCHECK below; a non-root user for the app itself.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && adduser --disabled-password --gecos "" --uid 1654 inkwell \
    && mkdir -p /app/wwwroot/uploads /app/wwwroot/og-cache /data/keys \
    && chown -R inkwell:inkwell /app /data
USER inkwell

COPY --from=build --chown=inkwell:inkwell /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# /robots.txt needs no authentication and is served for every tenant, including one with no DB
# reachable (fail-open), so it is a reasonable liveness probe without depending on app state.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["curl", "-f", "http://localhost:8080/robots.txt"]

ENTRYPOINT ["dotnet", "Blog.Web.dll"]
