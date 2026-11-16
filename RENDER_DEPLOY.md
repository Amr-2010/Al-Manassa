# Deploy Al-Manassa Phase 1 demo on Render

## What is included
- `render.yaml`: Render Blueprint with two independent Docker web services (web + demo API).
- `Dockerfile.web`, `Dockerfile.api`: .NET 10 multi-stage build files.
- `.dockerignore`: exclude build artifacts and local secrets.
- `/health` on both services.

## Steps
1. Push the entire extracted folder contents to a **new** GitHub repository (repository root must contain `render.yaml`). Do not place these files in the Xerox or SakanGo repository.
2. Render Dashboard > New > Blueprint > select the new repository > deploy the Blueprint. Review available plan/pricing before approval.
3. Set `WebOrigin` for the API to the exact public web origin, for example `https://almanassa-web.onrender.com` (replace with the URL Render actually assigns). No trailing slash.
4. Check `https://<your-web-domain>/health` and `https://<your-api-domain>/health`, then open `/` and `/dashboard` on the web service.
5. API and web are still separate **demo** applications; the web does not consume the API. No database service is provisioned by this Blueprint.

## Important security notice
This package is **NOT production ready**. `/dashboard` is public, and `/api/demo/curriculum/suggest` is unauthenticated. The curriculum generator is rule-based, not AI. Do not add real students, payment data, private course materials, AI secrets, or sensitive information. Before production use, implement authentication/authorization, persistence, rate limits, audit logging, AI provider integration, and environment secrets.

## Database
`database/001_schema.sql` is only a starter schema. It is not wired into the API. Provision PostgreSQL separately only after implementing a proper database connection and migrations. Never put a database password in source control.

## Platform binaries
.NET MAUI is source scaffolding only; Render does not build signed Android APK, Windows EXE, or iOS IPA artifacts. These need separate platform build pipelines and signing.

## Local verification
Requires .NET 10 SDK and Docker (neither build nor deployment is verified in this package):
`docker build -f Dockerfile.web -t almanassa-web .`
`docker build -f Dockerfile.api -t almanassa-api .`
