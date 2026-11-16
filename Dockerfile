# Default Dockerfile for the existing Render Al-Manassa Web service.
# The dashboard is a demo and MUST NOT be used with real student/admin data until authentication is implemented.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/AlManassa.Web/AlManassa.Web.csproj src/AlManassa.Web/
RUN dotnet restore src/AlManassa.Web/AlManassa.Web.csproj
COPY src/AlManassa.Web/ src/AlManassa.Web/
RUN dotnet publish src/AlManassa.Web/AlManassa.Web.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY database/003_sample_curricula.sql /app/database/003_sample_curricula.sql
COPY database/004_demo_final_exams.sql /app/database/004_demo_final_exams.sql
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "AlManassa.Web.dll"]
