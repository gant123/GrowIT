FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY global.json ./
COPY src/GrowIT.API/GrowIT.API.csproj src/GrowIT.API/
COPY src/GrowIT.Core/GrowIT.Core.csproj src/GrowIT.Core/
COPY src/GrowIT.Infrastructure/GrowIT.Infrastructure.csproj src/GrowIT.Infrastructure/
COPY src/GrowIT.Shared/GrowIT.Shared.csproj src/GrowIT.Shared/
RUN dotnet restore src/GrowIT.API/GrowIT.API.csproj

COPY src/ ./src/
RUN dotnet publish src/GrowIT.API/GrowIT.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

RUN mkdir -p /app/wwwroot/uploads/profile-photos /app/dev-emails

ENTRYPOINT ["dotnet", "GrowIT.API.dll"]
