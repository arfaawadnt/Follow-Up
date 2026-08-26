import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface TestGroup { id: string; code: string; nameEn: string; }
interface TestSetup { id: string; code: string; nameEn: string; nameAr: string | null; groupId: string | null; }

@Component({
  selector: 'app-testsetup',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'testsetup' | t : 'Test setup' }}</div><h1>{{ 'testsetup' | t : 'Test setup' }}</h1></div></div>
    @if (banner()) { <div class="inline-banner inline-banner-error">{{ banner() }}</div> }

    <div class="grid" style="grid-template-columns:1fr 2fr;gap:16px;align-items:start">
      @if (auth.has('AddTestsetup') || editId()) {
        <div class="card" style="padding:20px">
          <h3 style="margin:0 0 15px;font-size:14px;font-weight:600;color:var(--slate-800)">{{ editId() ? ('edit_test' | t : 'Edit test') : ('add_new_test' | t : 'Add test') }}</h3>
          <div class="field"><label>{{ 'test_code' | t : 'Test code' }}</label><input class="input" [(ngModel)]="code" [disabled]="!!editId()" placeholder="e.g. GLU"></div>
          <div class="field" style="margin-top:10px"><label>{{ 'test_name' | t : 'Test name' }}</label><input class="input" [(ngModel)]="name" placeholder="e.g. Glucose"></div>
          <div class="field" style="margin-top:10px"><label>{{ 'parent_group' | t : 'Parent group' }}</label>
            <select class="select" [(ngModel)]="groupId"><option [ngValue]="null">{{ 'select_group_placeholder' | t : '-- Select Group --' }}</option>@for (g of groups(); track g.id) { <option [ngValue]="g.id">{{ g.nameEn }} ({{ g.code }})</option> }</select></div>
          <div style="display:flex;gap:8px;margin-top:15px">
            <button class="btn btn-p" [disabled]="busy() || !code || !name" (click)="save()">{{ 'save_2' | t : 'Save' }}</button>
            @if (editId()) { <button class="btn btn-s" (click)="reset()">{{ 'cancel_2' | t : 'Cancel' }}</button> }
          </div>
        </div>
      }
      <div class="card" style="padding:0;overflow:hidden">
        @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          <table class="grid-table" style="margin:0;border:none">
            <thead><tr><th>{{ 'test_code_2' | t : 'Test Code' }}</th><th>{{ 'test_name_2' | t : 'Test Name' }}</th><th>{{ 'parent_group' | t : 'Parent Group' }}</th><th style="width:130px"></th></tr></thead>
            <tbody>
              @for (s of setups(); track s.id) {
                <tr><td class="mono">{{ s.code }}</td><td>{{ s.nameEn }}</td><td>{{ groupName(s.groupId) }}</td>
                  <td class="actions">
                    @if (auth.has('UpdateTestsetup')) { <button class="btn-ghost" (click)="edit(s)">{{ 'edit_2' | t : 'Edit' }}</button> }
                    @if (auth.has('DeleteTestsetup')) { <button class="btn-ghost red" (click)="del(s)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button> }
                  </td></tr>
              } @empty { <tr><td colspan="4" class="empty" style="text-align:center;padding:24px">—</td></tr> }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}`],
})
export class TestSetupComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly groups = signal<TestGroup[]>([]);
  readonly setups = signal<TestSetup[]>([]);
  readonly editId = signal<string | null>(null);
  readonly banner = signal<string | null>(null);
  code = ''; name = ''; groupId: string | null = null;

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<TestGroup[]>('/test-groups').subscribe({ next: (g) => this.groups.set(g) });
    this.api.get<TestSetup[]>('/test-setups').subscribe({ next: (s) => { this.setups.set(s); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  groupName(id: string | null): string { return id ? (this.groups().find((g) => g.id === id)?.code ?? '—') : '—'; }
  edit(s: TestSetup): void { this.editId.set(s.id); this.code = s.code; this.name = s.nameEn; this.groupId = s.groupId; }
  reset(): void { this.editId.set(null); this.code = ''; this.name = ''; this.groupId = null; this.banner.set(null); }
  save(): void {
    this.busy.set(true); this.banner.set(null);
    const id = this.editId();
    const obs = id
      ? this.api.put(`/test-setups/${id}`, { id, nameEn: this.name, nameAr: null, groupId: this.groupId })
      : this.api.post('/test-setups', { code: this.code, nameEn: this.name, nameAr: null, groupId: this.groupId });
    obs.subscribe({ next: () => { this.busy.set(false); this.reset(); this.load(); }, error: (e) => { this.busy.set(false); this.banner.set(e?.error?.detail ?? 'Save failed.'); } });
  }
  del(s: TestSetup): void {
    if (!window.confirm(`Delete test ${s.code}?`)) return;
    this.busy.set(true);
    this.api.delete(`/test-setups/${s.id}`).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: (e) => { this.busy.set(false); this.banner.set(e?.error?.detail ?? 'Delete failed.'); } });
  }
}
