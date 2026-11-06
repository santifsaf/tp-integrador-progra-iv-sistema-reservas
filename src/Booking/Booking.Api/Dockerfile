# Imagen de Booking.Api.
# El contexto de build es la RAÍZ del repo (no esta carpeta), porque Booking compila
# archivos de /contracts: el mensaje del broker y el payment.proto.
#   docker build -f src/Booking/Booking.Api/Dockerfile -t booking-api .
# Desde infra/ se levanta con: docker compose up --build booking-api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# 1) Restore aparte para aprovechar la caché de Docker: solo se repite si cambia un .csproj.
COPY src/Booking/Booking.Api/Booking.Api.csproj src/Booking/Booking.Api/
COPY src/Booking/Booking.Business/Booking.Business.csproj src/Booking/Booking.Business/
COPY src/Booking/Booking.Data/Booking.Data.csproj src/Booking/Booking.Data/
RUN dotnet restore src/Booking/Booking.Api/Booking.Api.csproj

# 2) Código: contratos compartidos y las tres capas de Booking (sin tests ni Demo).
COPY contracts/ contracts/
COPY src/Booking/Booking.Api/ src/Booking/Booking.Api/
COPY src/Booking/Booking.Business/ src/Booking/Booking.Business/
COPY src/Booking/Booking.Data/ src/Booking/Booking.Data/
RUN dotnet publish src/Booking/Booking.Api/Booking.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# La imagen de ASP.NET escucha en 8080 y corre con el usuario no root "app".
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Booking.Api.dll"]
