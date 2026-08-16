import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ReceivingItem } from '../../core/models';

@Component({
  selector: 'app-labcheckin',
  standalone: true,
  imports: [DatePipe],
  template: `
    <h1 class="display page-title">Lab Check-in</h1>
    <p class="lede">Transferred samples awaiting confirmation of receipt at the laboratory.</p>

    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">Loading…</div> }
      @if (!loading()) {
        <table class="app">
          <thead><tr><th>Lab</th><th>Name</th><th>Visit date</th><th>Samples</th><th></th></tr></thead>
          <tbody>
            @for (r of items(); track r.visitId) {
              <tr>
                <td class="client-code mono">{{ r.labDisplayCode }}</td>
                <td>{{ r.labName }}</td>
                <td>{{ r.visitDate | date:'mediumDate' }}</td>
                <td class="mono">{{ r.sampleCount ?? '—' }}</td>
                <td class="actions">
                  @if (auth.has('ConfirmTransfers')) {
                    <button class="btn btn-mini btn-p" (click)="confirm(r)" [disabled]="busy()">Confirm receipt</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="5" class="empty">Nothing awaiting receipt.</td></tr> }
          </tbody>
        </table>
      }
    </div></div>
  `,
  styles: [`
    .page-title { font-size:22px; margin:0 0 4px; } .lede { color:var(--slate-500); font-size:13px; margin:0 0 16px; }
    .empty { color:var(--slate-500); text-align:center; padding:24px; }
    .actions { display:flex; gap:6px; } .btn-mini { padding:4px 10px; font-size:11.5px; border-radius:var(--r-btn); }
  `],
})
export class LabCheckInComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<ReceivingItem[]>([]);

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<ReceivingItem[]>('/labcheckin').subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  confirm(r: ReceivingItem): void {
    this.busy.set(true);
    this.api.post('/labcheckin/confirm', { visitId: r.visitId }).subscribe({
      next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false),
    });
  }
}
