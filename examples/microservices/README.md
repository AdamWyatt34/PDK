# Microservices Example

A multi-service architecture demonstrating PDK with parallel job execution, job dependencies, and Docker composition.

## What This Example Demonstrates

- Parallel job execution (3 services build simultaneously)
- Job dependencies (Docker builds wait for unit tests)
- Docker multi-stage builds
- Service composition with docker-compose
- Health check endpoints
- Integration testing

## Architecture

```
                    +------------------+
                    |   API Gateway    |
                    |   (Port 8080)    |
                    +--------+---------+
                             |
              +--------------+--------------+
              |                             |
      +-------v-------+           +---------v------+
      | User Service  |           | Order Service  |
      | (Port 8081)   |           | (Port 8082)    |
      +---------------+           +----------------+
```

## Project Structure

```
microservices/
├── .github/workflows/ci.yml      # GitHub Actions workflow
├── services/
│   ├── api-gateway/              # API Gateway service
│   │   ├── Program.cs
│   │   ├── ApiGateway.csproj
│   │   └── Dockerfile
│   ├── user-service/             # User management service
│   │   ├── Program.cs
│   │   ├── UserService.csproj
│   │   └── Dockerfile
│   └── order-service/            # Order processing service
│       ├── Program.cs
│       ├── OrderService.csproj
│       └── Dockerfile
├── tests/                        # Test projects
│   ├── ApiGateway.Tests/
│   ├── UserService.Tests/
│   └── OrderService.Tests/
├── docker-compose.yml            # Service composition
├── Microservices.sln             # Solution file
└── README.md                     # This file
```

## Prerequisites

- .NET 8.0 SDK
- Docker and Docker Compose
- PDK installed

## The Pipeline

The CI workflow demonstrates:

1. **Parallel Builds**: Three services build and test simultaneously
2. **Job Dependencies**: Docker image builds wait for all tests to pass
3. **Integration Tests**: Full stack testing with docker-compose

```
Pipeline Flow:
+-------------------+   +-------------------+   +-------------------+
| Build API Gateway |   | Build User Svc    |   | Build Order Svc   |
| - Restore         |   | - Restore         |   | - Restore         |
| - Build           |   | - Build           |   | - Build           |
| - Test            |   | - Test            |   | - Test            |
+--------+----------+   +--------+----------+   +--------+----------+
         |                       |                       |
         +-----------+-----------+-----------+-----------+
                     |
           +---------v---------+
           | Build Docker      |
           | Images            |
           +--------+----------+
                    |
           +--------v----------+
           | Integration Tests |
           +-------------------+
```

## Running with PDK

### Run the entire pipeline

```bash
pdk run
```

### Run specific service build

```bash
# Build and test API Gateway only
pdk run --job build-api-gateway

# Build and test User Service only
pdk run --job build-user-service
```

### Watch mode for development

```bash
# Watch API Gateway for changes
pdk run --watch --job build-api-gateway
```

### Dry-run to validate pipeline

```bash
pdk run --dry-run
```

### Run with step filtering

```bash
# Run only restore and build steps
pdk run --step "Restore" --step "Build"

# Skip Docker image builds
pdk run --skip-step "Build API Gateway Image"
```

## Running Locally

### Build all services

```bash
dotnet build Microservices.sln
```

### Run tests

```bash
dotnet test Microservices.sln
```

### Start with Docker Compose

```bash
docker compose up --build
```

### Test the services

```bash
# API Gateway
curl http://localhost:8080/health
curl http://localhost:8080/api/status

# User Service
curl http://localhost:8081/api/users
curl http://localhost:8081/api/users/1

# Order Service
curl http://localhost:8082/api/orders
curl http://localhost:8082/api/orders/user/1
```

### Stop services

```bash
docker compose down
```

## Expected Output

When running `pdk run`, you should see:

```
Pipeline: Microservices CI

[Parallel Jobs]
  Job: build-api-gateway
    - Checkout code .................. OK
    - Setup .NET ..................... OK
    - Restore API Gateway ............ OK
    - Build API Gateway .............. OK
    - Test API Gateway ............... OK

  Job: build-user-service
    - Checkout code .................. OK
    - Setup .NET ..................... OK
    - Restore User Service ........... OK
    - Build User Service ............. OK
    - Test User Service .............. OK

  Job: build-order-service
    - Checkout code .................. OK
    - Setup .NET ..................... OK
    - Restore Order Service .......... OK
    - Build Order Service ............ OK
    - Test Order Service ............. OK

[Sequential Jobs]
  Job: build-docker-images
    - Build API Gateway Image ........ OK
    - Build User Service Image ....... OK
    - Build Order Service Image ...... OK

  Job: integration-tests
    - Start services ................. OK
    - Health checks .................. OK
    - Stop services .................. OK

Pipeline completed in 2m 15s
```

## API Endpoints

### API Gateway (Port 8080)

| Endpoint | Description |
|----------|-------------|
| `GET /` | Service info |
| `GET /health` | Health check |
| `GET /api/status` | Gateway status and available endpoints |

### User Service (Port 8081)

| Endpoint | Description |
|----------|-------------|
| `GET /` | Service info |
| `GET /health` | Health check |
| `GET /api/users` | List all users |
| `GET /api/users/{id}` | Get user by ID |

### Order Service (Port 8082)

| Endpoint | Description |
|----------|-------------|
| `GET /` | Service info |
| `GET /health` | Health check |
| `GET /api/orders` | List all orders |
| `GET /api/orders/{id}` | Get order by ID |
| `GET /api/orders/user/{userId}` | Get orders by user |

## Customization

To add a new service:

1. Create a new directory under `services/`
2. Add a minimal API project with health endpoint
3. Add a Dockerfile following the existing pattern
4. Add the service to `docker-compose.yml`
5. Add a new job to `.github/workflows/ci.yml`
6. Create tests under `tests/`

## Troubleshooting

### Docker build fails

```bash
# Ensure Docker is running
docker info

# Build with verbose output
docker compose build --no-cache
```

### Tests fail

```bash
# Run tests with detailed output
dotnet test --verbosity detailed
```

### Port conflicts

If ports 8080-8082 are in use, modify `docker-compose.yml`:

```yaml
ports:
  - "9080:8080"  # Map to different host port
```

## Related Examples

- [dotnet-console](../dotnet-console) - Simple .NET console app
- [dotnet-webapi](../dotnet-webapi) - Single Web API example
- [docker-app](../docker-app) - Docker multi-stage build
