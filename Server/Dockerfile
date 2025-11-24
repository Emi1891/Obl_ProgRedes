FROM mcr.microsoft.com/dotnet/sdk:8.0

WORKDIR /app

# Instalar tzdata para soportar zonas horarias
RUN apt-get update && apt-get install -y tzdata && rm -rf /var/lib/apt/lists/*

ENV SERVER_HOSTNAME=server
ENV SERVER_PORT=5000

COPY Server/*.csproj ./Server/
COPY Common/*.csproj ./Common/

RUN dotnet restore ./Server

COPY Server/. ./Server
COPY Common/. ./Common

RUN dotnet publish ./Server/Server.csproj -c Release -o /app/publish

ENTRYPOINT ["dotnet", "/app/publish/Server.dll"]

## Para correr el contenedor en la red de docker agregamos --network obl-pdr

## Para correr el contenedor y que acepte inputs de consola agregamos -it

## Linea final: docker run -it --network obl-pdr -v ./ServerFiles:/app/Images --name server server