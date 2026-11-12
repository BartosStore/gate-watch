# 1) Frontend build (Vite) -> /src/Api/wwwroot
FROM node:24-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# 2) .NET publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj
COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app --no-restore

# Tests (only built on demand: docker compose run --rm --build tests). Playwright image so browser tests run too;
# the compose service adds Postgres for the DB tests.
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble AS test
WORKDIR /src
COPY GateWatch.slnx ./
COPY src/Api/Api.csproj src/Api/
COPY tests/Api.Tests/Api.Tests.csproj tests/Api.Tests/
RUN dotnet restore tests/Api.Tests/Api.Tests.csproj
COPY src/Api/ src/Api/
COPY tests/ tests/
COPY data/ data/
RUN dotnet build tests/Api.Tests --no-restore
CMD ["dotnet", "test", "tests/Api.Tests", "--no-build"]

# 3) Runtime: Playwright image (Chromium + .NET 10). Version must match the Microsoft.Playwright NuGet package.
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /src/Api/wwwroot ./wwwroot
COPY data ./data
# dotnet publish drops the exec bit of Playwright's bundled Node driver.
RUN chmod +x /app/.playwright/node/*/node
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
# Non-root user shipped with the Playwright image.
USER pwuser
ENTRYPOINT ["dotnet", "Api.dll"]
