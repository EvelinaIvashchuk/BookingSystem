using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data;

/// <summary>
/// Demo fleet: together with the 6 cars seeded by the InitialCreate migration it gives 50 cars covering
/// all categories, six cities, both transmissions and all fuel types. Used by the web application
/// (seeded at startup) and by the evaluation database, so search behaves the same in both.
/// Cars are matched by licence plate: seeding only adds missing cars and never changes existing ones.
/// </summary>
public static class DemoFleet
{
    private const int Sedan = 1, Suv = 2, Hatchback = 3, Minivan = 4, Coupe = 5;

    private const string Kyiv = "Київ, вул. Хрещатик, 10";
    private const string Kyiv2 = "Київ, вул. Велика Васильківська, 25";
    private const string Lviv = "Львів, пр. Свободи, 5";
    private const string Odesa = "Одеса, вул. Дерибасівська, 15";
    private const string Kharkiv = "Харків, вул. Сумська, 20";
    private const string Dnipro = "Дніпро, пр. Яворницького, 40";
    private const string Chernivtsi = "Чернівці, вул. Головна, 1";

    /// <summary>Cars 7–50 with their ids in the evaluation database.</summary>
    public static IReadOnlyList<Car> Create() =>
    [
        Car(7, "Skoda", "Octavia", 2023, Sedan, Transmission.Automatic, FuelType.Diesel, 5, 1100, Kyiv2, "Практичний седан з великим багажником та економним дизелем."),
        Car(8, "Hyundai", "Elantra", 2024, Sedan, Transmission.Automatic, FuelType.Petrol, 5, 1000, Lviv, "Сучасний седан з мультимедіа та камерою заднього огляду."),
        Car(9, "Volkswagen", "Passat", 2022, Sedan, Transmission.Manual, FuelType.Diesel, 5, 1300, Odesa, "Просторий бізнес-седан для далеких поїздок."),
        Car(10, "Kia", "K5", 2024, Sedan, Transmission.Automatic, FuelType.Petrol, 5, 1400, Dnipro, "Динамічний седан з адаптивним круїз-контролем."),
        Car(11, "Toyota", "Corolla", 2023, Sedan, Transmission.Automatic, FuelType.Hybrid, 5, 1250, Kharkiv, "Економний гібрид для міста та траси."),
        Car(12, "Renault", "Duster", 2022, Suv, Transmission.Manual, FuelType.Diesel, 5, 1000, Chernivtsi, "Невибагливий кросовер з повним приводом для гірських доріг."),
        Car(13, "Toyota", "RAV4", 2024, Suv, Transmission.Automatic, FuelType.Hybrid, 5, 2000, Lviv, "Гібридний кросовер з повним приводом."),
        Car(14, "Kia", "Sportage", 2023, Suv, Transmission.Automatic, FuelType.Diesel, 5, 1700, Kyiv, "Комфортний кросовер з підігрівом сидінь і керма."),
        Car(15, "Mitsubishi", "Outlander", 2022, Suv, Transmission.Automatic, FuelType.Petrol, 7, 2100, Odesa, "Семимісний кросовер для великої компанії."),
        Car(16, "Volvo", "XC90", 2024, Suv, Transmission.Automatic, FuelType.Diesel, 7, 3200, Kyiv, "Преміальний позашляховик на сім місць."),
        Car(17, "Kia", "Ceed", 2023, Hatchback, Transmission.Automatic, FuelType.Petrol, 5, 950, Kyiv2, "Компактний хетчбек для міських поїздок."),
        Car(18, "Renault", "Clio", 2022, Hatchback, Transmission.Manual, FuelType.Petrol, 5, 750, Odesa, "Маленький і економний хетчбек."),
        Car(19, "Skoda", "Fabia", 2021, Hatchback, Transmission.Manual, FuelType.Petrol, 5, 700, Chernivtsi, "Найдоступніший варіант для міста."),
        Car(20, "Volkswagen", "ID.3", 2024, Hatchback, Transmission.Automatic, FuelType.Electric, 5, 1300, Lviv, "Електромобіль із запасом ходу близько 400 км."),
        Car(21, "Peugeot", "208", 2023, Hatchback, Transmission.Automatic, FuelType.Petrol, 5, 850, Dnipro, "Стильний хетчбек з автоматичною коробкою."),
        Car(22, "Volkswagen", "Touran", 2022, Minivan, Transmission.Manual, FuelType.Diesel, 7, 1600, Lviv, "Сімейний мінівен на сім місць."),
        Car(23, "Ford", "Galaxy", 2023, Minivan, Transmission.Automatic, FuelType.Diesel, 7, 1900, Kharkiv, "Просторий мінівен з автоматом для подорожей."),
        Car(24, "Mercedes-Benz", "V-Class", 2024, Minivan, Transmission.Automatic, FuelType.Diesel, 8, 3900, Kyiv, "Преміальний мінівен на вісім місць."),
        Car(25, "Citroen", "Berlingo", 2022, Minivan, Transmission.Manual, FuelType.Diesel, 7, 1200, Chernivtsi, "Місткий і недорогий мінівен."),
        Car(26, "Ford", "Mustang", 2023, Coupe, Transmission.Automatic, FuelType.Petrol, 4, 4500, Kyiv, "Легендарне спортивне купе з двигуном V8."),
        Car(27, "Audi", "A5 Coupe", 2023, Coupe, Transmission.Automatic, FuelType.Petrol, 4, 3800, Lviv, "Елегантне купе з повним приводом."),
        Car(28, "Tesla", "Model 3", 2024, Sedan, Transmission.Automatic, FuelType.Electric, 5, 2300, Kyiv2, "Електричний седан з автопілотом."),
        Car(29, "Nissan", "Qashqai", 2022, Suv, Transmission.Manual, FuelType.Petrol, 5, 1350, Dnipro, "Популярний міський кросовер."),
        Car(30, "Hyundai", "Tucson", 2024, Suv, Transmission.Automatic, FuelType.Petrol, 5, 1600, Kharkiv, "Сучасний кросовер з панорамним дахом."),
        Car(31, "Mazda", "6", 2023, Sedan, Transmission.Automatic, FuelType.Petrol, 5, 1350, Odesa, "Седан з якісним салоном і чітким керуванням."),
        Car(32, "Skoda", "Superb", 2024, Sedan, Transmission.Automatic, FuelType.Diesel, 5, 1600, Kharkiv, "Великий седан з величезним простором для пасажирів."),
        Car(33, "Honda", "Civic", 2022, Sedan, Transmission.Manual, FuelType.Petrol, 5, 1050, Chernivtsi, "Надійний седан з механічною коробкою."),
        Car(34, "BYD", "Seal", 2025, Sedan, Transmission.Automatic, FuelType.Electric, 5, 2100, Odesa, "Електричний седан із запасом ходу понад 500 км."),
        Car(35, "Toyota", "Land Cruiser Prado", 2023, Suv, Transmission.Automatic, FuelType.Diesel, 7, 3600, Kyiv, "Рамний позашляховик для будь-яких доріг."),
        Car(36, "Hyundai", "Kona Electric", 2024, Suv, Transmission.Automatic, FuelType.Electric, 5, 1750, Kyiv2, "Компактний електричний кросовер."),
        Car(37, "Suzuki", "Vitara", 2022, Suv, Transmission.Manual, FuelType.Petrol, 5, 1100, Lviv, "Легкий кросовер з повним приводом."),
        Car(38, "Ford", "Kuga", 2023, Suv, Transmission.Automatic, FuelType.Hybrid, 5, 1850, Dnipro, "Гібридний кросовер з великим багажником."),
        Car(39, "Toyota", "Yaris", 2024, Hatchback, Transmission.Automatic, FuelType.Hybrid, 5, 900, Kyiv, "Гібридний хетчбек з мінімальною витратою пального."),
        Car(40, "Opel", "Corsa", 2021, Hatchback, Transmission.Manual, FuelType.Petrol, 5, 720, Kharkiv, "Економний хетчбек. Наразі на технічному обслуговуванні.", CarStatus.UnderMaintenance),
        Car(41, "Volkswagen", "Polo", 2023, Hatchback, Transmission.Automatic, FuelType.Petrol, 5, 820, Odesa, "Зручний хетчбек з автоматом для міста."),
        Car(42, "Renault", "Zoe", 2022, Hatchback, Transmission.Automatic, FuelType.Electric, 5, 990, Dnipro, "Доступний міський електромобіль."),
        Car(43, "Mini", "Cooper", 2023, Hatchback, Transmission.Automatic, FuelType.Petrol, 4, 1450, Lviv, "Стильний компактний хетчбек."),
        Car(44, "Kia", "Carnival", 2024, Minivan, Transmission.Automatic, FuelType.Diesel, 8, 2800, Odesa, "Великий мінівен на вісім місць з електродверима."),
        Car(45, "Renault", "Grand Scenic", 2021, Minivan, Transmission.Manual, FuelType.Diesel, 7, 1350, Dnipro, "Недорогий семимісний мінівен."),
        Car(46, "Toyota", "Proace Verso", 2023, Minivan, Transmission.Manual, FuelType.Diesel, 8, 2400, Kharkiv, "Мікроавтобус для групових поїздок."),
        Car(47, "Chevrolet", "Camaro", 2022, Coupe, Transmission.Automatic, FuelType.Petrol, 4, 4200, Odesa, "Американське спортивне купе."),
        Car(48, "Mercedes-Benz", "C-Class Coupe", 2024, Coupe, Transmission.Automatic, FuelType.Petrol, 4, 3900, Kyiv2, "Комфортне купе преміум-класу."),
        Car(49, "BMW", "2 Series Coupe", 2023, Coupe, Transmission.Manual, FuelType.Petrol, 4, 3000, Kharkiv, "Задньопривідне купе з механічною коробкою."),
        Car(50, "Porsche", "718 Cayman", 2023, Coupe, Transmission.Automatic, FuelType.Petrol, 2, 6000, Kyiv, "Двомісне спортивне купе для особливих випадків.")
    ];

    /// <summary>
    /// Adds the demo cars that are missing (by licence plate).
    /// </summary>
    /// <param name="keepIds">Insert with the fixed ids (evaluation database); otherwise ids are generated.</param>
    /// <returns>Number of cars added; 0 when categories 1–5 are not present.</returns>
    public static async Task<int> EnsureAsync(ApplicationDbContext db, bool keepIds, CancellationToken ct = default)
    {
        var categories = await db.Categories.Select(c => c.Id).ToListAsync(ct);
        if (!new[] { Sedan, Suv, Hatchback, Minivan, Coupe }.All(categories.Contains))
            return 0;

        var plates = (await db.Cars.Select(c => c.LicensePlate).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = Create().Where(c => !plates.Contains(c.LicensePlate)).ToList();
        if (missing.Count == 0) return 0;

        if (!keepIds)
            foreach (var car in missing) car.Id = 0;

        db.Cars.AddRange(missing);
        await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    private static Car Car(
        int id, string brand, string model, int year, int category, Transmission transmission, FuelType fuel,
        int seats, decimal price, string location, string description, CarStatus status = CarStatus.Available) => new()
    {
        Id = id,
        Brand = brand,
        Model = model,
        Year = year,
        LicensePlate = Plate(id, location),
        CategoryId = category,
        Transmission = transmission,
        FuelType = fuel,
        Seats = seats,
        PricePerDay = price,
        Location = location,
        Description = description,
        Status = status
    };

    /// <summary>Region prefix by city + fleet number, e.g. "BC0020KA" for car 20 in Lviv.</summary>
    private static string Plate(int id, string location)
    {
        var prefix = location.Split(',')[0] switch
        {
            "Львів" => "BC",
            "Одеса" => "BH",
            "Харків" => "AX",
            "Дніпро" => "AE",
            "Чернівці" => "CE",
            _ => "AA"
        };
        return $"{prefix}{id:0000}KA";
    }
}
