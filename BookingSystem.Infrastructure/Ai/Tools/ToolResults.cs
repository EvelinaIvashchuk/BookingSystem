namespace BookingSystem.Ai.Tools;

/// <summary>Error codes returned to the model in <see cref="ToolError.Error"/>.</summary>
public static class ToolErrorCodes
{
    /// <summary>Arguments could not be parsed (bad date format, unknown enum value...).</summary>
    public const string InvalidArguments = "invalid_arguments";

    /// <summary>Slot filling blocked the call: required parameters missing or invalid (tool not executed).</summary>
    public const string InvalidSlots = "invalid_slots";

    /// <summary>Arguments are well-formed but violate a rental rule (past date, too long...).</summary>
    public const string BusinessRule = "business_rule";

    public const string NotFound = "not_found";
    public const string AuthenticationRequired = "authentication_required";
    public const string InternalError = "internal_error";
}

/// <summary>Structured error the model receives instead of an exception.</summary>
public sealed record ToolError(string Error, string Message)
{
    public IReadOnlyList<string>? Missing { get; init; }
    public IReadOnlyList<string>? Invalid { get; init; }
}

public sealed record CarOffer(
    int Id,
    string Name,
    string Category,
    string Transmission,
    string FuelType,
    int Seats,
    string Location,
    decimal PricePerDay,
    decimal TotalPrice);

public sealed record CarSearchResult(
    string PickupDate,
    string ReturnDate,
    int Days,
    int TotalFound,
    IReadOnlyList<CarOffer> Cars,
    string? Note = null);

public sealed record CarDetails(
    int Id,
    string Name,
    string Brand,
    string Model,
    int Year,
    string Category,
    string Transmission,
    string FuelType,
    int Seats,
    decimal PricePerDay,
    string Location,
    string? Description,
    string Status,
    bool IsRentable);

public sealed record PriceQuote(
    int CarId,
    string CarName,
    string PickupDate,
    string ReturnDate,
    int Days,
    decimal PricePerDay,
    decimal TotalPrice,
    string Currency,
    bool IsAvailable,
    string? UnavailableReason = null);

public sealed record UserRentalInfo(
    int Id,
    string Car,
    string? PickupDate,
    string? ReturnDate,
    string Status,
    decimal TotalPrice);

public sealed record UserRentalsResult(int Total, IReadOnlyList<UserRentalInfo> Rentals);

public sealed record RentalDraftCreated(
    Guid PendingActionId,
    string CarName,
    string PickupDate,
    string ReturnDate,
    int Days,
    decimal TotalPrice,
    int ExpiresInMinutes,
    string Instruction);
