using BookingSystem.Ai;
using BookingSystem.Ai.Tools;
using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Moq;
using static BookingSystem.Tests.Ai.AiTestData;

namespace BookingSystem.Tests.Ai;

public class BookingToolsTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _db = TestDbContextFactory.Create();

    public Task InitializeAsync() => SeedAsync(_db);

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private readonly AiOptions _options = new() { PendingActionTtlMinutes = 15 };
    private readonly Mock<IKnowledgeSearch> _knowledge = new();

    private (BookingTools Tools, AgentTurnContext Turn) CreateSut(string? userId = null, Guid? sessionId = null)
    {
        var turn = new AgentTurnContext(sessionId ?? Guid.NewGuid(), userId, Today);
        var tools = new BookingTools(CreateUnitOfWork(_db), CreateCarService(_db), _knowledge.Object, CreateClock(), _options, turn);
        return (tools, turn);
    }

    // ── search_available_cars ────────────────────────────────────────────────

    [Fact]
    public async Task Search_ReturnsFiveCheapestFreeCars_ExcludingBookedAndMaintenance()
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return);

        result.Days.Should().Be(5);
        result.TotalFound.Should().Be(7);   // 9 cars − Leaf (maintenance) − Octavia (booked)
        result.Cars.Select(c => c.Id).Should().Equal(GolfId, CeedId, DusterId, CamryId, CrvId);
        result.Cars[0].TotalPrice.Should().Be(900 * 5);
    }

    [Fact]
    public async Task Search_CancelledAndBackToBackRentalsDoNotBlockCar()
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return, location: "Київ");

        result.Cars.Select(c => c.Id).Should().Contain([CeedId, CamryId]).And.NotContain(OctaviaId);
    }

    [Fact]
    public async Task Search_MapsUkrainianSynonymsAndInflectedCity()
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(
            Pickup, Return, location: "Києві", transmission: "автомат", minSeats: 5);

        result.Cars.Select(c => c.Id).Should().Equal(CeedId, CamryId, CrvId, SiennaId);
    }

    [Theory]
    [InlineData("minivan", null, null, new[] { SiennaId })]
    [InlineData("Мінівен", null, null, new[] { SiennaId })]
    [InlineData(null, "дизель", 1000, new[] { GolfId, DusterId })]
    [InlineData("crossover", "Diesel", null, new[] { DusterId })]
    public async Task Search_AppliesCategoryFuelAndPriceFilters(
        string? category, string? fuel, int? maxPrice, int[] expectedIds)
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(
            Pickup, Return, category: category, fuelType: fuel, maxPricePerDay: maxPrice);

        result.Cars.Select(c => c.Id).Should().Equal(expectedIds);
    }

    [Fact]
    public async Task Search_FillsCarCardsForTheWidget()
    {
        var (tools, turn) = CreateSut();

        await tools.SearchAvailableCarsAsync(Pickup, Return, category: "SUV");

        turn.Cars.Should().HaveCount(2);
        turn.Cars[0].Id.Should().Be(DusterId);
        turn.Cars[0].TotalPrice.Should().Be(5000);
    }

    [Fact]
    public async Task Search_NothingFound_ReturnsNoteInsteadOfCars()
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return, minSeats: 9);

        result.TotalFound.Should().Be(0);
        result.Note.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("20.12.2026", Return, ToolErrorCodes.InvalidArguments)]
    [InlineData("next friday", Return, ToolErrorCodes.InvalidArguments)]
    [InlineData("2026-11-10", "2026-11-15", ToolErrorCodes.BusinessRule)]   // in the past
    [InlineData(Return, Pickup, ToolErrorCodes.BusinessRule)]               // return before pickup
    [InlineData("2026-12-01", "2027-01-15", ToolErrorCodes.BusinessRule)]   // longer than 30 days
    [InlineData("2027-02-01", "2027-02-05", ToolErrorCodes.BusinessRule)]   // more than 60 days ahead
    public async Task Search_InvalidDates_ReturnStructuredError(string pickup, string ret, string expectedCode)
    {
        var (tools, turn) = CreateSut();

        var result = await tools.SearchAvailableCarsAsync(pickup, ret);

        result.Should().BeOfType<ToolError>().Which.Error.Should().Be(expectedCode);
        turn.Cars.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_UnknownEnumValues_ListAllowedValues()
    {
        var (tools, _) = CreateSut();

        var result = await tools.SearchAvailableCarsAsync(Pickup, Return, transmission: "напівавтомат", category: "лімузин");

        var error = result.Should().BeOfType<ToolError>().Subject;
        error.Error.Should().Be(ToolErrorCodes.InvalidArguments);
        error.Invalid.Should().HaveCount(2);
        error.Invalid!.Should().Contain(s => s.StartsWith("transmission") && s.Contains("Automatic"));
    }

    [Fact]
    public async Task Search_AcceptsIsoDateTimes()
    {
        var (tools, _) = CreateSut();

        var result = await tools.SearchAvailableCarsAsync("2026-12-20T00:00:00", "2026-12-25T00:00:00+02:00");

        result.Should().BeOfType<CarSearchResult>().Which.Days.Should().Be(5);
    }

    // ── search by car name (E1 failure case 2) ───────────────────────────────

    [Theory]
    [InlineData("BMW 4 Series", BmwId)]          // the most expensive car: never among the 5 cheapest without the filter
    [InlineData("bmw", BmwId)]
    [InlineData("Toyota Sienna", SiennaId)]
    [InlineData("Тойота Сієнна", SiennaId)]
    [InlineData("Honda CR-V (2023)", CrvId)]         // text of the Choose button on a car card
    public async Task Search_ByCarName_FindsCarOutsideTheCheapestFive(string carName, int expectedId)
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return, carName: carName);

        result.Cars.Select(c => c.Id).Should().Equal(expectedId);
    }

    [Fact]
    public async Task Search_ByBrand_ReturnsAllModelsOfTheBrand()
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return, carName: "Toyota");

        result.Cars.Select(c => c.Id).Should().Equal(CamryId, SiennaId);
    }

    [Fact]
    public async Task Search_ByNameOfBookedCar_SaysItIsInTheFleetButBusy()
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return, carName: "Skoda Octavia");

        result.Cars.Should().BeEmpty();
        result.Note.Should().Contain("Skoda Octavia").And.Contain("not free");
    }

    [Fact]
    public async Task Search_ByUnknownName_SaysThereIsNoSuchCar()
    {
        var (tools, _) = CreateSut();

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return, carName: "Lada Niva");

        result.Cars.Should().BeEmpty();
        result.Note.Should().Contain("no car matching");
    }

    // ── car name consistency (E1 failure case 3) ─────────────────────────────

    [Fact]
    public async Task CreateDraft_NameDoesNotMatchCarId_IsRejectedWithoutDraft()
    {
        // gemini-3.1-flash-lite, B-04: "Ви обрали Citroen Berlingo", but carId = 1 (Toyota Camry).
        var (tools, turn) = CreateSut(userId: "user-1");

        var result = await tools.CreateRentalDraftAsync(CamryId, Pickup, Return, carName: "Toyota Sienna");

        result.Should().BeOfType<ToolError>().Which.Error.Should().Be(ToolErrorCodes.CarMismatch);
        ((ToolError)result).Message.Should().Contain("Toyota Camry");
        turn.Draft.Should().BeNull();
        (await _db.PendingActions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Toyota Camry")]
    [InlineData("Toyota Camry (2023)")]
    [InlineData("Toyota Camry (2024)")]                // a wrong year is ignored
    [InlineData("Камрі")]
    [InlineData(null)]
    public async Task CreateDraft_MatchingOrMissingName_IsAccepted(string? carName)
    {
        var (tools, _) = CreateSut(userId: "user-1");

        var result = await tools.CreateRentalDraftAsync(CamryId, "2026-12-21", Return, carName: carName);

        result.Should().BeOfType<RentalDraftCreated>();
    }

    [Fact]
    public async Task CalculatePrice_NameDoesNotMatchCarId_IsRejected()
    {
        var (tools, _) = CreateSut();

        var result = await tools.CalculatePriceAsync(SiennaId, Pickup, Return, carName: "BMW 4 Series");

        result.Should().BeOfType<ToolError>().Which.Error.Should().Be(ToolErrorCodes.CarMismatch);
    }

    // ── calculate_price / get_car_details ────────────────────────────────────

    [Fact]
    public async Task CalculatePrice_UsesRentalRules()
    {
        var (tools, _) = CreateSut();

        var quote = (PriceQuote)await tools.CalculatePriceAsync(CamryId, Pickup, Return);

        quote.Days.Should().Be(5);
        quote.TotalPrice.Should().Be(6000);
        quote.IsAvailable.Should().BeTrue();
    }

    [Theory]
    [InlineData(OctaviaId)]   // booked
    [InlineData(LeafId)]      // under maintenance
    public async Task CalculatePrice_ReportsUnavailableCar(int carId)
    {
        var (tools, _) = CreateSut();

        var quote = (PriceQuote)await tools.CalculatePriceAsync(carId, Pickup, Return);

        quote.IsAvailable.Should().BeFalse();
        quote.UnavailableReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetCarDetails_UnknownCar_ReturnsNotFound()
    {
        var (tools, _) = CreateSut();

        var result = await tools.GetCarDetailsAsync(999);

        result.Should().BeOfType<ToolError>().Which.Error.Should().Be(ToolErrorCodes.NotFound);
    }

    // ── get_my_rentals ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyRentals_Anonymous_RequiresAuthentication()
    {
        var (tools, _) = CreateSut(userId: null);

        var result = await tools.GetMyRentalsAsync();

        result.Should().BeOfType<ToolError>().Which.Error.Should().Be(ToolErrorCodes.AuthenticationRequired);
    }

    [Fact]
    public async Task GetMyRentals_ReturnsOnlyCurrentUsersRentals()
    {
        var (tools, _) = CreateSut(userId: "user-1");

        var result = (UserRentalsResult)await tools.GetMyRentalsAsync();

        result.Total.Should().Be(2);
        result.Rentals.Select(r => r.Id).Should().BeEquivalentTo([102, 103]);
    }

    // ── create_rental_draft ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraft_StoresPendingActionAndCreatesNoRental()
    {
        var (tools, turn) = CreateSut(userId: "user-1");
        var rentalsBefore = await _db.Rentals.CountAsync();

        var result = await tools.CreateRentalDraftAsync(CrvId, Pickup, Return, notes: "  Дитяче крісло  ");

        var created = result.Should().BeOfType<RentalDraftCreated>().Subject;
        created.TotalPrice.Should().Be(1800 * 5);

        var action = await _db.PendingActions.SingleAsync();
        action.Id.Should().Be(created.PendingActionId);
        action.UserId.Should().Be("user-1");
        action.SessionId.Should().Be(turn.SessionId);
        action.Status.Should().Be(PendingActionStatus.Pending);
        action.Type.Should().Be(PendingActionType.CreateRental);
        action.ExpiresAt.Should().Be(UtcNow.UtcDateTime.AddMinutes(15));

        turn.Draft.Should().NotBeNull();
        turn.Draft!.PendingActionId.Should().Be(created.PendingActionId);
        turn.Draft.Notes.Should().Be("Дитяче крісло");

        (await _db.Rentals.CountAsync()).Should().Be(rentalsBefore, "a draft must never create a rental");
    }

    [Fact]
    public async Task CreateDraft_Anonymous_RequiresAuthentication()
    {
        var (tools, turn) = CreateSut(userId: null);

        var result = await tools.CreateRentalDraftAsync(CrvId, Pickup, Return);

        result.Should().BeOfType<ToolError>().Which.Error.Should().Be(ToolErrorCodes.AuthenticationRequired);
        (await _db.PendingActions.CountAsync()).Should().Be(0);
        turn.Draft.Should().BeNull();
    }

    [Theory]
    [InlineData(OctaviaId)]   // booked for the dates
    [InlineData(LeafId)]      // under maintenance
    public async Task CreateDraft_UnavailableCar_IsRejected(int carId)
    {
        var (tools, _) = CreateSut(userId: "user-1");

        var result = await tools.CreateRentalDraftAsync(carId, Pickup, Return);

        result.Should().BeOfType<ToolError>().Which.Error.Should().Be(ToolErrorCodes.BusinessRule);
        (await _db.PendingActions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateDraft_UserWithMaxActiveRentals_IsRejected()
    {
        _db.Rentals.Add(new Rental
        {
            Id = 200, UserId = "user-1", CarId = BmwId, Status = RentalStatus.Pending,
            PickupDate = new DateTime(2026, 12, 1), ReturnDate = new DateTime(2026, 12, 3)
        });
        await _db.SaveChangesAsync();   // user-1 now has 3 active rentals
        var (tools, _) = CreateSut(userId: "user-1");

        var result = await tools.CreateRentalDraftAsync(CrvId, Pickup, Return);

        result.Should().BeOfType<ToolError>().Which.Message.Should().Contain("active rentals");
    }

    [Fact]
    public async Task CreateDraft_ReplacesPreviousDraftOfTheSession()
    {
        var sessionId = Guid.NewGuid();
        var (first, _) = CreateSut(userId: "user-1", sessionId: sessionId);
        var (second, _) = CreateSut(userId: "user-1", sessionId: sessionId);

        var a = (RentalDraftCreated)await first.CreateRentalDraftAsync(CrvId, Pickup, Return);
        var b = (RentalDraftCreated)await second.CreateRentalDraftAsync(SiennaId, Pickup, Return);

        (await _db.PendingActions.SingleAsync(x => x.Id == a.PendingActionId)).Status.Should().Be(PendingActionStatus.Cancelled);
        (await _db.PendingActions.SingleAsync(x => x.Id == b.PendingActionId)).Status.Should().Be(PendingActionStatus.Pending);
    }

    // ── AIFunction schema ────────────────────────────────────────────────────

    [Fact]
    public async Task AIFunction_AcceptsNumbersWrittenAsStrings()
    {
        var (tools, _) = CreateSut();
        var search = tools.AsAITools().OfType<AIFunction>().Single(f => f.Name == BookingTools.SearchAvailableCarsName);

        var minSeatsType = search.JsonSchema.GetProperty("properties").GetProperty("minSeats").GetProperty("type").ToString();
        var result = await search.InvokeAsync(new AIFunctionArguments
        {
            ["pickupDate"] = Pickup, ["returnDate"] = Return, ["minSeats"] = "7", ["maxPricePerDay"] = "2500"
        });

        minSeatsType.Should().Contain("integer").And.Contain("string");
        result!.ToString().Should().Contain("Sienna").And.NotContain("Camry");
    }

    [Fact]
    public void AIFunction_NamesAndDescriptionsAreExposedToTheModel()
    {
        var (tools, _) = CreateSut();

        var functions = tools.AsAITools().OfType<AIFunction>().ToList();

        functions.Select(f => f.Name).Should().BeEquivalentTo(
            BookingTools.SearchAvailableCarsName, BookingTools.GetCarDetailsName, BookingTools.CalculatePriceName,
            BookingTools.GetMyRentalsName, BookingTools.CreateRentalDraftName, BookingTools.SearchRentalPoliciesName);
        functions.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Description));
        functions.Single(f => f.Name == BookingTools.SearchAvailableCarsName)
            .JsonSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("pickupDate", "returnDate");
    }

    // ── search_rental_policies ───────────────────────────────────────────────

    [Fact]
    public async Task SearchPolicies_ReturnsFragmentsWithSourcesUsingConfiguredTopK()
    {
        _options.Search.TopK = 3;
        _knowledge.Setup(k => k.SearchAsync("Який депозит?", 3, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeSearchResult(KnowledgeSearchMode.HybridAdaptive,
            [
                new KnowledgeHit("deposit#2", "deposit", "Розмір депозиту за категоріями", "Хетчбек — 5 000 ₴ ...", 0.03),
                new KnowledgeHit("deposit#1", "deposit", "Що таке депозит", "Депозит — це сума ...", 0.02)
            ]));
        var (tools, _) = CreateSut();

        var result = (PolicySearchResult)await tools.SearchRentalPoliciesAsync("  Який депозит?  ");

        result.Fragments.Select(f => f.Source).Should().Equal("deposit#2", "deposit#1");
        result.Note.Should().BeNull();
    }

    [Fact]
    public async Task SearchPolicies_NothingFound_TellsModelNotToInvent()
    {
        _knowledge.Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeSearchResult(KnowledgeSearchMode.HybridAdaptive, []));
        var (tools, _) = CreateSut();

        var result = (PolicySearchResult)await tools.SearchRentalPoliciesAsync("Чи можна на Марс?");

        result.Fragments.Should().BeEmpty();
        result.Note.Should().Contain("support");
    }
}
