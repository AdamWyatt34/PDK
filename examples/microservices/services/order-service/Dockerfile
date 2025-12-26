FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["services/order-service/OrderService.csproj", "services/order-service/"]
RUN dotnet restore "services/order-service/OrderService.csproj"

COPY services/order-service/ services/order-service/
WORKDIR /src/services/order-service
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8082
ENV ASPNETCORE_URLS=http://+:8082

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8082/health || exit 1

ENTRYPOINT ["dotnet", "OrderService.dll"]
