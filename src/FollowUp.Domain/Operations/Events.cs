using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Operations;

/// <summary>Raised on check-in — may auto-create an outsource row and derive lab status (Workflows §2.1).</summary>
public sealed record VisitCheckedIn(DailyVisitId VisitId, LaboratoryId LaboratoryId, int SampleCount) : DomainEvent;

/// <summary>Raised when a visit is marked missed — queues missed-visit notifications (FR-5).</summary>
public sealed record VisitMissed(DailyVisitId VisitId, LaboratoryId LaboratoryId) : DomainEvent;

/// <summary>Raised when samples are received at the laboratory (FR-7) — derives lab status.</summary>
public sealed record VisitReceived(DailyVisitId VisitId, LaboratoryId LaboratoryId) : DomainEvent;
