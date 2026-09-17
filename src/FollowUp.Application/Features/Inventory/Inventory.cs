using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Inventory;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Inventory;

// =====================================================================================================================
// Inventory feature slice: master data (manufacturers, distributors, stores, items + test links), purchase orders and
// their goods receipts, stock lots / movements, store-to-store transfers, alerts and the utilization report. Reads need
// ViewInventory; writes need ManageInventory (which implies View). Stores carry a Branch and every stock row is scoped
// on it: a caller sees (and may move) stock of a store when their scope is wildcard on Branches or covers the store's.
// =====================================================================================================================

// ---- Read side: DTOs ----

public sealed record ManufacturerDto(Guid Id, string Name, string? Country, string? Notes, bool IsActive, int ItemCount);
public sealed record SupplierDto(Guid Id, string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? Notes, bool IsActive, int OpenOrders);
public sealed record StoreDto(Guid Id, string Name, string Branch, string? Location, bool IsActive, int LotCount, decimal StockValue);

public sealed record ItemTestLinkDto(string TestCode, int TestType, string TestName, decimal QuantityPerTest);
public sealed record ItemTestLinkInput(string TestCode, int TestType, string? TestName, decimal QuantityPerTest);
public sealed record InventoryItemDto(Guid Id, string Code, string Name, string Kind, Guid ManufacturerId, string ManufacturerName, string? CatalogNumber,
    string Unit, decimal MinStock, decimal ReorderQuantity, int ExpiryWarningDays, string? StorageConditions, string? Notes, bool IsActive,
    decimal OnHand, IReadOnlyList<ItemTestLinkDto> TestLinks);

/// <summary>Stock of one item summed over the caller's visible stores (or the one store filtered): totals, value and the
/// alert flags the dashboard and the daily job raise.</summary>
public sealed record StockRowDto(Guid ItemId, string Code, string Name, string Kind, string Unit, string ManufacturerName,
    decimal MinStock, decimal ReorderQuantity, decimal OnHand, decimal Value, int LotCount, decimal ExpiringQuantity, decimal ExpiredQuantity,
    DateOnly? NearestExpiry, bool IsLow, bool IsOut);
public sealed record StockLotDto(Guid Id, Guid ItemId, string ItemCode, string ItemName, string Unit, Guid StoreId, string StoreName, string LotNumber,
    DateOnly? ExpiryDate, decimal Quantity, decimal UnitCost, decimal Value, DateOnly FirstReceivedOn, string Status);

/// <summary>One alert line. Kind: LowStock | OutOfStock | Expiring | Expired.</summary>
public sealed record InventoryAlertDto(string Kind, Guid ItemId, string ItemCode, string ItemName, string Unit, Guid? StoreId, string? StoreName,
    Guid? LotId, string? LotNumber, DateOnly? ExpiryDate, decimal Quantity, decimal? Threshold, string Message);
public sealed record InventoryDashboardDto(int ActiveItems, int Stores, int LotsWithStock, decimal StockValue, int LowStock, int OutOfStock, int Expiring, int Expired,
    int OpenPurchaseOrders, int TransfersInTransit, IReadOnlyList<InventoryAlertDto> Alerts);

public sealed record PurchaseOrderLineDto(Guid Id, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal OrderedQuantity, decimal UnitPrice,
    decimal ReceivedQuantity, decimal Outstanding, decimal LineTotal, string? Notes);
public sealed record PurchaseOrderLineInput(Guid ItemId, decimal OrderedQuantity, decimal UnitPrice, string? Notes);
public sealed record GoodsReceiptLineDto(Guid Id, Guid OrderLineId, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal Quantity, string LotNumber,
    DateOnly? ExpiryDate, decimal UnitCost);
public sealed record GoodsReceiptDto(Guid Id, long Serial, string Number, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid StoreId, string StoreName,
    string SupplierName, DateOnly ReceivedDate, string? DeliveryNote, string? InvoiceNumber, string? Notes, string ReceivedBy, IReadOnlyList<GoodsReceiptLineDto> Lines);
public sealed record ReceiptLineInput(Guid OrderLineId, decimal Quantity, string LotNumber, DateOnly? ExpiryDate, decimal? UnitCost);
public sealed record PurchaseOrderDto(Guid Id, long Serial, string Number, Guid SupplierId, string SupplierName, Guid StoreId, string StoreName,
    DateOnly OrderDate, DateOnly? ExpectedDate, string Status, string? Reference, string? Notes, DateOnly? OrderedOn, DateOnly? ClosedOn, decimal Total,
    decimal OrderedQuantity, decimal ReceivedQuantity, string CreatedBy, IReadOnlyList<PurchaseOrderLineDto> Lines, IReadOnlyList<GoodsReceiptDto> Receipts);

public sealed record StockMovementDto(Guid Id, long Serial, DateOnly Date, DateTimeOffset CreatedAt, string Type, Guid ItemId, string ItemCode, string ItemName,
    string Unit, Guid StoreId, string StoreName, Guid LotId, string LotNumber, DateOnly? ExpiryDate, decimal Quantity, decimal BalanceAfter, decimal UnitCost,
    string ReferenceKind, Guid? ReferenceId, string? ReferenceNumber, string? Reason, string? TestCode, string? Notes, string PerformedBy);

public sealed record StockTransferLineDto(Guid Id, Guid ItemId, string ItemCode, string ItemName, string Unit, Guid SourceLotId, string LotNumber, DateOnly? ExpiryDate,
    decimal Quantity, decimal? ReceivedQuantity, decimal UnitCost);
public sealed record StockTransferDto(Guid Id, long Serial, string Number, Guid FromStoreId, string FromStoreName, Guid ToStoreId, string ToStoreName, DateOnly Date,
    string Status, string? Notes, DateOnly? ReceivedDate, string? ReceiveNotes, string CreatedBy, bool CanReceive, IReadOnlyList<StockTransferLineDto> Lines);
public sealed record TransferLineInput(Guid LotId, decimal Quantity);
public sealed record TransferReceiveInput(Guid LineId, decimal ReceivedQuantity);

public sealed record UtilizationTestDto(string TestCode, int TestType, string TestName, int TestCount, decimal QuantityPerTest, decimal Expected);
/// <summary>Expected consumption (tests performed × quantity per test, from the synced test statistics) against the
/// consumption actually issued from stock. Variance = actual − expected; UtilizationPct = expected / actual × 100.</summary>
public sealed record UtilizationRowDto(Guid ItemId, string ItemCode, string ItemName, string Unit, int TestsPerformed, decimal Expected, decimal Actual, decimal Variance,
    decimal? UtilizationPct, IReadOnlyList<UtilizationTestDto> Tests);

// ---- Read side: query interface (implemented in Infrastructure, org scope pushed into the store filter) ----

public interface IInventoryQueries
{
    Task<IReadOnlyList<ManufacturerDto>> ManufacturersAsync(bool includeInactive, CancellationToken ct);
    Task<IReadOnlyList<SupplierDto>> SuppliersAsync(bool includeInactive, CancellationToken ct);
    Task<IReadOnlyList<StoreDto>> StoresAsync(OrgScope scope, bool includeInactive, CancellationToken ct);
    Task<IReadOnlyList<InventoryItemDto>> ItemsAsync(OrgScope scope, string? search, ItemKind? kind, bool includeInactive, CancellationToken ct);
    Task<InventoryItemDto?> ItemAsync(Guid id, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<StockRowDto>> StockAsync(OrgScope scope, Guid? storeId, ItemKind? kind, string? search, bool onlyAlerts, DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<StockLotDto>> LotsAsync(OrgScope scope, Guid? itemId, Guid? storeId, bool includeEmpty, DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<InventoryAlertDto>> AlertsAsync(OrgScope scope, DateOnly today, CancellationToken ct);
    Task<InventoryDashboardDto> DashboardAsync(OrgScope scope, DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<PurchaseOrderDto>> PurchaseOrdersAsync(OrgScope scope, DateOnly from, DateOnly to, PurchaseOrderStatus? status, Guid? supplierId, Guid? storeId, CancellationToken ct);
    Task<PurchaseOrderDto?> PurchaseOrderAsync(Guid id, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<GoodsReceiptDto>> GoodsReceiptsAsync(OrgScope scope, DateOnly from, DateOnly to, Guid? storeId, CancellationToken ct);
    Task<IReadOnlyList<StockMovementDto>> MovementsAsync(OrgScope scope, DateOnly from, DateOnly to, Guid? storeId, Guid? itemId, StockMovementType? type, CancellationToken ct);
    Task<IReadOnlyList<StockTransferDto>> TransfersAsync(OrgScope scope, DateOnly from, DateOnly to, TransferStatus? status, Guid? storeId, CancellationToken ct);
    Task<StockTransferDto?> TransferAsync(Guid id, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<UtilizationRowDto>> UtilizationAsync(OrgScope scope, DateOnly from, DateOnly to, Guid? storeId, Guid? itemId, CancellationToken ct);
}

/// <summary>Stores (and every stock row hanging off one) are org-scoped on the Branch dimension. Shared by the write-side
/// guard and the read-side filter.</summary>
public static class StoreScope
{
    public static bool IsVisible(OrgScope scope, string branch) =>
        scope.Branches.Contains(OrgScope.Wildcard) || scope.Branches.Contains(branch);

    public static void EnsureInScope(ICurrentUser user, Store store)
    {
        if (!IsVisible(user.Scope, store.Branch))
            throw new ForbiddenException("The store is outside your organizational scope.");
    }
}

internal static class InvEnum
{
    public static T Name<T>(string value, string field) where T : Enumeration =>
        Enumeration.GetAll<T>().FirstOrDefault(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase))
        ?? throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]> { [field] = new[] { $"'{value}' is not a valid {typeof(T).Name}." } });

    public static T? Optional<T>(string? value, string field) where T : Enumeration =>
        string.IsNullOrWhiteSpace(value) ? null : Name<T>(value, field);

    public static bool IsValid<T>(string? value) where T : Enumeration =>
        value is not null && Enumeration.GetAll<T>().Any(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase));
}

// ---- Queries ----

public sealed record GetManufacturersQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<ManufacturerDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetManufacturersHandler : IQueryHandler<GetManufacturersQuery, IReadOnlyList<ManufacturerDto>>
{
    private readonly IInventoryQueries _q;
    public GetManufacturersHandler(IInventoryQueries q) => _q = q;
    public Task<IReadOnlyList<ManufacturerDto>> Handle(GetManufacturersQuery r, CancellationToken ct) => _q.ManufacturersAsync(r.IncludeInactive, ct);
}

public sealed record GetSuppliersQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<SupplierDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetSuppliersHandler : IQueryHandler<GetSuppliersQuery, IReadOnlyList<SupplierDto>>
{
    private readonly IInventoryQueries _q;
    public GetSuppliersHandler(IInventoryQueries q) => _q = q;
    public Task<IReadOnlyList<SupplierDto>> Handle(GetSuppliersQuery r, CancellationToken ct) => _q.SuppliersAsync(r.IncludeInactive, ct);
}

public sealed record GetStoresQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<StoreDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetStoresHandler : IQueryHandler<GetStoresQuery, IReadOnlyList<StoreDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetStoresHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<StoreDto>> Handle(GetStoresQuery r, CancellationToken ct) => _q.StoresAsync(_user.Scope, r.IncludeInactive, ct);
}

public sealed record GetInventoryItemsQuery(string? Search, string? Kind, bool IncludeInactive = false) : IQuery<IReadOnlyList<InventoryItemDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetInventoryItemsHandler : IQueryHandler<GetInventoryItemsQuery, IReadOnlyList<InventoryItemDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetInventoryItemsHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<InventoryItemDto>> Handle(GetInventoryItemsQuery r, CancellationToken ct) =>
        _q.ItemsAsync(_user.Scope, r.Search, InvEnum.Optional<ItemKind>(r.Kind, nameof(r.Kind)), r.IncludeInactive, ct);
}

public sealed record GetInventoryItemQuery(Guid Id) : IQuery<InventoryItemDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetInventoryItemHandler : IQueryHandler<GetInventoryItemQuery, InventoryItemDto>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetInventoryItemHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public async Task<InventoryItemDto> Handle(GetInventoryItemQuery r, CancellationToken ct) =>
        await _q.ItemAsync(r.Id, _user.Scope, ct) ?? throw new NotFoundException("InventoryItem", r.Id);
}

public sealed record GetStockQuery(Guid? StoreId, string? Kind, string? Search, bool OnlyAlerts = false) : IQuery<IReadOnlyList<StockRowDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetStockHandler : IQueryHandler<GetStockQuery, IReadOnlyList<StockRowDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user; private readonly IClock _clock;
    public GetStockHandler(IInventoryQueries q, ICurrentUser user, IClock clock) { _q = q; _user = user; _clock = clock; }
    public Task<IReadOnlyList<StockRowDto>> Handle(GetStockQuery r, CancellationToken ct) =>
        _q.StockAsync(_user.Scope, r.StoreId, InvEnum.Optional<ItemKind>(r.Kind, nameof(r.Kind)), r.Search, r.OnlyAlerts, _clock.CairoToday, ct);
}

public sealed record GetStockLotsQuery(Guid? ItemId, Guid? StoreId, bool IncludeEmpty = false) : IQuery<IReadOnlyList<StockLotDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetStockLotsHandler : IQueryHandler<GetStockLotsQuery, IReadOnlyList<StockLotDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user; private readonly IClock _clock;
    public GetStockLotsHandler(IInventoryQueries q, ICurrentUser user, IClock clock) { _q = q; _user = user; _clock = clock; }
    public Task<IReadOnlyList<StockLotDto>> Handle(GetStockLotsQuery r, CancellationToken ct) =>
        _q.LotsAsync(_user.Scope, r.ItemId, r.StoreId, r.IncludeEmpty, _clock.CairoToday, ct);
}

public sealed record GetInventoryAlertsQuery() : IQuery<IReadOnlyList<InventoryAlertDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetInventoryAlertsHandler : IQueryHandler<GetInventoryAlertsQuery, IReadOnlyList<InventoryAlertDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user; private readonly IClock _clock;
    public GetInventoryAlertsHandler(IInventoryQueries q, ICurrentUser user, IClock clock) { _q = q; _user = user; _clock = clock; }
    public Task<IReadOnlyList<InventoryAlertDto>> Handle(GetInventoryAlertsQuery r, CancellationToken ct) => _q.AlertsAsync(_user.Scope, _clock.CairoToday, ct);
}

public sealed record GetInventoryDashboardQuery() : IQuery<InventoryDashboardDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetInventoryDashboardHandler : IQueryHandler<GetInventoryDashboardQuery, InventoryDashboardDto>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user; private readonly IClock _clock;
    public GetInventoryDashboardHandler(IInventoryQueries q, ICurrentUser user, IClock clock) { _q = q; _user = user; _clock = clock; }
    public Task<InventoryDashboardDto> Handle(GetInventoryDashboardQuery r, CancellationToken ct) => _q.DashboardAsync(_user.Scope, _clock.CairoToday, ct);
}

public sealed record GetPurchaseOrdersQuery(DateOnly From, DateOnly To, string? Status, Guid? SupplierId, Guid? StoreId) : IQuery<IReadOnlyList<PurchaseOrderDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetPurchaseOrdersHandler : IQueryHandler<GetPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetPurchaseOrdersHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<PurchaseOrderDto>> Handle(GetPurchaseOrdersQuery r, CancellationToken ct) =>
        _q.PurchaseOrdersAsync(_user.Scope, r.From, r.To, InvEnum.Optional<PurchaseOrderStatus>(r.Status, nameof(r.Status)), r.SupplierId, r.StoreId, ct);
}

public sealed record GetPurchaseOrderQuery(Guid Id) : IQuery<PurchaseOrderDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetPurchaseOrderHandler : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetPurchaseOrderHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public async Task<PurchaseOrderDto> Handle(GetPurchaseOrderQuery r, CancellationToken ct) =>
        await _q.PurchaseOrderAsync(r.Id, _user.Scope, ct) ?? throw new NotFoundException("PurchaseOrder", r.Id);
}

public sealed record GetGoodsReceiptsQuery(DateOnly From, DateOnly To, Guid? StoreId) : IQuery<IReadOnlyList<GoodsReceiptDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetGoodsReceiptsHandler : IQueryHandler<GetGoodsReceiptsQuery, IReadOnlyList<GoodsReceiptDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetGoodsReceiptsHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<GoodsReceiptDto>> Handle(GetGoodsReceiptsQuery r, CancellationToken ct) => _q.GoodsReceiptsAsync(_user.Scope, r.From, r.To, r.StoreId, ct);
}

public sealed record GetStockMovementsQuery(DateOnly From, DateOnly To, Guid? StoreId, Guid? ItemId, string? Type) : IQuery<IReadOnlyList<StockMovementDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetStockMovementsHandler : IQueryHandler<GetStockMovementsQuery, IReadOnlyList<StockMovementDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetStockMovementsHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<StockMovementDto>> Handle(GetStockMovementsQuery r, CancellationToken ct) =>
        _q.MovementsAsync(_user.Scope, r.From, r.To, r.StoreId, r.ItemId, InvEnum.Optional<StockMovementType>(r.Type, nameof(r.Type)), ct);
}

public sealed record GetStockTransfersQuery(DateOnly From, DateOnly To, string? Status, Guid? StoreId) : IQuery<IReadOnlyList<StockTransferDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetStockTransfersHandler : IQueryHandler<GetStockTransfersQuery, IReadOnlyList<StockTransferDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetStockTransfersHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<StockTransferDto>> Handle(GetStockTransfersQuery r, CancellationToken ct) =>
        _q.TransfersAsync(_user.Scope, r.From, r.To, InvEnum.Optional<TransferStatus>(r.Status, nameof(r.Status)), r.StoreId, ct);
}

public sealed record GetStockTransferQuery(Guid Id) : IQuery<StockTransferDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetStockTransferHandler : IQueryHandler<GetStockTransferQuery, StockTransferDto>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetStockTransferHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public async Task<StockTransferDto> Handle(GetStockTransferQuery r, CancellationToken ct) =>
        await _q.TransferAsync(r.Id, _user.Scope, ct) ?? throw new NotFoundException("StockTransfer", r.Id);
}

public sealed record GetUtilizationQuery(DateOnly From, DateOnly To, Guid? StoreId, Guid? ItemId) : IQuery<IReadOnlyList<UtilizationRowDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewInventory }; }
public sealed class GetUtilizationValidator : AbstractValidator<GetUtilizationQuery>
{ public GetUtilizationValidator() => RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From); }
public sealed class GetUtilizationHandler : IQueryHandler<GetUtilizationQuery, IReadOnlyList<UtilizationRowDto>>
{
    private readonly IInventoryQueries _q; private readonly ICurrentUser _user;
    public GetUtilizationHandler(IInventoryQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<UtilizationRowDto>> Handle(GetUtilizationQuery r, CancellationToken ct) => _q.UtilizationAsync(_user.Scope, r.From, r.To, r.StoreId, r.ItemId, ct);
}

// ---- Commands: master data ----

public sealed record CreateManufacturerCommand(string Name, string? Country, string? Notes) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class CreateManufacturerValidator : AbstractValidator<CreateManufacturerCommand>
{ public CreateManufacturerValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(150); RuleFor(x => x.Country).MaximumLength(80); RuleFor(x => x.Notes).MaximumLength(500); } }
public sealed class CreateManufacturerHandler : ICommandHandler<CreateManufacturerCommand, Guid>
{
    private readonly IManufacturerRepository _repo;
    public CreateManufacturerHandler(IManufacturerRepository repo) => _repo = repo;
    public Task<Guid> Handle(CreateManufacturerCommand r, CancellationToken ct)
    {
        var m = Manufacturer.Create(r.Name, r.Country, r.Notes);
        _repo.Add(m);
        return Task.FromResult(m.Id.Value);
    }
}

public sealed record UpdateManufacturerCommand(Guid Id, string Name, string? Country, string? Notes, bool IsActive) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class UpdateManufacturerValidator : AbstractValidator<UpdateManufacturerCommand>
{ public UpdateManufacturerValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(150); RuleFor(x => x.Country).MaximumLength(80); RuleFor(x => x.Notes).MaximumLength(500); } }
public sealed class UpdateManufacturerHandler : ICommandHandler<UpdateManufacturerCommand>
{
    private readonly IManufacturerRepository _repo;
    public UpdateManufacturerHandler(IManufacturerRepository repo) => _repo = repo;
    public async Task<Unit> Handle(UpdateManufacturerCommand r, CancellationToken ct)
    {
        var m = await _repo.GetByIdAsync(new ManufacturerId(r.Id), ct) ?? throw new NotFoundException("Manufacturer", r.Id);
        m.Update(r.Name, r.Country, r.Notes);
        m.Activate(r.IsActive);
        return Unit.Value;
    }
}

public sealed record CreateSupplierCommand(string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? Notes) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class CreateSupplierValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150); RuleFor(x => x.ContactPerson).MaximumLength(120); RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)); RuleFor(x => x.Address).MaximumLength(300); RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class CreateSupplierHandler : ICommandHandler<CreateSupplierCommand, Guid>
{
    private readonly ISupplierRepository _repo;
    public CreateSupplierHandler(ISupplierRepository repo) => _repo = repo;
    public Task<Guid> Handle(CreateSupplierCommand r, CancellationToken ct)
    {
        var s = Supplier.Create(r.Name, r.ContactPerson, r.Phone, r.Email, r.Address, r.Notes);
        _repo.Add(s);
        return Task.FromResult(s.Id.Value);
    }
}

public sealed record UpdateSupplierCommand(Guid Id, string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? Notes, bool IsActive) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class UpdateSupplierValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(150); RuleFor(x => x.ContactPerson).MaximumLength(120); RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)); RuleFor(x => x.Address).MaximumLength(300); RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class UpdateSupplierHandler : ICommandHandler<UpdateSupplierCommand>
{
    private readonly ISupplierRepository _repo;
    public UpdateSupplierHandler(ISupplierRepository repo) => _repo = repo;
    public async Task<Unit> Handle(UpdateSupplierCommand r, CancellationToken ct)
    {
        var s = await _repo.GetByIdAsync(new SupplierId(r.Id), ct) ?? throw new NotFoundException("Supplier", r.Id);
        s.Update(r.Name, r.ContactPerson, r.Phone, r.Email, r.Address, r.Notes);
        s.Activate(r.IsActive);
        return Unit.Value;
    }
}

public sealed record CreateStoreCommand(string Name, string Branch, string? Location) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class CreateStoreValidator : AbstractValidator<CreateStoreCommand>
{ public CreateStoreValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(120); RuleFor(x => x.Branch).NotEmpty().MaximumLength(100); RuleFor(x => x.Location).MaximumLength(200); } }
public sealed class CreateStoreHandler : ICommandHandler<CreateStoreCommand, Guid>
{
    private readonly IStoreRepository _repo; private readonly ICurrentUser _user;
    public CreateStoreHandler(IStoreRepository repo, ICurrentUser user) { _repo = repo; _user = user; }
    public Task<Guid> Handle(CreateStoreCommand r, CancellationToken ct)
    {
        var s = Store.Create(r.Name, r.Branch, r.Location);
        StoreScope.EnsureInScope(_user, s); // a scoped admin can only open stores on branches they cover
        _repo.Add(s);
        return Task.FromResult(s.Id.Value);
    }
}

public sealed record UpdateStoreCommand(Guid Id, string Name, string Branch, string? Location, bool IsActive) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class UpdateStoreValidator : AbstractValidator<UpdateStoreCommand>
{ public UpdateStoreValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(120); RuleFor(x => x.Branch).NotEmpty().MaximumLength(100); RuleFor(x => x.Location).MaximumLength(200); } }
public sealed class UpdateStoreHandler : ICommandHandler<UpdateStoreCommand>
{
    private readonly IStoreRepository _repo; private readonly ICurrentUser _user;
    public UpdateStoreHandler(IStoreRepository repo, ICurrentUser user) { _repo = repo; _user = user; }
    public async Task<Unit> Handle(UpdateStoreCommand r, CancellationToken ct)
    {
        var s = await _repo.GetByIdAsync(new StoreId(r.Id), ct) ?? throw new NotFoundException("Store", r.Id);
        StoreScope.EnsureInScope(_user, s);
        s.Update(r.Name, r.Branch, r.Location);
        StoreScope.EnsureInScope(_user, s); // the new branch must still be within the caller's scope
        s.Activate(r.IsActive);
        return Unit.Value;
    }
}

/// <summary>Shared item rules of Create/Update (the domain re-checks all of it; these give field-level 400s).</summary>
internal static class ItemRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, string> name, Func<T, string> kind, Func<T, Guid> manufacturer, Func<T, string?> catalog,
        Func<T, string> unit, Func<T, decimal> minStock, Func<T, decimal> reorder, Func<T, int> warnDays, Func<T, IReadOnlyList<ItemTestLinkInput>?> links)
    {
        v.RuleFor(x => name(x)).NotEmpty().MaximumLength(200).OverridePropertyName("Name");
        v.RuleFor(x => kind(x)).Must(InvEnum.IsValid<ItemKind>).WithMessage("Kind must be Chemical, Consumable, Kit, Control or Other.").OverridePropertyName("Kind");
        v.RuleFor(x => manufacturer(x)).NotEmpty().OverridePropertyName("ManufacturerId");
        v.RuleFor(x => catalog(x)).MaximumLength(80).OverridePropertyName("CatalogNumber");
        v.RuleFor(x => unit(x)).NotEmpty().MaximumLength(20).OverridePropertyName("Unit");
        v.RuleFor(x => minStock(x)).GreaterThanOrEqualTo(0).OverridePropertyName("MinStock");
        v.RuleFor(x => reorder(x)).GreaterThanOrEqualTo(0).OverridePropertyName("ReorderQuantity");
        v.RuleFor(x => warnDays(x)).InclusiveBetween(0, 730).OverridePropertyName("ExpiryWarningDays");
        v.RuleForEach(x => links(x) ?? Array.Empty<ItemTestLinkInput>()).ChildRules(l =>
        {
            l.RuleFor(t => t.TestCode).NotEmpty().MaximumLength(32);
            l.RuleFor(t => t.QuantityPerTest).GreaterThan(0);
        }).OverridePropertyName("TestLinks");
        v.RuleFor(x => links(x)).Must(l => l is null || l.Select(t => (t.TestCode.Trim().ToUpperInvariant(), t.TestType)).Distinct().Count() == l.Count)
            .WithMessage("A test can be linked to the item only once.").OverridePropertyName("TestLinks");
    }

    public static IEnumerable<ItemTestLink> Links(IReadOnlyList<ItemTestLinkInput>? links) =>
        (links ?? Array.Empty<ItemTestLinkInput>()).Select(l => ItemTestLink.Create(l.TestCode, l.TestType, l.TestName, l.QuantityPerTest));
}

public sealed record CreateInventoryItemCommand(string Code, string Name, string Kind, Guid ManufacturerId, string? CatalogNumber, string Unit, decimal MinStock,
    decimal ReorderQuantity, int ExpiryWarningDays, string? StorageConditions, string? Notes, IReadOnlyList<ItemTestLinkInput>? TestLinks) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class CreateInventoryItemValidator : AbstractValidator<CreateInventoryItemCommand>
{
    public CreateInventoryItemValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        ItemRules.Apply(this, x => x.Name, x => x.Kind, x => x.ManufacturerId, x => x.CatalogNumber, x => x.Unit, x => x.MinStock, x => x.ReorderQuantity, x => x.ExpiryWarningDays, x => x.TestLinks);
    }
}
public sealed class CreateInventoryItemHandler : ICommandHandler<CreateInventoryItemCommand, Guid>
{
    private readonly IInventoryItemRepository _items; private readonly IManufacturerRepository _manufacturers;
    public CreateInventoryItemHandler(IInventoryItemRepository items, IManufacturerRepository manufacturers) { _items = items; _manufacturers = manufacturers; }
    public async Task<Guid> Handle(CreateInventoryItemCommand r, CancellationToken ct)
    {
        var manufacturer = await _manufacturers.GetByIdAsync(new ManufacturerId(r.ManufacturerId), ct) ?? throw new NotFoundException("Manufacturer", r.ManufacturerId);
        if (!manufacturer.IsActive) throw new ConflictException("The manufacturer is inactive.");
        if (await _items.CodeExistsAsync(r.Code.Trim().ToUpperInvariant(), null, ct)) throw new ConflictException($"An item with code '{r.Code.Trim().ToUpperInvariant()}' already exists.");
        var item = InventoryItem.Create(r.Code, r.Name, InvEnum.Name<ItemKind>(r.Kind, nameof(r.Kind)), manufacturer.Id, r.CatalogNumber, r.Unit,
            r.MinStock, r.ReorderQuantity, r.ExpiryWarningDays, r.StorageConditions, r.Notes);
        item.SetTestLinks(ItemRules.Links(r.TestLinks));
        _items.Add(item);
        return item.Id.Value;
    }
}

public sealed record UpdateInventoryItemCommand(Guid Id, string Name, string Kind, Guid ManufacturerId, string? CatalogNumber, string Unit, decimal MinStock,
    decimal ReorderQuantity, int ExpiryWarningDays, string? StorageConditions, string? Notes, bool IsActive, IReadOnlyList<ItemTestLinkInput>? TestLinks) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class UpdateInventoryItemValidator : AbstractValidator<UpdateInventoryItemCommand>
{
    public UpdateInventoryItemValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        ItemRules.Apply(this, x => x.Name, x => x.Kind, x => x.ManufacturerId, x => x.CatalogNumber, x => x.Unit, x => x.MinStock, x => x.ReorderQuantity, x => x.ExpiryWarningDays, x => x.TestLinks);
    }
}
public sealed class UpdateInventoryItemHandler : ICommandHandler<UpdateInventoryItemCommand>
{
    private readonly IInventoryItemRepository _items; private readonly IManufacturerRepository _manufacturers;
    public UpdateInventoryItemHandler(IInventoryItemRepository items, IManufacturerRepository manufacturers) { _items = items; _manufacturers = manufacturers; }
    public async Task<Unit> Handle(UpdateInventoryItemCommand r, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(new InventoryItemId(r.Id), ct) ?? throw new NotFoundException("InventoryItem", r.Id);
        var manufacturer = await _manufacturers.GetByIdAsync(new ManufacturerId(r.ManufacturerId), ct) ?? throw new NotFoundException("Manufacturer", r.ManufacturerId);
        if (!manufacturer.IsActive && manufacturer.Id != item.ManufacturerId) throw new ConflictException("The manufacturer is inactive.");
        item.Update(r.Name, InvEnum.Name<ItemKind>(r.Kind, nameof(r.Kind)), manufacturer.Id, r.CatalogNumber, r.Unit, r.MinStock, r.ReorderQuantity, r.ExpiryWarningDays, r.StorageConditions, r.Notes);
        item.SetTestLinks(ItemRules.Links(r.TestLinks));
        item.Activate(r.IsActive);
        return Unit.Value;
    }
}

// ---- Commands: purchasing ----

/// <summary>Resolves and checks the distributor, the destination store (scope + active) and the items of an order.</summary>
internal static class PurchaseSupport
{
    public static async Task<(Supplier Supplier, Store Store, List<PurchaseOrderLine> Lines)> ResolveAsync(ISupplierRepository suppliers, IStoreRepository stores,
        IInventoryItemRepository items, ICurrentUser user, Guid supplierId, Guid storeId, IReadOnlyList<PurchaseOrderLineInput> lines, CancellationToken ct)
    {
        var supplier = await suppliers.GetByIdAsync(new SupplierId(supplierId), ct) ?? throw new NotFoundException("Supplier", supplierId);
        if (!supplier.IsActive) throw new ConflictException("The distributor is inactive.");
        var store = await stores.GetByIdAsync(new StoreId(storeId), ct) ?? throw new NotFoundException("Store", storeId);
        StoreScope.EnsureInScope(user, store);
        if (!store.IsActive) throw new ConflictException("The destination store is inactive.");
        var ids = lines.Select(l => new InventoryItemId(l.ItemId)).Distinct().ToList();
        var found = (await items.GetByIdsAsync(ids, ct)).ToDictionary(i => i.Id);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var item)) throw new NotFoundException("InventoryItem", id.Value);
            if (!item.IsActive) throw new ConflictException($"Item {item.Code} is inactive.");
        }
        return (supplier, store, lines.Select(l => PurchaseOrderLine.Create(new InventoryItemId(l.ItemId), l.OrderedQuantity, l.UnitPrice, l.Notes)).ToList());
    }

    public static void Rules<T>(AbstractValidator<T> v, Func<T, Guid> supplier, Func<T, Guid> store, Func<T, DateOnly> orderDate, Func<T, DateOnly?> expected,
        Func<T, string?> reference, Func<T, string?> notes, Func<T, IReadOnlyList<PurchaseOrderLineInput>> lines)
    {
        v.RuleFor(x => supplier(x)).NotEmpty().OverridePropertyName("SupplierId");
        v.RuleFor(x => store(x)).NotEmpty().OverridePropertyName("StoreId");
        v.RuleFor(x => expected(x)).Must((x, e) => e is null || e >= orderDate(x)).WithMessage("Expected delivery date cannot be before the order date.").OverridePropertyName("ExpectedDate");
        v.RuleFor(x => reference(x)).MaximumLength(80).OverridePropertyName("Reference");
        v.RuleFor(x => notes(x)).MaximumLength(1000).OverridePropertyName("Notes");
        v.RuleFor(x => lines(x)).NotNull().Must(l => l.Count > 0).WithMessage("Add at least one line.").OverridePropertyName("Lines");
        v.RuleFor(x => lines(x)).Must(l => l is null || l.Select(i => i.ItemId).Distinct().Count() == l.Count).WithMessage("An item can appear on the order only once.").OverridePropertyName("Lines");
        v.RuleForEach(x => lines(x)).ChildRules(l =>
        {
            l.RuleFor(i => i.ItemId).NotEmpty();
            l.RuleFor(i => i.OrderedQuantity).GreaterThan(0);
            l.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
            l.RuleFor(i => i.Notes).MaximumLength(300);
        }).OverridePropertyName("Lines");
    }
}

/// <summary>Creates a purchase order; <c>Submit</c> = true sends it to the distributor straight away (Ordered).</summary>
public sealed record CreatePurchaseOrderCommand(Guid SupplierId, Guid StoreId, DateOnly OrderDate, DateOnly? ExpectedDate, string? Reference, string? Notes,
    IReadOnlyList<PurchaseOrderLineInput> Lines, bool Submit = false) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderCommand>
{ public CreatePurchaseOrderValidator() => PurchaseSupport.Rules(this, x => x.SupplierId, x => x.StoreId, x => x.OrderDate, x => x.ExpectedDate, x => x.Reference, x => x.Notes, x => x.Lines); }
public sealed class CreatePurchaseOrderHandler : ICommandHandler<CreatePurchaseOrderCommand, Guid>
{
    private readonly IPurchaseOrderRepository _orders; private readonly ISupplierRepository _suppliers; private readonly IStoreRepository _stores;
    private readonly IInventoryItemRepository _items; private readonly ICurrentUser _user; private readonly IClock _clock;
    public CreatePurchaseOrderHandler(IPurchaseOrderRepository orders, ISupplierRepository suppliers, IStoreRepository stores, IInventoryItemRepository items, ICurrentUser user, IClock clock)
    { _orders = orders; _suppliers = suppliers; _stores = stores; _items = items; _user = user; _clock = clock; }
    public async Task<Guid> Handle(CreatePurchaseOrderCommand r, CancellationToken ct)
    {
        var (supplier, store, lines) = await PurchaseSupport.ResolveAsync(_suppliers, _stores, _items, _user, r.SupplierId, r.StoreId, r.Lines, ct);
        var po = PurchaseOrder.Create(supplier.Id, store.Id, r.OrderDate, r.ExpectedDate, r.Reference, r.Notes, lines);
        if (r.Submit) po.Submit(_clock.CairoToday);
        _orders.Add(po);
        return po.Id.Value;
    }
}

public sealed record UpdatePurchaseOrderCommand(Guid Id, Guid SupplierId, Guid StoreId, DateOnly OrderDate, DateOnly? ExpectedDate, string? Reference, string? Notes,
    IReadOnlyList<PurchaseOrderLineInput> Lines) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class UpdatePurchaseOrderValidator : AbstractValidator<UpdatePurchaseOrderCommand>
{
    public UpdatePurchaseOrderValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        PurchaseSupport.Rules(this, x => x.SupplierId, x => x.StoreId, x => x.OrderDate, x => x.ExpectedDate, x => x.Reference, x => x.Notes, x => x.Lines);
    }
}
public sealed class UpdatePurchaseOrderHandler : ICommandHandler<UpdatePurchaseOrderCommand>
{
    private readonly IPurchaseOrderRepository _orders; private readonly ISupplierRepository _suppliers; private readonly IStoreRepository _stores;
    private readonly IInventoryItemRepository _items; private readonly ICurrentUser _user;
    public UpdatePurchaseOrderHandler(IPurchaseOrderRepository orders, ISupplierRepository suppliers, IStoreRepository stores, IInventoryItemRepository items, ICurrentUser user)
    { _orders = orders; _suppliers = suppliers; _stores = stores; _items = items; _user = user; }
    public async Task<Unit> Handle(UpdatePurchaseOrderCommand r, CancellationToken ct)
    {
        var po = await _orders.GetByIdAsync(new PurchaseOrderId(r.Id), ct) ?? throw new NotFoundException("PurchaseOrder", r.Id);
        var current = await _stores.GetByIdAsync(po.StoreId, ct);
        if (current is not null) StoreScope.EnsureInScope(_user, current);
        if (!po.IsEditable) throw new ConflictException($"A {po.Status.Name} purchase order cannot be edited.");
        var (supplier, store, lines) = await PurchaseSupport.ResolveAsync(_suppliers, _stores, _items, _user, r.SupplierId, r.StoreId, r.Lines, ct);
        po.UpdateDraft(supplier.Id, store.Id, r.OrderDate, r.ExpectedDate, r.Reference, r.Notes, lines);
        return Unit.Value;
    }
}

/// <summary>Draft → Ordered, Ordered/PartiallyReceived → Closed, Draft/Ordered → Cancelled. Action: Submit | Close | Cancel.</summary>
public sealed record ChangePurchaseOrderStatusCommand(Guid Id, string Action) : ICommand, IAuthorizedRequest
{
    public const string Submit = "Submit"; public const string Close = "Close"; public const string Cancel = "Cancel";
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory };
}
public sealed class ChangePurchaseOrderStatusValidator : AbstractValidator<ChangePurchaseOrderStatusCommand>
{
    public ChangePurchaseOrderStatusValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Action).Must(a => a is ChangePurchaseOrderStatusCommand.Submit or ChangePurchaseOrderStatusCommand.Close or ChangePurchaseOrderStatusCommand.Cancel)
            .WithMessage("Action must be Submit, Close or Cancel.");
    }
}
public sealed class ChangePurchaseOrderStatusHandler : ICommandHandler<ChangePurchaseOrderStatusCommand>
{
    private readonly IPurchaseOrderRepository _orders; private readonly IStoreRepository _stores; private readonly ICurrentUser _user; private readonly IClock _clock;
    public ChangePurchaseOrderStatusHandler(IPurchaseOrderRepository orders, IStoreRepository stores, ICurrentUser user, IClock clock) { _orders = orders; _stores = stores; _user = user; _clock = clock; }
    public async Task<Unit> Handle(ChangePurchaseOrderStatusCommand r, CancellationToken ct)
    {
        var po = await _orders.GetByIdAsync(new PurchaseOrderId(r.Id), ct) ?? throw new NotFoundException("PurchaseOrder", r.Id);
        var store = await _stores.GetByIdAsync(po.StoreId, ct);
        if (store is not null) StoreScope.EnsureInScope(_user, store);
        var today = _clock.CairoToday;
        try
        {
            switch (r.Action)
            {
                case ChangePurchaseOrderStatusCommand.Submit: po.Submit(today); break;
                case ChangePurchaseOrderStatusCommand.Close: po.Close(today); break;
                default: po.Cancel(today); break;
            }
        }
        catch (DomainException ex) { throw new ConflictException(ex.Message); } // a state-machine refusal is a 409, not a 400
        return Unit.Value;
    }
}

/// <summary>Records a delivery against an order: the counts are validated line by line against the outstanding quantities,
/// every received quantity opens (or tops up) the lot of that lot number in the order's store, and a Receipt movement is
/// written per line. Returns the goods-receipt id.</summary>
public sealed record ReceivePurchaseOrderCommand(Guid PurchaseOrderId, DateOnly ReceivedDate, string? DeliveryNote, string? InvoiceNumber, string? Notes,
    IReadOnlyList<ReceiptLineInput> Lines) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class ReceivePurchaseOrderValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.DeliveryNote).MaximumLength(80); RuleFor(x => x.InvoiceNumber).MaximumLength(80); RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotNull().Must(l => l.Count > 0).WithMessage("Enter at least one received line.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(i => i.OrderLineId).NotEmpty();
            l.RuleFor(i => i.Quantity).GreaterThan(0);
            l.RuleFor(i => i.LotNumber).NotEmpty().MaximumLength(64);
            l.RuleFor(i => i.UnitCost).GreaterThanOrEqualTo(0).When(i => i.UnitCost is not null);
        });
        RuleFor(x => x).Must(x => x.Lines is null || x.Lines.All(l => l.ExpiryDate is null || l.ExpiryDate >= x.ReceivedDate))
            .WithMessage("An expired lot cannot be received into stock.").OverridePropertyName("Lines");
    }
}
public sealed class ReceivePurchaseOrderHandler : ICommandHandler<ReceivePurchaseOrderCommand, Guid>
{
    private readonly IPurchaseOrderRepository _orders; private readonly IGoodsReceiptRepository _receipts; private readonly IStoreRepository _stores;
    private readonly IStockLotRepository _lots; private readonly IStockMovementRepository _movements; private readonly ICurrentUser _user;
    public ReceivePurchaseOrderHandler(IPurchaseOrderRepository orders, IGoodsReceiptRepository receipts, IStoreRepository stores, IStockLotRepository lots,
        IStockMovementRepository movements, ICurrentUser user)
    { _orders = orders; _receipts = receipts; _stores = stores; _lots = lots; _movements = movements; _user = user; }

    public async Task<Guid> Handle(ReceivePurchaseOrderCommand r, CancellationToken ct)
    {
        var po = await _orders.GetByIdAsync(new PurchaseOrderId(r.PurchaseOrderId), ct) ?? throw new NotFoundException("PurchaseOrder", r.PurchaseOrderId);
        var store = await _stores.GetByIdAsync(po.StoreId, ct) ?? throw new NotFoundException("Store", po.StoreId.Value);
        StoreScope.EnsureInScope(_user, store);
        if (!po.IsOpen) throw new ConflictException($"A {po.Status.Name} purchase order cannot receive goods; submit it first.");

        var lines = new List<GoodsReceiptLine>();
        foreach (var input in r.Lines)
        {
            var orderLine = po.Lines.FirstOrDefault(l => l.Id.Value == input.OrderLineId) ?? throw new NotFoundException("PurchaseOrderLine", input.OrderLineId);
            lines.Add(GoodsReceiptLine.Create(orderLine.Id, orderLine.ItemId, input.Quantity, input.LotNumber, input.ExpiryDate, input.UnitCost ?? orderLine.UnitPrice.Amount, r.ReceivedDate));
        }
        var receipt = GoodsReceipt.Create(po, r.ReceivedDate, r.DeliveryNote, r.InvoiceNumber, r.Notes, lines);

        // Validate the counts against the order (throws when a line is over-delivered), then land each line in its lot.
        foreach (var line in receipt.Lines)
        {
            try { po.Receive(line.OrderLineId, line.Quantity); }
            catch (DomainException ex) { throw new ConflictException(ex.Message); }
            var lot = await ResolveLotAsync(line, store.Id, r.ReceivedDate, ct);
            lot.Add(line.Quantity, line.UnitCost.Amount, line.ExpiryDate);
            _movements.Add(StockMovement.Receipt(lot, r.ReceivedDate, line.Quantity, receipt.Id, $"{po.Number} · lot {line.LotNumber}"));
        }
        _receipts.Add(receipt);
        return receipt.Id.Value;
    }

    private readonly Dictionary<(InventoryItemId, string), StockLot> _opened = new();
    private async Task<StockLot> ResolveLotAsync(GoodsReceiptLine line, StoreId storeId, DateOnly receivedDate, CancellationToken ct)
    {
        var key = (line.ItemId, line.LotNumber.ToUpperInvariant());
        if (_opened.TryGetValue(key, out var pending)) return pending;
        var lot = await _lots.FindAsync(line.ItemId, storeId, line.LotNumber, ct);
        if (lot is null)
        {
            lot = StockLot.Open(line.ItemId, storeId, line.LotNumber, line.ExpiryDate, line.UnitCost.Amount, receivedDate);
            _lots.Add(lot);
        }
        else if (lot.ExpiryDate is { } existing && line.ExpiryDate is { } given && existing != given)
            throw new ConflictException($"Lot {lot.LotNumber} already exists in the store with expiry {existing:yyyy-MM-dd}; a lot number cannot carry two expiry dates.");
        _opened[key] = lot;
        return lot;
    }
}

// ---- Commands: stock ----

/// <summary>Issues stock from one lot by hand (consumption, damage, expiry disposal, return to the distributor).</summary>
public sealed record IssueStockCommand(Guid LotId, DateOnly Date, decimal Quantity, string Reason, string? TestCode, string? Notes) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class IssueStockValidator : AbstractValidator<IssueStockCommand>
{
    public IssueStockValidator()
    {
        RuleFor(x => x.LotId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Reason).Must(InvEnum.IsValid<IssueReason>).WithMessage("Reason must be Consumption, Damaged, Expired, ReturnToSupplier or Other.");
        RuleFor(x => x.TestCode).MaximumLength(32);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class IssueStockHandler : ICommandHandler<IssueStockCommand, Guid>
{
    private readonly IStockLotRepository _lots; private readonly IStockMovementRepository _movements; private readonly IStoreRepository _stores; private readonly ICurrentUser _user;
    public IssueStockHandler(IStockLotRepository lots, IStockMovementRepository movements, IStoreRepository stores, ICurrentUser user) { _lots = lots; _movements = movements; _stores = stores; _user = user; }
    public async Task<Guid> Handle(IssueStockCommand r, CancellationToken ct)
    {
        var lot = await _lots.GetByIdAsync(new StockLotId(r.LotId), ct) ?? throw new NotFoundException("StockLot", r.LotId);
        var store = await _stores.GetByIdAsync(lot.StoreId, ct) ?? throw new NotFoundException("Store", lot.StoreId.Value);
        StoreScope.EnsureInScope(_user, store);
        if (!store.IsActive) throw new ConflictException("The store is inactive.");
        try { lot.Take(r.Quantity); }
        catch (DomainException ex) { throw new ConflictException(ex.Message); } // not enough on hand → 409 with the on-hand figure
        var movement = StockMovement.Issue(lot, r.Date, r.Quantity, InvEnum.Name<IssueReason>(r.Reason, nameof(r.Reason)), r.TestCode, r.Notes);
        _movements.Add(movement);
        return movement.Id.Value;
    }
}

/// <summary>A physical count of one lot: the on-hand quantity becomes the counted one and the difference is posted as an Adjustment.</summary>
public sealed record AdjustStockCommand(Guid LotId, DateOnly Date, decimal CountedQuantity, string Reason, string? Notes) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.LotId).NotEmpty();
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class AdjustStockHandler : ICommandHandler<AdjustStockCommand, Guid>
{
    private readonly IStockLotRepository _lots; private readonly IStockMovementRepository _movements; private readonly IStoreRepository _stores; private readonly ICurrentUser _user;
    public AdjustStockHandler(IStockLotRepository lots, IStockMovementRepository movements, IStoreRepository stores, ICurrentUser user) { _lots = lots; _movements = movements; _stores = stores; _user = user; }
    public async Task<Guid> Handle(AdjustStockCommand r, CancellationToken ct)
    {
        var lot = await _lots.GetByIdAsync(new StockLotId(r.LotId), ct) ?? throw new NotFoundException("StockLot", r.LotId);
        var store = await _stores.GetByIdAsync(lot.StoreId, ct) ?? throw new NotFoundException("Store", lot.StoreId.Value);
        StoreScope.EnsureInScope(_user, store);
        decimal delta;
        try { delta = lot.SetCounted(r.CountedQuantity); }
        catch (DomainException ex) { throw new ConflictException(ex.Message); }
        var movement = StockMovement.Adjustment(lot, r.Date, delta, r.Reason, r.Notes);
        _movements.Add(movement);
        return movement.Id.Value;
    }
}

// ---- Commands: transfers ----

/// <summary>Dispatches lots from one store to another: the quantities leave the source now (TransferOut) and wait in
/// transit until the destination confirms them.</summary>
public sealed record CreateStockTransferCommand(Guid FromStoreId, Guid ToStoreId, DateOnly Date, string? Notes, IReadOnlyList<TransferLineInput> Lines) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class CreateStockTransferValidator : AbstractValidator<CreateStockTransferCommand>
{
    public CreateStockTransferValidator()
    {
        RuleFor(x => x.FromStoreId).NotEmpty();
        RuleFor(x => x.ToStoreId).NotEmpty().NotEqual(x => x.FromStoreId).WithMessage("The source and destination stores must differ.");
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotNull().Must(l => l.Count > 0).WithMessage("Add at least one line.");
        RuleFor(x => x.Lines).Must(l => l is null || l.Select(i => i.LotId).Distinct().Count() == l.Count).WithMessage("A lot can appear on the transfer only once.");
        RuleForEach(x => x.Lines).ChildRules(l => { l.RuleFor(i => i.LotId).NotEmpty(); l.RuleFor(i => i.Quantity).GreaterThan(0); });
    }
}
public sealed class CreateStockTransferHandler : ICommandHandler<CreateStockTransferCommand, Guid>
{
    private readonly IStockTransferRepository _transfers; private readonly IStoreRepository _stores; private readonly IStockLotRepository _lots;
    private readonly IStockMovementRepository _movements; private readonly ICurrentUser _user;
    public CreateStockTransferHandler(IStockTransferRepository transfers, IStoreRepository stores, IStockLotRepository lots, IStockMovementRepository movements, ICurrentUser user)
    { _transfers = transfers; _stores = stores; _lots = lots; _movements = movements; _user = user; }

    public async Task<Guid> Handle(CreateStockTransferCommand r, CancellationToken ct)
    {
        var from = await _stores.GetByIdAsync(new StoreId(r.FromStoreId), ct) ?? throw new NotFoundException("Store", r.FromStoreId);
        var to = await _stores.GetByIdAsync(new StoreId(r.ToStoreId), ct) ?? throw new NotFoundException("Store", r.ToStoreId);
        StoreScope.EnsureInScope(_user, from);
        StoreScope.EnsureInScope(_user, to);
        if (!from.IsActive || !to.IsActive) throw new ConflictException("Both stores must be active.");

        var lots = (await _lots.GetByIdsAsync(r.Lines.Select(l => new StockLotId(l.LotId)), ct)).ToDictionary(l => l.Id);
        var lines = new List<StockTransferLine>();
        foreach (var input in r.Lines)
        {
            if (!lots.TryGetValue(new StockLotId(input.LotId), out var lot)) throw new NotFoundException("StockLot", input.LotId);
            if (lot.StoreId != from.Id) throw new ConflictException($"Lot {lot.LotNumber} is not in the source store.");
            lines.Add(StockTransferLine.FromLot(lot, input.Quantity));
        }
        var transfer = StockTransfer.Create(from.Id, to.Id, r.Date, r.Notes, lines);
        foreach (var line in transfer.Lines)
        {
            var lot = lots[line.SourceLotId];
            try { lot.Take(line.Quantity); }
            catch (DomainException ex) { throw new ConflictException(ex.Message); }
            _movements.Add(StockMovement.TransferOut(lot, r.Date, line.Quantity, transfer.Id, $"→ {to.Name}"));
        }
        _transfers.Add(transfer);
        return transfer.Id.Value;
    }
}

/// <summary>The destination confirms the counts: what arrived lands in the destination's lots (TransferIn); a shortfall stays on the transfer.</summary>
public sealed record ReceiveStockTransferCommand(Guid Id, DateOnly ReceivedDate, IReadOnlyList<TransferReceiveInput>? Lines, string? Notes) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class ReceiveStockTransferValidator : AbstractValidator<ReceiveStockTransferCommand>
{
    public ReceiveStockTransferValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleForEach(x => x.Lines ?? Array.Empty<TransferReceiveInput>()).ChildRules(l => { l.RuleFor(i => i.LineId).NotEmpty(); l.RuleFor(i => i.ReceivedQuantity).GreaterThanOrEqualTo(0); })
            .OverridePropertyName("Lines");
    }
}
public sealed class ReceiveStockTransferHandler : ICommandHandler<ReceiveStockTransferCommand>
{
    private readonly IStockTransferRepository _transfers; private readonly IStoreRepository _stores; private readonly IStockLotRepository _lots;
    private readonly IStockMovementRepository _movements; private readonly ICurrentUser _user;
    public ReceiveStockTransferHandler(IStockTransferRepository transfers, IStoreRepository stores, IStockLotRepository lots, IStockMovementRepository movements, ICurrentUser user)
    { _transfers = transfers; _stores = stores; _lots = lots; _movements = movements; _user = user; }

    public async Task<Unit> Handle(ReceiveStockTransferCommand r, CancellationToken ct)
    {
        var transfer = await _transfers.GetByIdAsync(new StockTransferId(r.Id), ct) ?? throw new NotFoundException("StockTransfer", r.Id);
        var to = await _stores.GetByIdAsync(transfer.ToStoreId, ct) ?? throw new NotFoundException("Store", transfer.ToStoreId.Value);
        StoreScope.EnsureInScope(_user, to); // receiving is the destination store's act
        var from = await _stores.GetByIdAsync(transfer.FromStoreId, ct);
        var received = (r.Lines ?? Array.Empty<TransferReceiveInput>()).ToDictionary(l => new StockTransferLineId(l.LineId), l => l.ReceivedQuantity);
        try { transfer.Receive(r.ReceivedDate, received, r.Notes); }
        catch (DomainException ex) { throw new ConflictException(ex.Message); }

        var opened = new Dictionary<(InventoryItemId, string), StockLot>();
        foreach (var line in transfer.Lines.Where(l => l.ReceivedQuantity is > 0))
        {
            var key = (line.ItemId, line.LotNumber.ToUpperInvariant());
            if (!opened.TryGetValue(key, out var lot))
            {
                lot = await _lots.FindAsync(line.ItemId, to.Id, line.LotNumber, ct);
                if (lot is null) { lot = StockLot.Open(line.ItemId, to.Id, line.LotNumber, line.ExpiryDate, line.UnitCost.Amount, r.ReceivedDate); _lots.Add(lot); }
                opened[key] = lot;
            }
            lot.Add(line.ReceivedQuantity!.Value, line.UnitCost.Amount, line.ExpiryDate);
            var note = line.Shortfall > 0 ? $"← {from?.Name ?? "store"} · short by {line.Shortfall:0.###}" : $"← {from?.Name ?? "store"}";
            _movements.Add(StockMovement.TransferIn(lot, r.ReceivedDate, line.ReceivedQuantity!.Value, transfer.Id, note));
        }
        return Unit.Value;
    }
}

/// <summary>Cancels an in-transit transfer: every dispatched quantity goes back into its source lot.</summary>
public sealed record CancelStockTransferCommand(Guid Id, string? Notes) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class CancelStockTransferValidator : AbstractValidator<CancelStockTransferCommand>
{ public CancelStockTransferValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Notes).MaximumLength(1000); } }
public sealed class CancelStockTransferHandler : ICommandHandler<CancelStockTransferCommand>
{
    private readonly IStockTransferRepository _transfers; private readonly IStoreRepository _stores; private readonly IStockLotRepository _lots;
    private readonly IStockMovementRepository _movements; private readonly ICurrentUser _user; private readonly IClock _clock;
    public CancelStockTransferHandler(IStockTransferRepository transfers, IStoreRepository stores, IStockLotRepository lots, IStockMovementRepository movements, ICurrentUser user, IClock clock)
    { _transfers = transfers; _stores = stores; _lots = lots; _movements = movements; _user = user; _clock = clock; }

    public async Task<Unit> Handle(CancelStockTransferCommand r, CancellationToken ct)
    {
        var transfer = await _transfers.GetByIdAsync(new StockTransferId(r.Id), ct) ?? throw new NotFoundException("StockTransfer", r.Id);
        var from = await _stores.GetByIdAsync(transfer.FromStoreId, ct) ?? throw new NotFoundException("Store", transfer.FromStoreId.Value);
        StoreScope.EnsureInScope(_user, from);
        try { transfer.Cancel(r.Notes); }
        catch (DomainException ex) { throw new ConflictException(ex.Message); }
        var today = _clock.CairoToday;
        var lots = (await _lots.GetByIdsAsync(transfer.Lines.Select(l => l.SourceLotId), ct)).ToDictionary(l => l.Id);
        foreach (var line in transfer.Lines)
        {
            if (!lots.TryGetValue(line.SourceLotId, out var lot))
            {
                lot = StockLot.Open(line.ItemId, from.Id, line.LotNumber, line.ExpiryDate, line.UnitCost.Amount, today);
                _lots.Add(lot);
            }
            lot.Add(line.Quantity, line.UnitCost.Amount, line.ExpiryDate);
            _movements.Add(StockMovement.TransferIn(lot, today, line.Quantity, transfer.Id, $"{transfer.Number} cancelled — returned to {from.Name}"));
        }
        return Unit.Value;
    }
}

// ---- Commands: alerts ----

/// <summary>Runs the stock-limit / expiry evaluation now and pushes the in-app summary (same pass as the daily job).</summary>
public sealed record RunInventoryAlertsCommand : ICommand<InventoryAlertResult>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageInventory }; }
public sealed class RunInventoryAlertsValidator : AbstractValidator<RunInventoryAlertsCommand> { }
public sealed class RunInventoryAlertsHandler : ICommandHandler<RunInventoryAlertsCommand, InventoryAlertResult>
{
    private readonly IInventoryAlertRunner _runner; private readonly IClock _clock;
    public RunInventoryAlertsHandler(IInventoryAlertRunner runner, IClock clock) { _runner = runner; _clock = clock; }
    public Task<InventoryAlertResult> Handle(RunInventoryAlertsCommand r, CancellationToken ct) => _runner.RunAsync(_clock.CairoToday, manual: true, ct);
}
