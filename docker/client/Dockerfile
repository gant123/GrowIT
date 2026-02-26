# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY src/GrowIT.Client/GrowIT.Client.csproj src/GrowIT.Client/
COPY src/GrowIT.Core/GrowIT.Core.csproj src/GrowIT.Core/
COPY src/GrowIT.Shared/GrowIT.Shared.csproj src/GrowIT.Shared/

# Restore
RUN dotnet restore src/GrowIT.Client/GrowIT.Client.csproj

# Copy the rest of the source
COPY src/ ./src/

# Publish the client (WASM)
RUN dotnet publish src/GrowIT.Client/GrowIT.Client.csproj -c Release -o /app/publish

# BULLETPROOF BYPASS: 
# Find the hashed blazor.webassembly.xxx.js file and copy it to the standard name Nginx expects.
RUN cd /app/publish/wwwroot/_framework && \
    cp blazor.webassembly.*.js blazor.webassembly.js || true

# ---- runtime (nginx) ----
FROM nginx:1.27-alpine AS runtime

COPY docker/nginx/growit-client.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot/ /usr/share/nginx/html/

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]