FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/user-service/UserService.csproj", "services/user-service/"]
RUN dotnet restore "services/user-service/UserService.csproj"

COPY services/user-service/ services/user-service/
WORKDIR /src/services/user-service
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8081

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8081/health || exit 1

ENTRYPOINT ["dotnet", "UserService.dll"]
