FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["samples/EntityFrameworkCore.Samples.Encryption.Aes/EntityFrameworkCore.Samples.Encryption.Aes.csproj", "samples/EntityFrameworkCore.Samples.Encryption.Aes/"]
RUN dotnet restore "samples/EntityFrameworkCore.Samples.Encryption.Aes/EntityFrameworkCore.Samples.Encryption.Aes.csproj"
COPY . .
WORKDIR "/src/samples/EntityFrameworkCore.Samples.Encryption.Aes"
RUN dotnet build "EntityFrameworkCore.Samples.Encryption.Aes.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EntityFrameworkCore.Samples.Encryption.Aes.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EntityFrameworkCore.Samples.Encryption.Aes.dll"]
