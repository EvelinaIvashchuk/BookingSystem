using BookingSystem.Ai.Slots;
using BookingSystem.Ai.Tools;
using BookingSystem.Data;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using static BookingSystem.Tests.Ai.AiTestData;

namespace BookingSystem.Tests.Ai;

public class SlotValidatorTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _db = TestDbContextFactory.Create();

    public Task InitializeAsync() => SeedAsync(_db);

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private Task<SlotValidationResult> Validate(
        string tool, Dictionary<string, object?> args, string? message = null, IEnumerable<string>? provided = null) =>
        new SlotValidator(CreateUnitOfWork(_db)).ValidateAsync(
            tool, args,
            new SlotValidationContext(Today, message, (provided ?? args.Keys).ToHashSet()));

    private static Dictionary<string, object?> Dates(string pickup = Pickup, string ret = Return) =>
        new() { ["pickupDate"] = pickup, ["returnDate"] = ret };

    // ── Required slots ───────────────────────────────────────────────────────

    [Fact]
    public async Task Search_WithoutDates_ReportsBothMissing()
    {
        var result = await Validate(BookingTools.SearchAvailableCarsName, new() { ["transmission"] = "Automatic" });

        result.Missing.Should().Equal(SlotNames.PickupDate, SlotNames.ReturnDate);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Draft_WithoutCar_ReportsSelectedCarMissing()
    {
        var result = await Validate(BookingTools.CreateRentalDraftName, Dates());

        result.Missing.Should().Equal(SlotNames.SelectedCarId);
    }

    [Fact]
    public async Task ToolWithoutSlots_IsAlwaysValid()
    {
        var result = await Validate(BookingTools.GetMyRentalsName, new());

        result.IsValid.Should().BeTrue();
    }

    // ── Normalization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_NormalizesSynonymsNumbersAndDates()
    {
        var args = new Dictionary<string, object?>
        {
            ["pickupDate"] = "2026-12-20T00:00:00",
            ["returnDate"] = Return,
            ["transmission"] = "автомат",
            ["fuelType"] = "дизель",
            ["category"] = "minivan",
            ["location"] = "Києві",
            ["minSeats"] = "5",
            ["maxPricePerDay"] = "1500,50"
        };

        var result = await Validate(BookingTools.SearchAvailableCarsName, args);

        result.IsValid.Should().BeTrue();
        result.NormalizedArguments.Should().Contain(new Dictionary<string, object?>
        {
            ["pickupDate"] = "2026-12-20",
            ["transmission"] = "Automatic",
            ["fuelType"] = "Diesel",
            ["category"] = "Мінівен",
            ["location"] = "Київ",
            ["minSeats"] = 5,
            ["maxPricePerDay"] = 1500.50m
        });
    }

    [Fact]
    public async Task Search_AnyFilterIsDropped()
    {
        var args = Dates();
        args["transmission"] = "any";
        args["category"] = "будь-яка";

        var result = await Validate(BookingTools.SearchAvailableCarsName, args);

        result.IsValid.Should().BeTrue();
        result.NormalizedArguments.Should().NotContainKeys("transmission", "category");
    }

    // ── Invalid values ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-11-10", "2026-11-15", SlotNames.PickupDate)]    // past
    [InlineData("2027-02-01", "2027-02-03", SlotNames.PickupDate)]    // > 60 days ahead
    [InlineData(Return, Pickup, SlotNames.ReturnDate)]                // return before pickup
    [InlineData(Pickup, Pickup, SlotNames.ReturnDate)]                // zero days
    [InlineData("2026-12-01", "2027-01-15", SlotNames.ReturnDate)]    // > 30 days
    [InlineData("20.12.2026", Return, SlotNames.PickupDate)]          // format
    [InlineData("наступна пʼятниця", Return, SlotNames.PickupDate)]
    public async Task Search_InvalidDates_AreAttributedToTheRightSlot(string pickup, string ret, string slot)
    {
        var result = await Validate(BookingTools.SearchAvailableCarsName, Dates(pickup, ret));

        result.Invalid.Should().ContainSingle().Which.Should().StartWith(slot + ":");
    }

    [Theory]
    [InlineData("minSeats", 0, SlotNames.MinSeats)]
    [InlineData("minSeats", 12, SlotNames.MinSeats)]
    [InlineData("minSeats", "five", SlotNames.MinSeats)]
    [InlineData("maxPricePerDay", -100, SlotNames.MaxPricePerDay)]
    [InlineData("transmission", "напівавтомат", SlotNames.Transmission)]
    [InlineData("fuelType", "газ", SlotNames.FuelType)]
    [InlineData("category", "лімузин", SlotNames.Category)]
    public async Task Search_InvalidFilters_AreReported(string argument, object value, string slot)
    {
        var args = Dates();
        args[argument] = value;

        var result = await Validate(BookingTools.SearchAvailableCarsName, args);

        result.Invalid.Should().ContainSingle().Which.Should().StartWith(slot + ":");
    }

    [Fact]
    public async Task UnknownCar_IsInvalid()
    {
        var args = Dates();
        args["carId"] = 999;

        var result = await Validate(BookingTools.CalculatePriceName, args);

        result.Invalid.Should().ContainSingle().Which.Should().StartWith(SlotNames.SelectedCarId);
    }

    [Fact]
    public async Task CarUnderMaintenance_CannotBeBookedButCanBePriced()
    {
        var args = Dates();
        args["carId"] = LeafId;

        (await Validate(BookingTools.CreateRentalDraftName, args)).IsValid.Should().BeFalse();
        (await Validate(BookingTools.CalculatePriceName, args)).IsValid.Should().BeTrue();
    }

    // ── Consistency with the user's words ────────────────────────────────────

    [Fact]
    public async Task WeekdayMismatch_IsInvalid()
    {
        // Today is Friday 2026-11-20; Monday is 2026-11-23, the model produced Wednesday 2026-11-25.
        var result = await Validate(BookingTools.SearchAvailableCarsName,
            Dates("2026-11-25", "2026-11-28"), message: "Авто з понеділка на 3 дні");

        result.Invalid.Should().ContainSingle()
            .Which.Should().StartWith(SlotNames.PickupDate).And.Contain("Wednesday").And.Contain("Monday");
    }

    [Fact]
    public async Task WeekdayOfReturnDate_AlsoSatisfiesTheCheck()
    {
        var result = await Validate(BookingTools.SearchAvailableCarsName,
            Dates("2026-11-21", "2026-11-23"), message: "з завтра до понеділка");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task DurationMismatch_IsInvalidAndSuggestsReturnDate()
    {
        var result = await Validate(BookingTools.SearchAvailableCarsName,
            Dates("2026-11-23", "2026-11-25"), message: "з понеділка на 3 дні");

        result.Invalid.Should().ContainSingle()
            .Which.Should().StartWith(SlotNames.ReturnDate).And.Contain("2026-11-26");
    }

    [Fact]
    public async Task ConsistentDates_AreValid()
    {
        var result = await Validate(BookingTools.SearchAvailableCarsName,
            Dates("2026-11-23", "2026-11-26"), message: "з понеділка на 3 дні");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ConsistencyChecks_IgnoreDatesMergedFromEarlierTurns()
    {
        // The user now talks about Sunday opening hours, the dates come from slots, not from the model.
        var result = await Validate(BookingTools.SearchAvailableCarsName,
            Dates("2026-11-25", "2026-11-28"), message: "А в неділю ви працюєте?", provided: []);

        result.IsValid.Should().BeTrue();
    }

    // ── Grounding (Ai:SlotFilling:RequireGroundedDates) ─────────────────────

    private Task<SlotValidationResult> ValidateGrounded(
        Dictionary<string, object?> args, string message, IEnumerable<string>? provided = null, BookingSlots? known = null) =>
        new SlotValidator(CreateUnitOfWork(_db)).ValidateAsync(
            BookingTools.SearchAvailableCarsName, args,
            new SlotValidationContext(Today, message, (provided ?? args.Keys).ToHashSet())
            {
                RequireGroundedDates = true,
                KnownSlots = known
            });

    [Fact]
    public async Task Grounding_InventedReturnDate_IsReportedMissing()
    {
        var result = await ValidateGrounded(Dates("2026-12-20", "2026-12-21"), "Потрібен автомат з 20 грудня");

        result.Missing.Should().Equal(SlotNames.ReturnDate);
        result.Invalid.Should().BeEmpty();
    }

    [Fact]
    public async Task Grounding_StatedDates_AreValid()
    {
        var result = await ValidateGrounded(Dates("2026-12-20", "2026-12-25"), "з 20 по 25 грудня");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Grounding_SkipsDatesMergedFromSlots_AndAcceptsKnownValues()
    {
        var known = new BookingSlots { PickupDate = new DateOnly(2026, 12, 20), ReturnDate = new DateOnly(2026, 12, 25) };

        (await ValidateGrounded(Dates(), "А дизельний є?", provided: [])).IsValid.Should().BeTrue();
        (await ValidateGrounded(Dates(), "А дизельний є?", known: known)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Grounding_IsOffByDefault()
    {
        var result = await Validate(BookingTools.SearchAvailableCarsName, Dates("2026-12-20", "2026-12-21"),
            message: "Потрібен автомат з 20 грудня");

        result.IsValid.Should().BeTrue();
    }
}
