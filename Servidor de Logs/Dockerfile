FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Servidor de Logs/Servidor de Logs..csproj", "Servidor de Logs/"]

RUN dotnet restore "Servidor de Logs/Servidor de Logs..csproj"

COPY ["Servidor de Logs", "Servidor de Logs/"]

WORKDIR "/src/Servidor de Logs"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:AssemblyName=ServidorDeLogs

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "ServidorDeLogs.dll"]
