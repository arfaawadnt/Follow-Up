# syntax=docker/dockerfile:1
# Multi-stage build: Angular SPA -> .NET publish (SPA baked into wwwroot) -> slim runtime on :5088.

# ---- Stage 1: build the Angular SPA ----
FROM node:20-alpine AS web
WORKDIR /app
COPY web/package.json web/package-lock.json ./web/
RUN cd web && npm ci
COPY web/ ./web/
# angular.json outputPath is ../src/FollowUp.Api/wwwroot (relative to web/), so the SPA lands there.
RUN cd web && npx ng build

# ---- Stage 2: publish the API (wwwroot copied in from the web stage) ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY FollowUp.sln global.json Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/
COPY --from=web /app/src/FollowUp.Api/wwwroot ./src/FollowUp.Api/wwwroot
RUN dotnet publish src/FollowUp.Api/FollowUp.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:5088 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5088
# aspnet image ships a non-root user (APP_UID); run as it (container security).
USER $APP_UID
ENTRYPOINT ["dotnet", "FollowUp.Api.dll"]
