using FollowUp.Domain.Common;

namespace FollowUp.Domain.Laboratories;

/// <summary>Raised when a new lab is onboarded — the board scheduler reacts to schedule it intra-day (BR-3).</summary>
public sealed record LaboratoryRegistered(LaboratoryId LaboratoryId, string Code) : DomainEvent;

/// <summary>Raised when a lab's schedule changes so the current board can be reconciled (BR-3).</summary>
public sealed record LaboratoryScheduleChanged(LaboratoryId LaboratoryId) : DomainEvent;

/// <summary>Raised when a lab's status is derived/changed (BR-5) — feeds insights and notifications.</summary>
public sealed record LaboratoryStatusChanged(LaboratoryId LaboratoryId, string From, string To) : DomainEvent;
