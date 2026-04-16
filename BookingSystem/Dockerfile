FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# Пропускаємо завантаження XML-документації — значно прискорює dotnet restore
ENV NUGET_XMLDOC_MODE=skip

WORKDIR /src

# Копіюємо .csproj всіх проектів для максимального Docker-кешу при restore
COPY ["BookingSystem/BookingSystem.csproj",                       "BookingSystem/"]
COPY ["BookingSystem.Domain/BookingSystem.Domain.csproj",         "BookingSystem.Domain/"]
COPY ["BookingSystem.Application/BookingSystem.Application.csproj", "BookingSystem.Application/"]
COPY ["BookingSystem.Infrastructure/BookingSystem.Infrastructure.csproj", "BookingSystem.Infrastructure/"]

# Відновлюємо залежності для всього рішення
RUN dotnet restore "BookingSystem/BookingSystem.csproj"

# Копіюємо весь код і публікуємо Web-проект
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
