# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
# MORA ROOT DA BI INSTALIRAO PAKETE
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    libfreetype6 \
    fontconfig \
    fonts-liberation \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# curl-impersonate: a curl build whose TLS (JA3) + HTTP/2 fingerprint matches a real Chrome.
# Needed for Kick VOD resolution — kick.com's web API sits behind Cloudflare bot management that
# 403s .NET HttpClient AND the stock Debian curl (7.88 / OpenSSL 3.0). The impersonate build
# returns 200. KickStreamClient shells out to curl_chrome116 (see Streaming:Kick:CurlCommand).
ARG CURL_IMPERSONATE_VERSION=0.6.1
RUN curl -sL "https://github.com/lwthiker/curl-impersonate/releases/download/v${CURL_IMPERSONATE_VERSION}/curl-impersonate-v${CURL_IMPERSONATE_VERSION}.x86_64-linux-gnu.tar.gz" -o /tmp/ci.tar.gz \
    && mkdir -p /opt/curl-impersonate \
    && tar -xzf /tmp/ci.tar.gz -C /opt/curl-impersonate \
    && rm /tmp/ci.tar.gz \
    && chmod -R 755 /opt/curl-impersonate
ENV PATH="/opt/curl-impersonate:${PATH}"

WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GameHubz.Api/GameHubz.Api.csproj", "GameHubz.Api/"]
COPY ["GameHubz.Common/GameHubz.Common.csproj", "GameHubz.Common/"]
COPY ["GameHubz.DataModels/GameHubz.DataModels.csproj", "GameHubz.DataModels/"]
COPY ["GameHubz.Data/GameHubz.Data.csproj", "GameHubz.Data/"]
COPY ["GameHubz.Logic/GameHubz.Logic.csproj", "GameHubz.Logic/"]
COPY ["GameHubz.DataMigrations/GameHubz.DataMigrations.csproj", "GameHubz.DataMigrations/"]
COPY ["GameHubz.Localization/GameHubz.Localization.csproj", "GameHubz.Localization/"]
RUN dotnet restore "./GameHubz.Api/GameHubz.Api.csproj"
COPY . .
WORKDIR "/src/GameHubz.Api"
RUN dotnet build "./GameHubz.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GameHubz.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# OVO JE FALILO: Kopiranje tvojih Inter fontova u sam Linux!
RUN mkdir -p /usr/share/fonts/truetype/custom \
    && cp ./Fonts/*.ttf /usr/share/fonts/truetype/custom/ \
    && fc-cache -f -v

# NA SAMOM KRAJU vracamo na bezbednog korisnika
USER $APP_UID
ENTRYPOINT ["dotnet", "GameHubz.Api.dll"]