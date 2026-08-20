import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface Role { id: string; name: string; privileges: string[]; defaultLanguage: string; defaultTheme: string; isBuiltIn: boolean; }

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'roles' | t : 'Roles' }}</div><h1>{{ 'roles' | t : 'Roles' }}</h1></div></div>
    @if (banner()) { <div class="inline-banner" [class.inline-banner-error]="bannerError()">{{ banner() }}</div> }

    <div class="grid" style="grid-template-columns:1fr 3fr;gap:16px;align-items:start">
      <div class="card" style="padding:16px">
        <h3 style="margin:0 0 10px;font-size:13px;font-weight:700;color:var(--slate-600);text-transform:uppercase">{{ 'roles' | t : 'Roles' }}</h3>
        @for (r of roles(); track r.id) {
          <div class="rolerow" [class.on]="selected()?.id === r.id" (click)="select(r)">
            <span>{{ r.name }}</span>@if (r.isBuiltIn) { <span class="badge b-neu">built-in</span> }
          </div>
        }
        @if (auth.has('ManageUsers')) {
          <div style="margin-top:12px;display:flex;gap:6px">
            <input class="input" [(ngModel)]="newName" placeholder="New role name" style="flex:1">
            <button class="btn btn-mini btn-p" [disabled]="!newName || busy()" (click)="createRole()">Add</button>
          </div>
        }
      </div>

      <div class="card" style="padding:20px">
        @if (selected(); as r) {
          <div class="hrow" style="margin-bottom:12px"><h3 style="margin:0;font-size:16px;font-weight:700;color:var(--slate-900);flex:1">{{ r.name }}</h3>
            @if (auth.has('ManageUsers') && !r.isBuiltIn) {
              <button class="btn btn-mini btn-p" [disabled]="busy()" (click)="savePrivs()">{{ 'save' | t : 'Save' }}</button>
              <button class="btn btn-mini btn-d" [disabled]="busy()" (click)="delRole(r)">{{ 'delete' | t : 'Delete' }}</button>
            }
          </div>
          <div class="privgrid">
            @for (p of allPrivileges(); track p) {
              <label class="privitem" [class.dim]="r.isBuiltIn">
                <input type="checkbox" [checked]="draft().has(p)" [disabled]="r.isBuiltIn || !auth.has('ManageUsers')" (change)="togglePriv(p, $event)">
                <span>{{ ('priv_' + p.toLowerCase()) | t : p }}</span>
              </label>
            }
          </div>
          @if (r.isBuiltIn) { <p class="muted small" style="margin-top:12px">Built-in roles cannot be edited.</p> }
        } @else { <div class="empty">{{ 'select_role' | t : 'Select a role.' }}</div> }
      </div>
    </div>
  `,
  styles: [`
    .rolerow{padding:8px 10px;border-radius:var(--r-md);cursor:pointer;display:flex;gap:8px;align-items:center;font-size:13px}
    .rolerow:hover{background:var(--slate-100)} .rolerow.on{background:var(--primary-blue-light);color:var(--primary-blue);font-weight:600}
    .rolerow .badge{margin-inline-start:auto}
    .privgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:8px 16px}
    .privitem{display:flex;gap:8px;align-items:center;font-size:12.5px;color:var(--slate-800)} .privitem.dim{opacity:.7}
    .btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}
  `],
})
export class RolesComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly busy = signal(false);
  readonly roles = signal<Role[]>([]);
  readonly selected = signal<Role | null>(null);
  readonly draft = signal<Set<string>>(new Set());
  readonly banner = signal<string | null>(null);
  readonly bannerError = signal(false);
  newName = '';

  readonly allPrivileges = computed(() => {
    const set = new Set<string>();
    for (const r of this.roles()) for (const p of r.privileges) set.add(p);
    return [...set].sort();
  });

  constructor() { this.load(); }

  load(): void {
    this.api.get<Role[]>('/setup/roles').subscribe({ next: (r) => { this.roles.set(r); if (r.length && !this.selected()) this.select(r[0]); } });
  }
  select(r: Role): void { this.selected.set(r); this.draft.set(new Set(r.privileges)); }
  togglePriv(p: string, e: Event): void { const on = (e.target as HTMLInputElement).checked; this.draft.update((s) => { const n = new Set(s); on ? n.add(p) : n.delete(p); return n; }); }

  savePrivs(): void {
    const r = this.selected(); if (!r) return;
    this.busy.set(true); this.banner.set(null);
    this.api.put(`/setup/roles/${r.id}`, { id: r.id, name: r.name, privileges: [...this.draft()], defaultLanguage: r.defaultLanguage, defaultTheme: r.defaultTheme }).subscribe({
      next: () => { this.busy.set(false); this.set('Role saved.', false); this.load(); }, error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Save failed.', true); },
    });
  }
  createRole(): void {
    this.busy.set(true); this.banner.set(null);
    this.api.post('/setup/roles', { name: this.newName, privileges: [], defaultLanguage: 'en', defaultTheme: 'light' }).subscribe({
      next: () => { this.busy.set(false); this.newName = ''; this.set('Role created.', false); this.load(); }, error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Create failed.', true); },
    });
  }
  delRole(r: Role): void {
    if (!window.confirm(`Delete role ${r.name}?`)) return;
    this.busy.set(true);
    this.api.delete(`/setup/roles/${r.id}`).subscribe({ next: () => { this.busy.set(false); this.selected.set(null); this.load(); }, error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Delete failed.', true); } });
  }
  private set(msg: string, err: boolean): void { this.banner.set(msg); this.bannerError.set(err); }
}
