FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Servidor de Chat/Servidor de Chat.csproj", "Servidor de Chat/"]
COPY ["Servidor de Chat/Protos", "Servidor de Chat/Protos/"]

RUN dotnet restore "Servidor de Chat/Servidor de Chat.csproj"

COPY ["Servidor de Chat", "Servidor de Chat/"]

WORKDIR "/src/Servidor de Chat"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:AssemblyName=ServidorDeChat

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "ServidorDeChat.dll"]
