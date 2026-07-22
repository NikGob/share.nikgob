FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GDriveApi/GDriveApi.csproj GDriveApi/
RUN dotnet restore GDriveApi/GDriveApi.csproj

COPY GDriveApi/ GDriveApi/
RUN dotnet publish GDriveApi/GDriveApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "GDriveApi.dll"]
