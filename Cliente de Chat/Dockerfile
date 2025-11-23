FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Cliente de Chat/Cliente de Chat..csproj", "Cliente de Chat/"]

RUN dotnet restore "Cliente de Chat/Cliente de Chat..csproj"

COPY ["Cliente de Chat", "Cliente de Chat/"]

WORKDIR "/src/Cliente de Chat"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:AssemblyName=ClienteDeChat

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "ClienteDeChat.dll"]
