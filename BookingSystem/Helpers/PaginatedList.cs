using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Helpers;

public class PaginatedList<T> : List<T>
{
    public int PageIndex  { get; }
    public int TotalPages { get; }
    public int TotalCount { get; }
    public int PageSize   { get; }

    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage     => PageIndex < TotalPages;

    public PaginatedList(IEnumerable<T> items, int count, int pageIndex, int pageSize)
    {
        PageIndex  = pageIndex;
        PageSize   = pageSize;
        TotalCount = count;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
        AddRange(items);
    }

    /// <summary>
    /// Creates a paginated list from an IQueryable (executes COUNT + paged query against DB).
    /// </summary>
    public static async Task<PaginatedList<T>> CreateAsync(
        IQueryable<T> source, int pageIndex, int pageSize)
    {
        var count = await source.CountAsync();
        var items = await source
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedList<T>(items, count, pageIndex, pageSize);
    }

    /// <summary>
    /// Creates a paginated list from an in-memory collection.
    /// </summary>
    public static PaginatedList<T> Create(
        IEnumerable<T> source, int pageIndex, int pageSize)
    {
        var list  = source.ToList();
        var count = list.Count;
        var items = list
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize);

        return new PaginatedList<T>(items, count, pageIndex, pageSize);
    }
}
