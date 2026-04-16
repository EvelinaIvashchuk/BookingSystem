FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# Прискорює restore: пропускаємо XML-документацію пакетів
ENV NUGET_XMLDOC_MODE=skip

WORKDIR /src

# Копіюємо .csproj всіх проектів — ще один шар кешу (restore не запускається при зміні лише .cs)
COPY ["BookingSystem/BookingSystem.csproj",                                   "BookingSystem/"]
COPY ["BookingSystem.Domain/BookingSystem.Domain.csproj",                     "BookingSystem.Domain/"]
COPY ["BookingSystem.Application/BookingSystem.Application.csproj",           "BookingSystem.Application/"]
COPY ["BookingSystem.Infrastructure/BookingSystem.Infrastructure.csproj",     "BookingSystem.Infrastructure/"]
COPY ["BookingSystem.Tests/BookingSystem.Tests.csproj",                       "BookingSystem.Tests/"]

# Відновлюємо залежності для всього рішення
RUN dotnet restore "BookingSystem/BookingSystem.csproj"

# ─────────────────────────────────────────────────────────────────────────────
# Тестовий stage (опціональний — не потрапляє у фінальний образ)
FROM build AS test
COPY . .
RUN dotnet test "BookingSystem.Tests/BookingSystem.Tests.csproj" \
        --no-restore \
        --configuration Debug \
        --logger "console;verbosity=minimal"

# ─────────────────────────────────────────────────────────────────────────────
FROM build AS publish
COPY . .
WORKDIR "/src/BookingSystem"
RUN dotnet publish "./BookingSystem.csproj" \
        -c $BUILD_CONFIGURATION \
        -o /app/publish \
        /p:UseAppHost=false \
        --no-restore

# ─────────────────────────────────────────────────────────────────────────────
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BookingSystem.dll"]
