FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/GrowIT.Client/GrowIT.Client.csproj src/GrowIT.Client/
COPY src/GrowIT.Core/GrowIT.Core.csproj src/GrowIT.Core/
COPY src/GrowIT.Shared/GrowIT.Shared.csproj src/GrowIT.Shared/
RUN dotnet restore src/GrowIT.Client/GrowIT.Client.csproj

COPY . .
RUN dotnet publish src/GrowIT.Client/GrowIT.Client.csproj -c Release -o /app/publish

FROM nginx:1.27-alpine AS final
WORKDIR /usr/share/nginx/html

COPY docker/nginx/growit-client.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot ./

EXPOSE 80

