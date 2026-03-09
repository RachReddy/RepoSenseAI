FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["RepoSenseAI.csproj", "."]
RUN dotnet restore "RepoSenseAI.csproj"
COPY . .
RUN dotnet build "RepoSenseAI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RepoSenseAI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RepoSenseAI.dll"]
