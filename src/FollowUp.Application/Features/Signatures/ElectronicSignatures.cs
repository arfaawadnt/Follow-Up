using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.Complaints.Commands; // ComplaintActionSupport — single-sourced record scope check
using FollowUp.Domain.Common;
using FollowUp.Domain.Signatures;
using FluentValidation;

namespace FollowUp.Application.Features.Signatures;

// ---- Sign ----

/// <summary>
/// Signs a record (SRS FR-19). Re-authenticates the signer (password), computes the content hash and version
/// server-side, and binds identity + auth level + intent/meaning + record id/version + timestamp + reason.
/// </summary>
public sealed record SignRecordCommand(string Module, string RecordId, string Meaning, string? Reason, string Password)
    : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>(); // authenticated; re-auth inside
}

public sealed class SignRecordValidator : AbstractValidator<SignRecordCommand>
{
    public SignRecordValidator()
    {
        RuleFor(x => x.Module).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        RuleFor(x => x.Meaning).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class SignRecordHandler : ICommandHandler<SignRecordCommand, Guid>
{
    private readonly IElectronicSignatureRepository _signatures;
    private readonly IAppUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IRecordHasher _recordHasher;
    private readonly ICurrentUser _caller;
    private readonly IClock _clock;

    public SignRecordHandler(IElectronicSignatureRepository signatures, IAppUserRepository users,
        IPasswordHasher hasher, IRecordHasher recordHasher, ICurrentUser caller, IClock clock)
    {
        _signatures = signatures; _users = users; _hasher = hasher;
        _recordHasher = recordHasher; _caller = caller; _clock = clock;
    }

    public async Task<Guid> Handle(SignRecordCommand request, CancellationToken ct)
    {
        // Re-authenticate the signer (SRS FR-19).
        var user = await _users.GetByIdAsync(_caller.UserId, ct)
            ?? throw new NotFoundException("User", _caller.UserId);
        if (!_hasher.Verify(request.Password, user.Password))
            throw new ForbiddenException("Re-authentication failed.");

        // Server-computed content hash + version (client never supplies these).
        var computed = await _recordHasher.ComputeAsync(request.Module, request.RecordId, ct)
            ?? throw new NotFoundException($"{request.Module} record", request.RecordId);

        var signature = ElectronicSignature.Create(
            request.Module, request.RecordId, computed.Version,
            _caller.UserId.Value, _caller.Username, authLevel: "password",
            Enumeration.FromName<SignatureMeaning>(request.Meaning), request.Reason,
            computed.ContentHash, _clock.UtcNow, _caller.Ip);

        _signatures.Add(signature);
        return signature.Id.Value;
    }
}

// ---- Verify ----

public sealed record SignatureVerificationDto(
    bool Signed, bool StillValid, string? SignerUsername, string? Meaning, DateTimeOffset? SignedAt, uint? SignedVersion);

/// <summary>Verifies whether a record still matches its latest signature (tamper evidence, SRS FR-19).</summary>
public sealed record VerifySignatureQuery(string Module, string RecordId) : IQuery<SignatureVerificationDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class VerifySignatureHandler : IQueryHandler<VerifySignatureQuery, SignatureVerificationDto>
{
    private readonly IElectronicSignatureRepository _signatures;
    private readonly IRecordHasher _recordHasher;
    private readonly ICurrentUser _user;
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;

    public VerifySignatureHandler(IElectronicSignatureRepository signatures, IRecordHasher recordHasher,
        ICurrentUser user, IComplaintRepository complaints, ILaboratoryRepository labs)
    {
        _signatures = signatures; _recordHasher = recordHasher;
        _user = user; _complaints = complaints; _labs = labs;
    }

    public async Task<SignatureVerificationDto> Handle(VerifySignatureQuery request, CancellationToken ct)
    {
        // Enforce record-level org scope before disclosing signature metadata (SRS SCOPE-READ): the signed
        // record's lab must be within the caller's scope. Reuses the canonical complaint load+authorize
        // helper so the scope rule stays single-sourced; "complaint" is the only signable module today and
        // an unknown module fails closed.
        await EnsureRecordInScopeAsync(request.Module, request.RecordId, ct);

        var signature = await _signatures.GetLatestAsync(request.Module, request.RecordId, ct);
        if (signature is null)
            return new SignatureVerificationDto(false, false, null, null, null, null);

        var computed = await _recordHasher.ComputeAsync(request.Module, request.RecordId, ct);
        var stillValid = computed is { } c && signature.StillValidFor(c.ContentHash, c.Version);

        return new SignatureVerificationDto(true, stillValid, signature.SignerUsername,
            signature.Meaning.Name, signature.SignedAt, signature.RecordVersion);
    }

    private async Task EnsureRecordInScopeAsync(string module, string recordId, CancellationToken ct)
    {
        if (string.Equals(module, ComplaintActionSupport.Module, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(recordId, out var complaintId))
        {
            // Throws NotFound if the record is absent, Forbidden if it is outside the caller's scope.
            await ComplaintActionSupport.LoadAuthorizedAsync(complaintId, _complaints, _labs, _user, ct);
            return;
        }
        throw new NotFoundException($"{module} record", recordId);
    }
}
