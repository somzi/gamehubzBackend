# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    libfreetype6 \
    fontconfig \
    fonts-liberation \
    && rm -rf /var/lib/apt/lists/*
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

# OVO JE MAGIJA ZA SVG: Kopiramo fontove u Linux OS i osvežavamo keš
RUN mkdir -p /usr/share/fonts/truetype/custom \
    && cp ./Fonts/*.ttf /usr/share/fonts/truetype/custom/ \
    && fc-cache -f -v

# Vraćamo korisnika na non-root zbog sigurnosti
USER $APP_UID
ENTRYPOINT ["dotnet", "GameHubz.Api.dll"]