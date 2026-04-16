using BookingSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Tests.Helpers;

/// <summary>
/// Фабрика InMemory-контексту для unit-тестів.
/// Кожен виклик Create() повертає ізольований контекст
/// з унікальною базою (Guid), без seed-даних з OnModelCreating.
/// </summary>
public static class TestDbContextFactory
{
    /// <param name="dbName">
    /// Унікальне ім'я БД. Якщо null — генерується Guid,
    /// що гарантує ізоляцію між тестами.
    /// </param>
    public static ApplicationDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        // НЕ викликаємо EnsureCreated(), щоб уникнути seed-даних з HasData().
        // InMemory провайдер не потребує явного створення схеми.
        return new ApplicationDbContext(options);
    }
}
