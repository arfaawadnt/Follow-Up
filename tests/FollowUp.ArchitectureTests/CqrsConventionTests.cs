using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentValidation;
using FollowUp.Application.Common.Messaging;
using MediatR;

namespace FollowUp.ArchitectureTests;

/// <summary>
/// CQRS and application-pipeline rules from the architect ruleset. Two rules are ratchets: the pre-existing
/// gaps are pinned in explicit allowlists (they are findings in the compliance report), and the tests fail if
/// a NEW request repeats the gap — or if an allowlist entry silently gets fixed without being removed here.
/// </summary>
public class CqrsConventionTests
{
    private static readonly Assembly Domain = typeof(FollowUp.Domain.Common.Entity<int>).Assembly;
    private static readonly Assembly Application = typeof(FollowUp.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(FollowUp.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static bool ImplementsOpenInterface(Type t, Type openGeneric) =>
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric);

    private static IReadOnlyList<Type> Commands { get; } = Application.GetTypes()
        .Where(t => typeof(IBaseCommand).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
        .ToList();

    private static IReadOnlyList<Type> Queries { get; } = Application.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && ImplementsOpenInterface(t, typeof(IQuery<>)))
        .ToList();

    [Fact]
    public void Request_handlers_live_only_in_the_application_layer()
    {
        var offenders = new[] { Api, Infrastructure, Domain }
            .SelectMany(a => a.GetTypes())
            .Where(t => ImplementsOpenInterface(t, typeof(IRequestHandler<,>))
                        || ImplementsOpenInterface(t, typeof(IRequestHandler<>)))
            .Select(t => t.FullName)
            .ToList();
        offenders.Should().BeEmpty("use cases (IRequestHandler) belong to the Application layer");
    }

    // FINDING (verification cycle 2026-08-27): 46 of 75 commands predate the "every command ships with a
    // validator" rule. Do not add entries; remove each one as its validator is written.
    private static readonly IReadOnlySet<string> CommandsWithoutValidatorYet = new HashSet<string>(StringComparer.Ordinal)
    {
        "AdvanceOutsourceStatusCommand", "AdvanceSampleTrackingCommand", "BatchRecordSampleDataEntryCommand",
        "CancelMarketingVisitCommand", "ChangeUserRoleCommand", "ConfirmReceiptCommand", "CreateAreaCommand",
        "CreateCityCommand", "CreateTestGroupCommand", "CreateTestSetupCommand", "DeleteAreaCommand",
        "DeleteCityCommand", "DeleteOutsourceSampleCommand", "DeleteRefItemCommand", "DeleteRoleCommand",
        "DeleteTestGroupCommand", "DeleteTestSetupCommand", "DeleteUserCommand", "ImportLabStatsCommand",
        "ImportTestStatsCommand", "LogoutCommand", "MarkAllNotificationsReadCommand", "MarkNotificationReadCommand",
        "MissVisitCommand", "RecalculateAllLoyaltyCommand", "RecalculateLoyaltyCommand",
        "ReopenComplaintCommand", "ResolveComplaintCommand", "RetryDeliveryCommand", "RunRetentionCommand",
        "SaveCommissionCommand", "SetCompensationConfigCommand", "SetOwnLanguageCommand", "StartComplaintCommand",
        "SyncOracleNowCommand", "UndoVisitCommand", "UnlockUserCommand", "UpdateNotificationPreferenceCommand",
        "UpdateRoleCommand", "UpdateTestGroupCommand", "UpdateTestSetupCommand", "UpdateUserCommand",
        "UploadLabImageCommand", "UpsertSettingCommand", "VerifyVisitCommand",
    };

    [Fact]
    public void Every_command_has_a_validator_ratchet()
    {
        var validatedTypes = Application.GetTypes()
            .Select(ValidatorTarget)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToHashSet();

        var missing = Commands
            .Where(c => !validatedTypes.Contains(c) && !CommandsWithoutValidatorYet.Contains(c.Name))
            .Select(c => c.Name)
            .ToList();
        missing.Should().BeEmpty("every new command must ship with an AbstractValidator");

        var stale = CommandsWithoutValidatorYet
            .Where(name => Commands.Any(c => c.Name == name && validatedTypes.Contains(c)))
            .ToList();
        stale.Should().BeEmpty("these commands gained validators — remove them from the allowlist");
    }

    private static Type? ValidatorTarget(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                return b.GetGenericArguments()[0];
        return null;
    }

    private static readonly IReadOnlySet<string> AnonymousOrUnprivilegedRequests = new HashSet<string>(StringComparer.Ordinal)
    {
        "LoginCommand", // anonymous by design — the only unauthenticated endpoint
        // FINDINGS (2026-08-27): authenticated but not privilege-checked; see the compliance report.
        "GetLaboratoriesQuery",
        "GetLaboratoryByIdQuery",
    };

    [Fact]
    public void Every_request_declares_its_required_privileges_ratchet()
    {
        var missing = Commands.Concat(Queries)
            .Where(t => !typeof(IAuthorizedRequest).IsAssignableFrom(t)
                        && !AnonymousOrUnprivilegedRequests.Contains(t.Name))
            .Select(t => t.Name)
            .Distinct()
            .ToList();
        missing.Should().BeEmpty("every request must declare RequiredPrivileges via IAuthorizedRequest");

        var stale = AnonymousOrUnprivilegedRequests
            .Where(name => Commands.Concat(Queries).Any(t => t.Name == name && typeof(IAuthorizedRequest).IsAssignableFrom(t)))
            .ToList();
        stale.Should().BeEmpty("these requests now declare privileges — remove them from the allowlist");
    }

    // Reviewed exceptions (2026-08-27):
    // - VerifySignatureHandler loads the ElectronicSignature aggregate to evaluate its StillValidFor(...)
    //   domain behavior — a projection would duplicate that rule outside Domain.
    // - GetRetentionHandler / GetIntegrationConfigHandler read config via aggregate repositories; FINDINGS
    //   in the compliance report — move them to ISettingsQueries/projection reads, then remove them here.
    private static readonly IReadOnlySet<string> ReviewedQueryHandlerRepositoryUse = new HashSet<string>(StringComparer.Ordinal)
    {
        "VerifySignatureHandler",
        "GetRetentionHandler",
        "GetIntegrationConfigHandler",
    };

    [Fact]
    public void Query_handlers_do_not_touch_the_write_side()
    {
        var offenders =
            (from t in Application.GetTypes()
             where ImplementsOpenInterface(t, typeof(IQueryHandler<,>))
                   && !ReviewedQueryHandlerRepositoryUse.Contains(t.Name)
             from p in t.GetConstructors().SelectMany(c => c.GetParameters())
             where p.ParameterType.Namespace == "FollowUp.Application.Common.Abstractions.Persistence"
                   || p.ParameterType == typeof(FollowUp.Application.Common.Abstractions.IOutbox)
             select $"{t.Name}({p.ParameterType.Name})").ToList();
        offenders.Should().BeEmpty("the read side depends on *Queries projection interfaces, never repositories or the outbox");

        var stale = ReviewedQueryHandlerRepositoryUse
            .Where(name => !Application.GetTypes().Any(t => t.Name == name))
            .ToList();
        stale.Should().BeEmpty("these reviewed exceptions no longer exist — remove them from the allowlist");
    }

    [Fact]
    public void No_public_contract_returns_IQueryable()
    {
        var offenders = new List<string>();
        foreach (var asm in new[] { Domain, Application })
            foreach (var t in asm.GetExportedTypes())
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (ReturnsQueryable(m.ReturnType))
                        offenders.Add($"{t.Name}.{m.Name}");
        offenders.Should().BeEmpty("IQueryable must never cross an assembly boundary");
    }

    private static bool ReturnsQueryable(Type t) =>
        t == typeof(IQueryable) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IQueryable<>));

    [Fact]
    public void No_generic_repository_abstraction_exists()
    {
        var offenders = new[] { Domain, Application, Infrastructure, Api }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsInterface && Regex.IsMatch(t.Name, @"^I(Generic)?(Read)?Repository(`\d+)?$"))
            .Select(t => t.FullName)
            .ToList();
        offenders.Should().BeEmpty("repositories are aggregate-oriented (ILaboratoryRepository, ...), never IRepository<T>");
    }
}
