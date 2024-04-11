FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["samples/EntityFrameworkCore.Encryption.Samples.WebApi/EntityFrameworkCore.Encryption.Samples.WebApi.csproj", "samples/EntityFrameworkCore.Encryption.Samples.WebApi/"]
RUN dotnet restore "samples/EntityFrameworkCore.Encryption.Samples.WebApi/EntityFrameworkCore.Encryption.Samples.WebApi.csproj"
COPY . .
WORKDIR "/src/samples/EntityFrameworkCore.Encryption.Samples.WebApi"
RUN dotnet build "EntityFrameworkCore.Encryption.Samples.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EntityFrameworkCore.Encryption.Samples.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EntityFrameworkCore.Encryption.Samples.WebApi.dll"]
