using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
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
        RuleFor(x => x.Module).Must(SignableModule.IsKnown).WithMessage("'{PropertyValue}' is not a signable record type."); // SIG-10 closed set
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
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly IAuthPolicy _policy;
    private readonly IFailedLoginRecorder _failedLogins;

    public SignRecordHandler(IElectronicSignatureRepository signatures, IAppUserRepository users,
        IPasswordHasher hasher, IRecordHasher recordHasher, ICurrentUser caller, IClock clock,
        IComplaintRepository complaints, ILaboratoryRepository labs,
        IAuthPolicy policy, IFailedLoginRecorder failedLogins)
    {
        _signatures = signatures; _users = users; _hasher = hasher;
        _recordHasher = recordHasher; _caller = caller; _clock = clock;
        _complaints = complaints; _labs = labs;
        _policy = policy; _failedLogins = failedLogins;
    }

    public async Task<Guid> Handle(SignRecordCommand request, CancellationToken ct)
    {
        // Re-authenticate the signer (SRS FR-19). This is a password check like login, so it honours the same
        // lockout (FR-1/NFR-SEC-4): a locked account cannot sign even with the right password, and a wrong
        // password counts toward lockout — otherwise signing is a lockout-bypassing password oracle (SIG-5).
        var now = _clock.UtcNow;
        var user = await _users.GetByIdAsync(_caller.UserId, ct)
            ?? throw new NotFoundException("User", _caller.UserId);

        if (user.IsLockedOut(now))
            throw new ForbiddenException("The account is temporarily locked. Try again later.");

        if (!_hasher.Verify(request.Password, user.Password))
        {
            // The throw rolls back this command's transaction, so persist the attempt in its own unit of work —
            // the same durable path login uses, or the counter never advances in production (finding IDN-1).
            user.RegisterFailedLogin(_policy.MaxFailedAttempts, _policy.LockoutWindow, now);
            await _failedLogins.RecordAsync(user.Id, ct);
            throw new ForbiddenException("Re-authentication failed.");
        }

        // Signing is "within organizational scope" (SRS FR-19): the record's lab must be in the signer's
        // scope. Runs after re-auth so a wrong password never reveals whether the record exists (finding SIG-2).
        await SignatureRecordScope.EnsureInScopeAsync(request.Module, request.RecordId, _complaints, _labs, _caller, ct);

        // Server-computed content hash + version (client never supplies these).
        var computed = await _recordHasher.ComputeAsync(request.Module, request.RecordId, ct)
            ?? throw new NotFoundException($"{request.Module} record", request.RecordId);
        var meaning = Enumeration.FromName<SignatureMeaning>(request.Meaning);

        // Idempotency (SIG-6): a retried sign — timeout+re-click, gateway retry, double-tap racing the client's
        // busy flag (which the standard forbids as the boundary) — must not append a second near-identical row
        // to the append-only signature log. If the latest signature already attests this exact state (same
        // signer, meaning, reason, hash, version), return it. A deliberate re-sign with a different meaning or
        // reason, or after a material change (new hash/version), still creates a new signature.
        var latest = await _signatures.GetLatestAsync(request.Module, request.RecordId, ct);
        if (latest is not null
            && latest.SignerUserId == _caller.UserId.Value
            && latest.Meaning == meaning
            && latest.RecordVersion == computed.Version
            && string.Equals(latest.ContentHash, computed.ContentHash, StringComparison.Ordinal)
            && string.Equals(latest.Reason, request.Reason, StringComparison.Ordinal))
        {
            return latest.Id.Value;
        }

        var signature = ElectronicSignature.Create(
            request.Module, request.RecordId, computed.Version,
            _caller.UserId.Value, _caller.Username, authLevel: "password",
            meaning, request.Reason,
            computed.ContentHash, now, _caller.Ip);

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
        // Enforce record-level org scope before disclosing signature metadata (SRS SCOPE-READ / FR-19):
        // the signed record's lab must be within the caller's scope.
        await SignatureRecordScope.EnsureInScopeAsync(request.Module, request.RecordId, _complaints, _labs, _user, ct);

        var signature = await _signatures.GetLatestAsync(request.Module, request.RecordId, ct);
        if (signature is null)
            return new SignatureVerificationDto(false, false, null, null, null, null);

        var computed = await _recordHasher.ComputeAsync(request.Module, request.RecordId, ct);
        var stillValid = computed is { } c && signature.StillValidFor(c.ContentHash, c.Version);

        return new SignatureVerificationDto(true, stillValid, signature.SignerUsername,
            signature.Meaning.Name, signature.SignedAt, signature.RecordVersion);
    }
}
