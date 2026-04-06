FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# Пропускаємо завантаження XML-документації — значно прискорює dotnet restore
ENV NUGET_XMLDOC_MODE=skip

WORKDIR /src

# Копіюємо global.json і nuget.config ДО restore
# щоб SDK і джерела пакетів кешувались у окремому шарі
COPY global.json  ./
COPY nuget.config ./

# Копіюємо лише .csproj для максимального використання Docker-кешу
COPY ["BookingSystem/BookingSystem.csproj", "BookingSystem/"]
RUN dotnet restore "BookingSystem/BookingSystem.csproj"

# Копіюємо код і одразу публікуємо (без проміжного build-кроку)
COPY . .
WORKDIR "/src/BookingSystem"
RUN dotnet publish "./BookingSystem.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        /p:UseAppHost=false \
        --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BookingSystem.dll"]
