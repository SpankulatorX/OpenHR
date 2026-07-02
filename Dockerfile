FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Web/RegionHR.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5076
ENV ASPNETCORE_URLS=http://+:5076
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__RegionHR="Host=regionhr-postgres;Port=5432;Database=openhr;Username=postgres;Password=postgres"
ENTRYPOINT ["dotnet", "RegionHR.Web.dll"]
