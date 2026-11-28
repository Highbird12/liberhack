# Builds and runs the app in a container, for hosts such as Render.
#   docker build -t booklibrary .
#   docker run -p 10000:10000 -e PORT=10000 -e ConnectionStrings__Default='...' \
#     -e Admin__Email=you@example.com -e Admin__Password='...' booklibrary

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, on its own layer, so rebuilds reuse the downloaded packages.
COPY BookLibrary.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish BookLibrary.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Admin uploads (covers and PDFs) are written here. The folder is writable for the non-root user,
# but a container's disk is wiped on every deploy, so uploads need a persistent disk or
# external storage before they can be relied on (see the README).
RUN mkdir -p /app/wwwroot/uploads && chown -R app:app /app/wwwroot/uploads

# The host's proxy terminates HTTPS and talks plain HTTP to the container. Trust its
# X-Forwarded-* headers so the app knows the original request was HTTPS.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# The listening port comes from the PORT variable the host sets (Render uses 10000).
ENV PORT=10000
EXPOSE 10000

USER app
ENTRYPOINT ["dotnet", "BookLibrary.dll"]
