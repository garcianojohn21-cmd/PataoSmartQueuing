# Use .NET 8 SDK to build (matches your project)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy from the subfolder where .csproj actually is
COPY PataoSmartQueuing/PataoSmartQueuing.csproj ./PataoSmartQueuing/
RUN dotnet restore ./PataoSmartQueuing/PataoSmartQueuing.csproj

# Copy everything and build
COPY PataoSmartQueuing/ ./PataoSmartQueuing/
WORKDIR /src/PataoSmartQueuing
RUN dotnet publish -c Release -o /app/publish

# Runtime image - also .NET 8
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "PataoSmartQueuing.dll"]
