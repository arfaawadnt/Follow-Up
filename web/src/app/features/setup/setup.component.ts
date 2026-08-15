import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { RefItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-setup',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="head">
      <h1 class="display page-title">{{ 'setup.title' | t }}</h1>
      <select [(ngModel)]="type" (change)="load()">
        @for (t of types; track t) { <option [value]="t">{{ t }}</option> }
      </select>
    </div>
    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">{{ 'common.loading' | t }}</div> }
      <table class="app">
        <thead><tr><th>{{ 'labs.code' | t }}</th><th>English</th><th>العربية</th></tr></thead>
        <tbody>
          @for (r of items(); track r.id) {
            <tr><td class="mono">{{ r.code }}</td><td>{{ r.nameEn }}</td><td dir="rtl">{{ r.nameAr ?? '—' }}</td></tr>
          } @empty { <tr><td colspan="3" class="empty">{{ 'common.empty' | t }}</td></tr> }
        </tbody>
      </table>
    </div></div>
  `,
  styles: [`
    .head{display:flex;justify-content:space-between;align-items:center;margin-bottom:16px}.page-title{font-size:22px;margin:0}
    select{border:1px solid var(--slate-300);border-radius:var(--r-input);padding:8px 12px;background:var(--white);color:var(--slate-900)}
    .empty{color:var(--slate-500);text-align:center;padding:24px}
  `],
})
export class SetupComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly items = signal<RefItem[]>([]);
  readonly types = ['Governorate', 'Branch', 'MarketingPurpose', 'ComplaintCategory', 'Team', 'Channel', 'Payer', 'ContractType', 'LabCategory'];
  type = 'Governorate';
  constructor() { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.get<RefItem[]>('/refs', { type: this.type }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
}
