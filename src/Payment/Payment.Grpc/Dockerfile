# Imagen de Payment.Grpc.
# El contexto de build es la RAÍZ del repo (no esta carpeta), porque Payment compila
# el contrato compartido /contracts/proto/payment.proto.
#   docker build -f src/Payment/Payment.Grpc/Dockerfile -t payment-api .
# Desde infra/ se levanta con: docker compose up --build payment-api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# 1) Restore aparte para aprovechar la caché de Docker: solo se repite si cambia un .csproj.
COPY src/Payment/Payment.Grpc/Payment.Grpc.csproj src/Payment/Payment.Grpc/
COPY src/Payment/Payment.Business/Payment.Business.csproj src/Payment/Payment.Business/
COPY src/Payment/Payment.Data/Payment.Data.csproj src/Payment/Payment.Data/
RUN dotnet restore src/Payment/Payment.Grpc/Payment.Grpc.csproj

# 2) Código: contrato compartido y las tres capas de Payment (sin tests).
COPY contracts/ contracts/
COPY src/Payment/Payment.Grpc/ src/Payment/Payment.Grpc/
COPY src/Payment/Payment.Business/ src/Payment/Payment.Business/
COPY src/Payment/Payment.Data/ src/Payment/Payment.Data/
RUN dotnet publish src/Payment/Payment.Grpc/Payment.Grpc.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# La imagen de ASP.NET escucha en 8080 y corre con el usuario no root "app".
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Payment.Grpc.dll"]
