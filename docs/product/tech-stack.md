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
- application_hosting: self-hosted Docker container
- database_hosting: self-hosted Postgres container on a named Docker volume
- asset_hosting: served from the API's wwwroot on the same origin
- deployment_solution: Docker Compose over a three-stage Dockerfile
- authentication_provider: Auth0 (planned, see DEC-011; not yet implemented)
- code_repository_url: not yet published; a public GitHub repository is planned

## Backend

- .NET 10, three projects: TodoApp.Models, TodoApp.Data, TodoApp.Api
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

- Dockerfile: three stages, node:22-alpine builds the SPA, dotnet/sdk:10.0 publishes the
  API, dotnet/aspnet:10.0 runs it as a non-root user on port 8080
- docker-compose.yml: app plus db, with the app gated on a db healthcheck
- docker-compose.dev.yml: Postgres only, published on 5432 for local development
- The Postgres 18 image sets PGDATA to /var/lib/postgresql/18/docker, so the named volume
  mounts at /var/lib/postgresql rather than the pre-18 conventional path
- Migrations are applied on startup with a bounded retry loop, because the app container
  regularly wins the race against Postgres even behind a Compose healthcheck

## Ports

- 8080: the containerised app, API and SPA on one origin
- 5080: the API when run locally with `dotnet run`
- 5173: the Vite dev server, proxying /api to 5080
- 5432: the development Postgres container
