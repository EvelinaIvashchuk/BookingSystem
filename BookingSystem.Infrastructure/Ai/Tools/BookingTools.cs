using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BookingSystem.Ai.Dtos;
using BookingSystem.Ai.Slots;
using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services;
using BookingSystem.Services.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai.Tools;

/// <summary>
/// Functions the LLM agent may call. Descriptions are in English (models pick tools more
/// reliably); the agent still answers in the user's language.
/// The user id is taken from <see cref="AgentTurnContext"/> (claims), never from model arguments.
/// All business logic is delegated to existing repositories and <see cref="RentalRules"/>.
/// </summary>
public sealed class BookingTools(
    IUnitOfWork uow,
    ICarService carService,
    IKnowledgeSearch knowledge,
    AgentClock clock,
    AiOptions options,
    AgentTurnContext turn)
{
    public const string SearchAvailableCarsName = "search_available_cars";
    public const string GetCarDetailsName = "get_car_details";
    public const string CalculatePriceName = "calculate_price";
    public const string GetMyRentalsName = "get_my_rentals";
    public const string CreateRentalDraftName = "create_rental_draft";
    public const string SearchRentalPoliciesName = "search_rental_policies";

    internal const int MaxSearchResults = 5;
    internal const int MaxRentalsListed = 10;
    internal const int MaxNotesLength = 500;
    private const string DateFormat = "yyyy-MM-dd";

    public IList<AITool> AsAITools() =>
    [
        Create(SearchAvailableCarsAsync, SearchAvailableCarsName),
        Create(GetCarDetailsAsync, GetCarDetailsName),
        Create(CalculatePriceAsync, CalculatePriceName),
        Create(GetMyRentalsAsync, GetMyRentalsName),
        Create(CreateRentalDraftAsync, CreateRentalDraftName),
        Create(SearchRentalPoliciesAsync, SearchRentalPoliciesName)
    ];

    /// <summary>Numbers written as strings ("5") are accepted when binding arguments.</summary>
    private static readonly JsonSerializerOptions ToolJsonOptions = new(AIJsonUtilities.DefaultOptions)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// Numeric parameters are declared as "number or string". Some providers (Groq) validate
    /// tool calls against the schema server-side and reject the whole response with
    /// <c>tool_use_failed</c> when a model writes <c>"5"</c> instead of <c>5</c>;
    /// our own validation parses both forms.
    /// </summary>
    private static readonly AIJsonSchemaCreateOptions LenientNumbersSchema = new()
    {
        TransformSchemaNode = (_, node) =>
        {
            if (node is JsonObject schema && schema["type"] is { } type)
            {
                var types = type is JsonArray array
                    ? array.Select(t => t!.GetValue<string>()).ToList()
                    : [type.GetValue<string>()];

                if (types.Any(t => t is "integer" or "number") && !types.Contains("string"))
                {
                    types.Add("string");
                    schema["type"] = new JsonArray([.. types.Select(t => (JsonNode)JsonValue.Create(t))]);
                }
            }
            return node;
        }
    };

    private static AIFunction Create(Delegate method, string name) =>
        AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
        {
            Name = name,
            SerializerOptions = ToolJsonOptions,
            JsonSchemaCreateOptions = LenientNumbersSchema
        });

    // ── search_available_cars ────────────────────────────────────────────────

    [Description(
        "Finds rental cars that are free for the WHOLE period from pickupDate to returnDate and match optional filters. " +
        "Returns up to 5 cheapest cars with price per day and total price. " +
        "Requires both dates; if the user has not given them, ask the user instead of guessing.")]
    public async Task<object> SearchAvailableCarsAsync(
        [Description("Pickup date in YYYY-MM-DD format.")] string pickupDate,
        [Description("Return date in YYYY-MM-DD format, after pickupDate.")] string returnDate,
        [Description("City name, e.g. Київ, Львів, Одеса. Omit if the user did not mention a city; \"any\" drops an earlier city.")] string? location = null,
        [Description("Car category: Седан (sedan), SUV (crossover), Хетчбек (hatchback), Мінівен (minivan), Купе (coupe); \"any\" drops it.")] string? category = null,
        [Description("Transmission: Automatic or Manual; \"any\" drops it.")] string? transmission = null,
        [Description("Fuel type: Petrol, Diesel, Electric or Hybrid; \"any\" drops it.")] string? fuelType = null,
        [Description("Minimum number of seats.")] int? minSeats = null,
        [Description("Maximum price per day in UAH.")] decimal? maxPricePerDay = null,
        CancellationToken cancellationToken = default)
    {
        if (ParseDates(pickupDate, returnDate) is not { } dates)
            return InvalidDates(pickupDate, returnDate);

        var (pickup, ret) = dates;
        if (RentalRules.ValidateDates(pickup, ret, TodayAsDateTime) is { IsSuccess: false } rule)
            return new ToolError(ToolErrorCodes.BusinessRule, rule.Error!);

        // "any" means "no preference" (the model uses it to drop a filter mentioned earlier).
        location = SlotValues.IsAny(location) ? null : location;
        category = SlotValues.IsAny(category) ? null : category;
        transmission = SlotValues.IsAny(transmission) ? null : transmission;
        fuelType = SlotValues.IsAny(fuelType) ? null : fuelType;

        var criteria = new CarSearchCriteria(pickup, ret) { MinSeats = minSeats, MaxPricePerDay = maxPricePerDay };
        var invalid = new List<string>();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryId = await ResolveCategoryIdAsync(category);
            if (categoryId is null) invalid.Add($"category: unknown value '{category}'. Allowed: {string.Join(", ", BookingVocabulary.CategoryValues)}");
            criteria = criteria with { CategoryId = categoryId };
        }

        if (!string.IsNullOrWhiteSpace(transmission))
        {
            if (BookingVocabulary.TryParseTransmission(transmission, out var t)) criteria = criteria with { Transmission = t };
            else invalid.Add($"transmission: unknown value '{transmission}'. Allowed: {string.Join(", ", BookingVocabulary.TransmissionValues)}");
        }

        if (!string.IsNullOrWhiteSpace(fuelType))
        {
            if (BookingVocabulary.TryParseFuelType(fuelType, out var f)) criteria = criteria with { FuelType = f };
            else invalid.Add($"fuelType: unknown value '{fuelType}'. Allowed: {string.Join(", ", BookingVocabulary.FuelTypeValues)}");
        }

        if (invalid.Count > 0)
            return new ToolError(ToolErrorCodes.InvalidArguments, "Some filters have unsupported values.") { Invalid = invalid };

        var cars = await uow.Cars.SearchAvailableAsync(criteria, cancellationToken);

        if (!string.IsNullOrWhiteSpace(location))
        {
            var city = BookingVocabulary.NormalizeCity(location);
            cars = [.. cars.Where(c => c.Location.Contains(city, StringComparison.OrdinalIgnoreCase))];
        }

        var days = RentalRules.CountDays(pickup, ret);
        var offers = cars.Take(MaxSearchResults)
            .Select(c => new CarOffer(
                c.Id, c.FullName, c.Category?.Name ?? string.Empty,
                c.Transmission.ToString(), c.FuelType.ToString(), c.Seats, c.Location,
                c.PricePerDay, RentalRules.CalculateTotalPrice(c.PricePerDay, pickup, ret)))
            .ToList();

        turn.Cars = cars.Take(MaxSearchResults)
            .Select(c => new CarCardDto(
                c.Id, c.FullName, c.PricePerDay,
                RentalRules.CalculateTotalPrice(c.PricePerDay, pickup, ret), c.ImageUrl))
            .ToList();

        return new CarSearchResult(
            FormatDate(pickup), FormatDate(ret), days, cars.Count, offers,
            cars.Count == 0
                ? "No cars match. Suggest other dates or relaxing filters; do not invent cars."
                : null);
    }

    // ── get_car_details ──────────────────────────────────────────────────────

    [Description("Returns full information about one car by its id (from search results): category, year, transmission, fuel, seats, location, description and whether it can be rented.")]
    public async Task<object> GetCarDetailsAsync(
        [Description("Car id from search results.")] int carId)
    {
        var car = await uow.Cars.GetWithCategoryAsync(carId);
        if (car is null)
            return new ToolError(ToolErrorCodes.NotFound, $"Car {carId} does not exist.");

        return new CarDetails(
            car.Id, car.FullName, car.Brand, car.Model, car.Year, car.Category?.Name ?? string.Empty,
            car.Transmission.ToString(), car.FuelType.ToString(), car.Seats, car.PricePerDay,
            car.Location, car.Description, car.Status.ToString(), car.Status == CarStatus.Available);
    }

    // ── calculate_price ──────────────────────────────────────────────────────

    [Description("Calculates the total rental price of a specific car for given dates and checks that it is free for them.")]
    public async Task<object> CalculatePriceAsync(
        [Description("Car id.")] int carId,
        [Description("Pickup date in YYYY-MM-DD format.")] string pickupDate,
        [Description("Return date in YYYY-MM-DD format, after pickupDate.")] string returnDate)
    {
        if (ParseDates(pickupDate, returnDate) is not { } dates)
            return InvalidDates(pickupDate, returnDate);

        var (pickup, ret) = dates;
        if (RentalRules.ValidateDates(pickup, ret, TodayAsDateTime) is { IsSuccess: false } rule)
            return new ToolError(ToolErrorCodes.BusinessRule, rule.Error!);

        var car = await uow.Cars.GetWithCategoryAsync(carId);
        if (car is null)
            return new ToolError(ToolErrorCodes.NotFound, $"Car {carId} does not exist.");

        string? unavailableReason = null;
        if (car.Status != CarStatus.Available)
            unavailableReason = $"The car is {car.Status} and cannot be rented now.";
        else if (await uow.Rentals.HasOverlapAsync(carId, pickup, ret))
            unavailableReason = "The car is already booked for some of these dates.";

        return new PriceQuote(
            car.Id, car.FullName, FormatDate(pickup), FormatDate(ret),
            RentalRules.CountDays(pickup, ret), car.PricePerDay,
            RentalRules.CalculateTotalPrice(car.PricePerDay, pickup, ret), "UAH",
            unavailableReason is null, unavailableReason);
    }

    // ── get_my_rentals ───────────────────────────────────────────────────────

    [Description("Lists the signed-in user's own bookings (most recent first). Works only for signed-in users.")]
    public async Task<object> GetMyRentalsAsync()
    {
        if (turn.UserId is null)
            return new ToolError(ToolErrorCodes.AuthenticationRequired,
                "The user is not signed in. Ask them to sign in to see their bookings.");

        var rentals = (await uow.Rentals.GetUserRentalsAsync(turn.UserId)).ToList();

        return new UserRentalsResult(
            rentals.Count,
            [.. rentals.Take(MaxRentalsListed).Select(r => new UserRentalInfo(
                r.Id, r.Car?.FullName ?? $"Car {r.CarId}",
                r.PickupDate is { } p ? FormatDate(p) : null,
                r.ReturnDate is { } d ? FormatDate(d) : null,
                r.Status.ToString(), r.TotalPrice))]);
    }

    // ── create_rental_draft ──────────────────────────────────────────────────

    [Description(
        "Prepares a booking of one car for the given dates WITHOUT creating it. The user then sees a card with " +
        "Confirm and Cancel buttons; only the user can confirm, you cannot. Call it only when the user clearly wants " +
        "to book a specific car (carId from search results) and both dates are known. Requires a signed-in user.")]
    public async Task<object> CreateRentalDraftAsync(
        [Description("Car id from search results.")] int carId,
        [Description("Pickup date in YYYY-MM-DD format.")] string pickupDate,
        [Description("Return date in YYYY-MM-DD format, after pickupDate.")] string returnDate,
        [Description("Optional notes from the user for the rental.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (turn.UserId is null)
            return new ToolError(ToolErrorCodes.AuthenticationRequired,
                "The user must sign in to book a car. Ask them to sign in and then repeat the request.");

        if (ParseDates(pickupDate, returnDate) is not { } dates)
            return InvalidDates(pickupDate, returnDate);

        var (pickup, ret) = dates;
        if (RentalRules.ValidateDates(pickup, ret, TodayAsDateTime) is { IsSuccess: false } rule)
            return new ToolError(ToolErrorCodes.BusinessRule, rule.Error!);

        var car = await uow.Cars.GetWithCategoryAsync(carId);
        if (car is null)
            return new ToolError(ToolErrorCodes.NotFound, $"Car {carId} does not exist.");

        if (car.Status != CarStatus.Available)
            return new ToolError(ToolErrorCodes.BusinessRule, $"{car.FullName} is {car.Status} and cannot be booked.");

        if (await uow.Rentals.HasOverlapAsync(carId, pickup, ret))
            return new ToolError(ToolErrorCodes.BusinessRule,
                $"{car.FullName} is already booked for some of these dates. Offer other dates or another car.");

        if (await uow.Rentals.GetActiveRentalCountAsync(turn.UserId) >= RentalRules.MaxActiveRentalsPerUser)
            return new ToolError(ToolErrorCodes.BusinessRule,
                $"The user already has {RentalRules.MaxActiveRentalsPerUser} active rentals and must cancel one first.");

        var now = clock.UtcNow;

        // A new draft replaces older unconfirmed drafts of the same conversation.
        foreach (var previous in await uow.PendingActions.GetPendingBySessionAsync(turn.SessionId, cancellationToken))
        {
            previous.Status = PendingActionStatus.Cancelled;
            previous.CompletedAt = now;
        }

        var draft = new RentalDraft
        {
            PendingActionId = Guid.NewGuid(),
            CarId = car.Id,
            CarName = car.FullName,
            ImageUrl = car.ImageUrl,
            PickupDate = FormatDate(pickup),
            ReturnDate = FormatDate(ret),
            Days = RentalRules.CountDays(pickup, ret),
            PricePerDay = car.PricePerDay,
            TotalPrice = RentalRules.CalculateTotalPrice(car.PricePerDay, pickup, ret),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()[..Math.Min(notes.Trim().Length, MaxNotesLength)],
            ExpiresAt = now.AddMinutes(options.PendingActionTtlMinutes)
        };

        await uow.PendingActions.AddAsync(new PendingAction
        {
            Id = draft.PendingActionId,
            SessionId = turn.SessionId,
            UserId = turn.UserId,
            Type = PendingActionType.CreateRental,
            PayloadJson = JsonSerializer.Serialize(draft, PendingActionService.PayloadJsonOptions),
            Status = PendingActionStatus.Pending,
            CreatedAt = now,
            ExpiresAt = draft.ExpiresAt
        }, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        turn.Draft = draft;

        return new RentalDraftCreated(
            draft.PendingActionId, draft.CarName, draft.PickupDate, draft.ReturnDate, draft.Days, draft.TotalPrice,
            options.PendingActionTtlMinutes,
            "The booking is NOT created yet. Tell the user, in their language, to check the card below and press its confirm button within the time limit. Do not name the button in English.");
    }

    // ── search_rental_policies ───────────────────────────────────────────────

    [Description(
        "Searches the rental rules knowledge base: documents and driver age, deposit and payment, insurance and excess, " +
        "cancellation and refunds, fuel, mileage limits, late or early return, damage and accidents, travel abroad, " +
        "extras (child seats, second driver), pickup and return, booking rules. Returns the most relevant fragments " +
        "with their source. Answer rule questions only from these fragments.")]
    public async Task<object> SearchRentalPoliciesAsync(
        [Description("The user's question about rental rules, in the user's language.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ToolError(ToolErrorCodes.InvalidArguments, "The query is empty.");

        var result = await knowledge.SearchAsync(query.Trim(), options.Search.TopK, ct: cancellationToken);

        return new PolicySearchResult(
            [.. result.Hits.Select(h => new PolicyFragment(h.Key, h.Title, h.Content))],
            result.Hits.Count == 0
                ? "Nothing relevant found. Say you do not have this information and suggest contacting support."
                : null);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private DateTime TodayAsDateTime => turn.Today.ToDateTime(TimeOnly.MinValue);

    private async Task<int?> ResolveCategoryIdAsync(string value)
    {
        var categories = await carService.GetAllCategoriesAsync();
        var name = BookingVocabulary.TryNormalizeCategory(value, out var canonical) ? canonical : value.Trim();
        return categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static (DateTime Pickup, DateTime Return)? ParseDates(string? pickupDate, string? returnDate) =>
        TryParseDate(pickupDate, out var pickup) && TryParseDate(returnDate, out var ret) ? (pickup, ret) : null;

    private static ToolError InvalidDates(string? pickupDate, string? returnDate)
    {
        var invalid = new List<string>();
        if (!TryParseDate(pickupDate, out _)) invalid.Add($"pickupDate: '{pickupDate}' is not a date in YYYY-MM-DD format");
        if (!TryParseDate(returnDate, out _)) invalid.Add($"returnDate: '{returnDate}' is not a date in YYYY-MM-DD format");
        return new ToolError(ToolErrorCodes.InvalidArguments, "Dates must be in YYYY-MM-DD format.") { Invalid = invalid };
    }

    /// <summary>Accepts YYYY-MM-DD and, leniently, ISO date-times (their calendar date is used).</summary>
    internal static bool TryParseDate(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (DateOnly.TryParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            date = d.ToDateTime(TimeOnly.MinValue);
            return true;
        }

        if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            date = dto.Date;
            return true;
        }

        return false;
    }

    private static string FormatDate(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}

/// <summary>Creates <see cref="BookingTools"/> bound to one agent turn.</summary>
public sealed class BookingToolsFactory(
    IUnitOfWork uow,
    ICarService carService,
    IKnowledgeSearch knowledge,
    AgentClock clock,
    IOptions<AiOptions> options)
{
    public BookingTools Create(AgentTurnContext turn) => new(uow, carService, knowledge, clock, options.Value, turn);
}
