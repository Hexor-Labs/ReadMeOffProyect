# Multi-etapa: el SDK (grande, con compilador) solo existe durante la
# construcción; la imagen que se despliega lleva solo el runtime.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Primero los archivos de proyecto y las versiones centralizadas: el restore
# queda cacheado y no se repite cuando cambia el código, solo cuando cambian
# las dependencias.
COPY Directory.Build.props Directory.Packages.props ./
COPY packages/SharedKernel/SharedKernel.csproj packages/SharedKernel/
COPY services/OrderService/OrderService.csproj services/OrderService/
RUN dotnet restore services/OrderService/OrderService.csproj

COPY packages/ packages/
COPY services/OrderService/ services/OrderService/
RUN dotnet publish services/OrderService/OrderService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Usuario sin privilegios. Un contenedor que corre como root convierte
# cualquier ejecución remota de código en control del contenedor entero.
RUN adduser --disabled-password --gecos "" --uid 64198 hub && chown -R hub /app
USER hub

COPY --from=build --chown=hub /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "HubNegocios.OrderService.dll"]
