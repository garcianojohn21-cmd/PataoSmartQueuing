# Use .NET 9 SDK to build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy from the subfolder where .csproj actually is
COPY PataoSmartQueuing/PataoSmartQueuing.csproj ./PataoSmartQueuing/
RUN dotnet restore ./PataoSmartQueuing/PataoSmartQueuing.csproj

# Copy everything and build
COPY PataoSmartQueuing/ ./PataoSmartQueuing/
WORKDIR /src/PataoSmartQueuing
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "PataoSmartQueuing.dll"]