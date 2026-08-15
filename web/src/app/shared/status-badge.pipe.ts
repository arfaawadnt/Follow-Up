import { Pipe, PipeTransform } from '@angular/core';

/**
 * Maps a domain status to a badge class (design-system domain-state mapping):
 * ok = Active/Received/Completed/Resolved · info = InProgress/Sent/Scheduled ·
 * warn = Pending/Suspended · bad = Missed/Stopped/Churned/Open · neu = Inactive/New · pur = Scanned.
 */
@Pipe({ name: 'statusBadge', standalone: true })
export class StatusBadgePipe implements PipeTransform {
  private static readonly map: Record<string, string> = {
    Active: 'b-ok', Received: 'b-ok', Completed: 'b-ok', Resolved: 'b-ok',
    InProgress: 'b-info', Sent: 'b-info', Scheduled: 'b-info', Visited: 'b-info',
    Pending: 'b-warn', Suspended: 'b-warn',
    Missed: 'b-bad', Stopped: 'b-bad', Churned: 'b-bad', Open: 'b-bad', Cancelled: 'b-bad',
    Inactive: 'b-neu', New: 'b-neu', Collected: 'b-neu',
    Scanned: 'b-pur',
  };

  transform(status: string | null | undefined): string {
    return (status && StatusBadgePipe.map[status]) || 'b-neu';
  }
}
