# syntax=docker/dockerfile:1

# ---------------------------------------------------------------- 1. the SPA
FROM node:22-alpine AS web
WORKDIR /web

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
# vite.config.ts points outDir at the API's wwwroot for local development; inside the
# image that path does not exist, so the output is redirected to a local dist folder.
RUN npm run build -- --outDir dist --emptyOutDir

# ----------------------------------------------------------------- 2. the API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /source

# Project files first so the restore layer is cached across source-only changes.
COPY src/TodoApp.Models/TodoApp.Models.csproj src/TodoApp.Models/
COPY src/TodoApp.Data/TodoApp.Data.csproj src/TodoApp.Data/
COPY src/TodoApp.Api/TodoApp.Api.csproj src/TodoApp.Api/
RUN dotnet restore src/TodoApp.Api/TodoApp.Api.csproj

COPY src/ src/
RUN dotnet publish src/TodoApp.Api/TodoApp.Api.csproj -c Release -o /app --no-restore

# ------------------------------------------------------------- 3. the runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "TodoApp.Api.dll"]
