using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FluentValidation;

namespace FollowUp.Application.Features.Platform;

/// <summary>
/// Resolves a Google-Maps short link to its target (SRS FR-21). Authenticated-only (no specific privilege);
/// the SSRF guard lives in the Infrastructure resolver.
/// </summary>
public sealed record ResolveMapLinkQuery(string ShortUrl) : IQuery<string>, IAuthorizedRequest
{
    // Empty required-privileges => authenticated caller is sufficient (AuthenticatedOnly).
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class ResolveMapLinkValidator : AbstractValidator<ResolveMapLinkQuery>
{
    public ResolveMapLinkValidator() =>
        RuleFor(x => x.ShortUrl).NotEmpty().Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("A valid absolute URL is required.");
}

public sealed class ResolveMapLinkHandler : IQueryHandler<ResolveMapLinkQuery, string>
{
    private readonly IMapLinkResolver _resolver;

    public ResolveMapLinkHandler(IMapLinkResolver resolver) => _resolver = resolver;

    public async Task<string> Handle(ResolveMapLinkQuery request, CancellationToken ct)
    {
        var target = await _resolver.ResolveAsync(request.ShortUrl, ct)
            ?? throw new NotFoundException("The map link could not be resolved to an allow-listed host.");
        return target;
    }
}
