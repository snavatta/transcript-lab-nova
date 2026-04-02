# Stage 1: Build frontend
FROM node:22-alpine AS frontend-build
WORKDIR /app/frontend
COPY src/frontend/package.json src/frontend/package-lock.json* ./
RUN npm ci
COPY src/frontend/ ./
RUN npm run build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /app
COPY src/ClassTranscriber.slnx ./
COPY src/ClassTranscriber.Api/ClassTranscriber.Api.csproj ClassTranscriber.Api/
COPY src/ClassTranscriber.Api.Tests/ClassTranscriber.Api.Tests.csproj ClassTranscriber.Api.Tests/
RUN dotnet restore ClassTranscriber.slnx
COPY src/ClassTranscriber.Api/ ClassTranscriber.Api/
COPY src/ClassTranscriber.Api.Tests/ ClassTranscriber.Api.Tests/
RUN dotnet publish ClassTranscriber.Api/ClassTranscriber.Api.csproj -c Release -o /app/publish --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=backend-build /app/publish ./
COPY --from=frontend-build /app/frontend/dist ./wwwroot/

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Storage__BasePath=/data

EXPOSE 5000
VOLUME /data

ENTRYPOINT ["dotnet", "ClassTranscriber.Api.dll"]
