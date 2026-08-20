# syntax=docker/dockerfile:1

# One file, two images. `target: api` and `target: gateway` in docker-compose.yml pick
# between them, and BuildKit skips the stages a target does not need - so building the API
# never builds the SPA.

# ---------------------------------------------------------------- 1. the SPA
FROM node:22-alpine AS web
WORKDIR /web

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
# vite.config.ts points outDir at the gateway's wwwroot for local development; inside the
# image that path does not exist, so the output is redirected to a local dist folder.
RUN npm run build -- --outDir dist --emptyOutDir

# ------------------------------------------------------- 2. both .NET services
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Project files first so the restore layer is cached across source-only changes.
COPY src/TodoApp.Models/TodoApp.Models.csproj src/TodoApp.Models/
COPY src/TodoApp.Data/TodoApp.Data.csproj src/TodoApp.Data/
COPY src/TodoApp.ServiceDefaults/TodoApp.ServiceDefaults.csproj src/TodoApp.ServiceDefaults/
COPY src/TodoApp.Api/TodoApp.Api.csproj src/TodoApp.Api/
COPY src/TodoApp.Gateway/TodoApp.Gateway.csproj src/TodoApp.Gateway/
RUN dotnet restore src/TodoApp.Api/TodoApp.Api.csproj \
 && dotnet restore src/TodoApp.Gateway/TodoApp.Gateway.csproj

# Copies TodoApp.AppHost too, which is harmless: restore and publish are invoked per-project,
# so the Aspire SDK is never resolved inside the image.
COPY src/ src/
RUN dotnet publish src/TodoApp.Api/TodoApp.Api.csproj         -c Release -o /app/api     --no-restore \
 && dotnet publish src/TodoApp.Gateway/TodoApp.Gateway.csproj -c Release -o /app/gateway --no-restore

# --------------------------------------------------------- 3. the API runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app

COPY --from=build /app/api ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "TodoApp.Api.dll"]

# ----------------------------------------------------- 4. the gateway runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS gateway
WORKDIR /app

COPY --from=build /app/gateway ./
COPY --from=web   /web/dist    ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "TodoApp.Gateway.dll"]
