FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DocuMind.Api/DocuMind.Api.csproj DocuMind.Api/
RUN dotnet restore DocuMind.Api/DocuMind.Api.csproj
COPY DocuMind.Api/ DocuMind.Api/
RUN dotnet publish DocuMind.Api/DocuMind.Api.csproj -c Release -o /app --no-restore

# One image runs as the API, the worker or the migration job:
#   Jobs__Mode=Api | Worker | All   and   `--migrate` as the command argument
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__RootPath=/data/uploads \
    DataProtection__KeysPath=/data/keys
RUN mkdir -p /data/uploads /data/keys && chown -R $APP_UID /data /app
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocuMind.Api.dll"]
