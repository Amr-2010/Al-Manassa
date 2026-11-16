# المنصة | Al-Manassa — Phase 1 dashboard prototype

ASP.NET Core API + Blazor Interactive Server + .NET MAUI scaffold (Android, iOS, Windows).

## Implemented
- Blazor home and `/dashboard` responsive Arabic admin **prototype**
- Demo course table and session-only curriculum suggestion form
- API `GET /health`, `GET /api/courses`, `GET /api/dashboard/summary`, `POST /api/demo/curriculum/suggest`
- Approved logo **reference PNG** under `assets/brand/approved-logo-reference.png` (not yet a production icon)

## Run
Requires .NET 10 SDK (not installed in the creation environment).

```sh
dotnet run --project src/AlManassa.Web
dotnet run --project src/AlManassa.Api
```

Web and API are currently independent demo processes; Blazor dashboard uses sample data. For API example POST `/api/demo/curriculum/suggest` JSON: `{"topic":"Cybersecurity","level":"Beginner","language":"ar","durationWeeks":8}`.

## NOT implemented yet
No actual AI provider, production database connection, login/auth, role enforcement, course CRUD persistence, real reports, payments, video hosting, exams, certificates, APK/EXE/IPA binaries or live deployment. API demo endpoints MUST NOT be published as a secure administration system. PostgreSQL starter schema exists but is not wired into the API. MAUI projects are scaffolds only. iOS packaging requires Mac/Xcode/signing.

## Next milestones
1. ASP.NET Core Identity, Super Admin policy and protected admin APIs.
2. EF Core PostgreSQL migrations and persistent courses/users/enrollments.
3. AI provider integration via server-side secrets, quota and approval workflow.
4. MAUI client integration, build and platform tests.


## Render deployment
See [RENDER_DEPLOY.md](RENDER_DEPLOY.md). The Render Blueprint is demo-only.
