import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { BoardItem } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-daily',
  standalone: true,
  imports: [FormsModule, StatusBadgePipe, TranslatePipe],
  template: `
    <div class="head">
      <h1 class="display page-title">{{ 'daily.title' | t }}</h1>
      <input type="date" [(ngModel)]="date" (change)="load()">
    </div>

    @if (loading()) { <div class="dcard"><div class="cbody">{{ 'common.loading' | t }}</div></div> }
    @if (!loading()) {
      <div class="dcard"><div class="cbody" style="padding:0">
        <table class="app">
          <thead><tr><th>{{ 'labs.code' | t }}</th><th>{{ 'labs.name' | t }}</th><th>{{ 'daily.time' | t }}</th>
            <th>{{ 'daily.samples' | t }}</th><th>{{ 'labs.status' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (v of board(); track v.visitId) {
              <tr>
                <td class="client-code mono">{{ v.labDisplayCode }}</td>
                <td>{{ v.labName }}</td>
                <td class="mono">{{ v.scheduledTime }}</td>
                <td class="mono">{{ v.sampleCount ?? '—' }}{{ v.adminChecked ? ' ✓' : '' }}</td>
                <td><span class="badge" [class]="v.status | statusBadge">{{ v.status }}</span></td>
                <td class="actions">
                  @if (v.status === 'Pending') {
                    <input class="num" type="number" min="0" [(ngModel)]="counts[v.visitId]" placeholder="#">
                    <button class="btn btn-mini btn-p" (click)="checkin(v)" [disabled]="busy()">{{ 'action.checkin' | t }}</button>
                    <button class="btn btn-mini btn-s" (click)="act(v, 'miss')" [disabled]="busy()">{{ 'action.miss' | t }}</button>
                  }
                  @if (v.status === 'Received' && !v.adminChecked && auth.has('VerifyDailyFollowup')) {
                    <button class="btn btn-mini btn-t" (click)="verify(v)" [disabled]="busy()">{{ 'action.verify' | t }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty">{{ 'common.empty' | t }}</td></tr> }
          </tbody>
        </table>
      </div></div>
    }
  `,
  styles: [`
    .head { display:flex; justify-content:space-between; align-items:center; margin-bottom:16px; }
    .page-title { font-size:22px; margin:0; }
    .actions { display:flex; gap:6px; align-items:center; }
    .num { width:64px; padding:4px 6px; border:1px solid var(--slate-300); border-radius:var(--r-btn); background:var(--white); color:var(--slate-900); }
    .btn-mini { padding:4px 10px; font-size:11.5px; border-radius:var(--r-btn); }
    input[type=date] { border:1px solid var(--slate-300); border-radius:var(--r-input); padding:7px 10px; background:var(--white); color:var(--slate-900); }
    .empty { color:var(--slate-500); text-align:center; padding:24px; }
  `],
})
export class DailyComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly board = signal<BoardItem[]>([]);
  counts: Record<string, number> = {};
  date = new Date().toISOString().slice(0, 10);

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<BoardItem[]>('/daily', { date: this.date }).subscribe({
      next: (b) => { this.board.set(b); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  checkin(v: BoardItem): void {
    this.run(this.api.post(`/daily/${v.visitId}/checkin`, { sampleCount: this.counts[v.visitId] ?? 0 }));
  }
  act(v: BoardItem, action: 'miss' | 'undo'): void { this.run(this.api.post(`/daily/${v.visitId}/${action}`)); }
  verify(v: BoardItem): void { this.run(this.api.post(`/daily/${v.visitId}/verify`, { verified: true })); }

  private run(obs: { subscribe: Function }): void {
    this.busy.set(true);
    (obs as any).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
}
