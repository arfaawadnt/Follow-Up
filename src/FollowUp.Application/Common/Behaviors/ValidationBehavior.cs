using FluentValidation;
using MediatR;
using ValidationException = FollowUp.Application.Common.Exceptions.ValidationException;

namespace FollowUp.Application.Common.Behaviors;

/// <summary>
/// Runs all FluentValidation validators registered for the request before the handler. On failure it throws
/// the application <see cref="ValidationException"/> (mapped to a single RFC 7807 400 at the API), so a
/// caller error is never reported as a server fault (SRS NFR-UX-4).
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, ct)));

        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
        if (failures.Count > 0)
        {
            var errors = failures
                .GroupBy(f => f.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());
            throw new ValidationException(errors);
        }

        return await next();
    }
}
