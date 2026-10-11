# Build and publish the API using the .NET 10 SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY LMS.Api/LMS.Api.csproj LMS.Api/
RUN dotnet restore LMS.Api/LMS.Api.csproj

COPY . .
RUN dotnet publish LMS.Api/LMS.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Run with the smaller ASP.NET Core runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish .
RUN mkdir -p /app/storage

ENTRYPOINT ["dotnet", "LMS.Api.dll"]
