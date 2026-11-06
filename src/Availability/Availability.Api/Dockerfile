# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Availability/Availability.Domain/Availability.Domain.csproj", "src/Availability/Availability.Domain/"]
COPY ["src/Availability/Availability.Application/Availability.Application.csproj", "src/Availability/Availability.Application/"]
COPY ["src/Availability/Availability.Infrastructure/Availability.Infrastructure.csproj", "src/Availability/Availability.Infrastructure/"]
COPY ["src/Availability/Availability.Api/Availability.Api.csproj", "src/Availability/Availability.Api/"]

# Restore dependencies
RUN dotnet restore "src/Availability/Availability.Api/Availability.Api.csproj"

# Copy source code
COPY ["contracts/events/", "contracts/events/"]
COPY ["src/Availability/", "src/Availability/"]

# Build
RUN dotnet build "src/Availability/Availability.Api/Availability.Api.csproj" -c Release -o /app/build

# Publish stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY --from=build /src .
COPY --from=build /app/build /app/build
RUN dotnet publish "src/Availability/Availability.Api/Availability.Api.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p logs

EXPOSE 8080
ENTRYPOINT ["dotnet", "Availability.Api.dll"]
