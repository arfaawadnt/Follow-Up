using FollowUp.Domain.Reference;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="RefItem"/> (write side; ADR-0005).</summary>
public interface IRefItemRepository
{
    Task<RefItem?> GetByIdAsync(RefItemId id, CancellationToken ct);
    Task<bool> ExistsAsync(RefType type, string code, CancellationToken ct);
    void Add(RefItem item);
    void Remove(RefItem item);
}

/// <summary>Aggregate repository for <see cref="City"/>.</summary>
public interface ICityRepository
{
    Task<City?> GetByIdAsync(CityId id, CancellationToken ct);
    void Add(City city);
    void Remove(City city);
}

/// <summary>Aggregate repository for <see cref="Area"/>.</summary>
public interface IAreaRepository
{
    Task<Area?> GetByIdAsync(AreaId id, CancellationToken ct);
    void Add(Area area);
    void Remove(Area area);
}

/// <summary>Aggregate repository for <see cref="AppSetting"/> (key/value settings).</summary>
public interface IAppSettingRepository
{
    Task<AppSetting?> GetAsync(string key, CancellationToken ct);
    void Add(AppSetting setting);
}
