FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MortgageApp/MortgageApp.csproj MortgageApp/
RUN dotnet restore MortgageApp/MortgageApp.csproj

COPY MortgageApp/ MortgageApp/
RUN dotnet publish MortgageApp/MortgageApp.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "MortgageApp.dll"]
