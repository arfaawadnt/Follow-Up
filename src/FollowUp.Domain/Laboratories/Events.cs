using FollowUp.Domain.Common;

namespace FollowUp.Domain.Laboratories;

/// <summary>Raised when a new lab is onboarded — the board scheduler reacts to schedule it intra-day (BR-3).</summary>
public sealed record LaboratoryRegistered(LaboratoryId LaboratoryId, string Code) : DomainEvent;

/// <summary>Raised when a lab's schedule changes so the current board can be reconciled (BR-3).</summary>
public sealed record LaboratoryScheduleChanged(LaboratoryId LaboratoryId) : DomainEvent;

/// <summary>Raised when a lab's status is derived/changed (BR-5) — feeds insights and notifications.</summary>
public sealed record LaboratoryStatusChanged(LaboratoryId LaboratoryId, string From, string To) : DomainEvent;

/// <summary>Raised when a lab moves between areas — the derived area/day sample-tracking totals for its
/// received visits are refreshed for both areas (FR-8), covering receipts confirmed before the lab had an area.</summary>
public sealed record LaboratoryAreaChanged(LaboratoryId LaboratoryId, string? OldArea, string? NewArea) : DomainEvent;
