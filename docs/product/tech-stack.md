# Technical Stack

Versions below are the ones actually resolved in `src/*/*.csproj` and `web/package.json`
as of 2026-08-17, not aspirational targets.

## Required Stack Items

- application_framework: ASP.NET Core Minimal API 10.0 (net10.0, SDK 10.0.302)
- database_system: PostgreSQL 18.6 (postgres:18-alpine)
- javascript_framework: React 19.2
- import_strategy: node
- css_framework: Tailwind CSS 4.3
- ui_component_library: none, components are hand-built in web/src/components
- fonts_provider: self-hosted via Fontsource (Fraunces, Outfit, IBM Plex Mono)
- icon_library: lucide-react 1.31
- application_hosting: self-hosted Docker containers, a YARP gateway in front of the API
- database_hosting: self-hosted Postgres container on a named Docker volume
- asset_hosting: served from the gateway's wwwroot on the same origin as /api (DEC-016)
- deployment_solution: Docker Compose over one Dockerfile with two runtime targets
- local_orchestration: .NET Aspire 13.4.6 AppHost (TodoApp.AppHost)
- authentication_provider: Auth0, PKCE from the SPA and JWT bearer at the API (DEC-011)
- code_repository_url: https://github.com/mgpeter/questward-todo

## Backend

- .NET 10, six projects: TodoApp.Models, TodoApp.Data, TodoApp.Api, TodoApp.Gateway,
  TodoApp.ServiceDefaults and TodoApp.AppHost (DEC-016)
- Microsoft.EntityFrameworkCore 10.0.11 and EntityFrameworkCore.Relational 10.0.11,
  both pinned explicitly because the Npgsql provider only requires 10.0.4 and the Design
  package is PrivateAssets=all, so its newer version does not flow to the API project
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
- Microsoft.EntityFrameworkCore.Design 10.0.11 (design time only)
- Microsoft.AspNetCore.OpenApi 10.0.11 with Scalar.AspNetCore 2.16.20 for the browsable
  API reference at /scalar/v1 in Development

## Frontend

- React 19.2 with React DOM 19.2, TypeScript 6.0, Vite 8.2
- @tanstack/react-query 5.101 for server state and cache invalidation
- motion 13.1 for animation
- @tailwindcss/vite 4.3 (CSS-first configuration, no tailwind.config.js)
- oxlint 1.75 for linting

## Authentication (Planned, Phase 3)

Not yet implemented. The app currently runs with no authentication and a single character
row. The intended shape:

- Auth0 as the identity provider, via standard OIDC so the API-side work stays portable
- SPA: `@auth0/auth0-react`, authorization code plus PKCE
- API: `Microsoft.AspNetCore.Authentication.JwtBearer` validating against the Auth0 issuer
  and API audience
- Tenant domain, client ID and audience supplied as environment variables at runtime, not
  baked into the image
- Users keyed locally by the Auth0 `sub` claim

See DEC-011 for the trade-offs, including the requirement for outbound internet at sign-in.

## Verification Tooling

- playwright-core 1.57 at the repository root, driving the locally installed Chrome via
  `channel: 'chrome'` rather than downloading a browser build
- scripts/verify-api.ps1 exercises the API directly with PowerShell
- scripts/verify-ui.mjs drives the full user flow and writes screenshots to artifacts/

## Infrastructure

- Dockerfile: node:22-alpine builds the SPA, dotnet/sdk:10.0 publishes the API and the
  gateway, and two dotnet/aspnet:10.0 targets (`api` and `gateway`) run them as a non-root
  user on port 8080. BuildKit skips the SPA stage when only the API target is built
- docker-compose.yml: gateway, api and db. Only the gateway publishes a port; the api is
  reachable solely from inside the network, which is what makes it safe for it to trust
  X-Forwarded-For (DEC-016)
- Service discovery: the gateway's YARP destination is the name `http://api` in every
  environment, resolved from `Services__api__http__0` - injected by the AppHost, set by hand
  in docker-compose.yml
- docker-compose.dev.yml: Postgres only, published on 5432 for local development
- The Postgres 18 image sets PGDATA to /var/lib/postgresql/18/docker, so the named volume
  mounts at /var/lib/postgresql rather than the pre-18 conventional path
- Migrations are applied on startup with a bounded retry loop, because the app container
  regularly wins the race against Postgres even behind a Compose healthcheck

## Ports

- 8080: the containerised gateway, the only published port in the compose stack
- 5080: the gateway locally, and the URL to open in development - SPA and /api on one origin
- 5081: the API locally (also reachable at /scalar/v1 through the gateway)
- 5173: the Vite dev server, reached through the gateway; proxies /api to 5081 if opened directly
- 5432: the development Postgres container from docker-compose.dev.yml
- 15080: the Aspire dashboard, when running under TodoApp.AppHost
