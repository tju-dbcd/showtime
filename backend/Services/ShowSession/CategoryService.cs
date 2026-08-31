using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.Show;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Services.ShowSession;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _context;

    public CategoryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<CategoryResponse>> GetEnabledCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<Category>()
            .AsNoTracking()
            .Where(c => c.Status == 1)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CategoryId)
            .Select(c => new CategoryResponse(
                c.CategoryId,
                c.CategoryName,
                c.ParentId,
                c.SortOrder))
            .ToListAsync(cancellationToken);
    }
}
