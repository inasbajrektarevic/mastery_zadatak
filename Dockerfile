FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["mastery_task.csproj", "./"]
RUN dotnet restore "mastery_task.csproj"
COPY . .
RUN dotnet publish "mastery_task.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Render (and similar hosts) set PORT (default 10000). Local: omit PORT → 8080 (matches README docker run -p 8080:8080).
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet mastery_task.dll --urls \"http://0.0.0.0:${PORT:-8080}\""]
