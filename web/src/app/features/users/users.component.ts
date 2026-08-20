import { Component, inject, signal } from '@angular/core';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, UserListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface Role { id: string; name: string; privileges: string[]; isBuiltIn: boolean; }

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'users' | t : 'Users' }}</div><h1>{{ 'users' | t : 'Users' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('ManageUsers')) { <button class="btn btn-p" (click)="toggle()">{{ showForm() ? 'Close' : ('new_user' | t : 'New user') }}</button> }</div>
    </div>
    @if (banner()) { <div class="inline-banner" [class.inline-banner-error]="bannerError()">{{ banner() }}</div> }

    @if (showForm()) {
      <form class="card" style="padding:20px;margin-bottom:20px" [formGroup]="form" (ngSubmit)="create()">
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'username' | t : 'Username' }}</label><input class="input" formControlName="username"></div>
          <div class="field"><label>{{ 'password' | t : 'Password' }}</label><input class="input" type="password" formControlName="password"></div>
          <div class="field"><label>{{ 'role' | t : 'Role' }}</label><select class="select" formControlName="roleId"><option value="">—</option>@for (r of roles(); track r.id) { <option [value]="r.id">{{ r.name }}</option> }</select></div>
          <div class="field"><label>Email</label><input class="input" formControlName="email"></div>
        </div>
        <div style="margin-top:12px"><button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">{{ 'save' | t : 'Create' }}</button></div>
      </form>
    }

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'username' | t : 'Username' }}</th><th>{{ 'role' | t : 'Role' }}</th><th>Email</th><th>{{ 'status' | t }}</th><th>{{ 'actions_3' | t : 'Actions' }}</th></tr></thead>
          <tbody>
            @for (u of users(); track u.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ u.username }}</b></td>
                <td>
                  @if (auth.has('ManageUsers')) {
                    <select class="select" style="padding:3px 6px;font-size:12px" [ngModel]="roleIdOf(u)" (ngModelChange)="changeRole(u, $event)">
                      @for (r of roles(); track r.id) { <option [value]="r.id">{{ r.name }}</option> }
                    </select>
                  } @else { {{ u.roleName }} }
                </td>
                <td>{{ u.email ?? '—' }}</td>
                <td>
                  <span class="badge" [class]="u.isActive ? 'b-ok' : 'b-neu'">{{ u.isActive ? 'Active' : 'Inactive' }}</span>
                  @if (u.isLocked) { <span class="badge b-bad">Locked</span> }
                </td>
                <td class="actions">
                  @if (auth.has('ManageUsers')) {
                    @if (u.isLocked) { <button class="btn btn-mini btn-p" (click)="unlock(u)" [disabled]="busy()">Unlock</button> }
                    <button class="btn btn-mini btn-d" (click)="del(u)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="5" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}`],
})
export class UsersComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly users = signal<UserListItem[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly showForm = signal(false);
  readonly banner = signal<string | null>(null);
  readonly bannerError = signal(false);

  readonly form = this.fb.group({
    username: this.fb.control('', Validators.required),
    password: this.fb.control('', [Validators.required, Validators.minLength(8)]),
    roleId: this.fb.control('', Validators.required),
    email: this.fb.control(''),
  });

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<RoleArr>('/setup/roles').subscribe({ next: (r) => this.roles.set(r as Role[]) });
    this.api.get<PagedResult<UserListItem>>('/users', { pageSize: 200 }).subscribe({
      next: (r) => { this.users.set(r.items); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  roleIdOf(u: UserListItem): string { return this.roles().find((r) => r.name === u.roleName)?.id ?? ''; }
  toggle(): void { this.showForm.set(!this.showForm()); this.banner.set(null); }

  create(): void {
    if (this.form.invalid) return;
    this.busy.set(true); this.banner.set(null);
    const v = this.form.getRawValue();
    this.api.post('/users', { username: v.username, password: v.password, roleId: v.roleId, email: v.email || null }).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.form.reset(); this.set('User created.', false); this.load(); },
      error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Create failed.', true); },
    });
  }
  changeRole(u: UserListItem, roleId: string): void {
    this.busy.set(true);
    this.api.put(`/users/${u.id}`, { id: u.id, roleId, email: u.email, language: 'en' }).subscribe({
      next: () => { this.busy.set(false); this.set('Role updated.', false); this.load(); }, error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Update failed.', true); },
    });
  }
  unlock(u: UserListItem): void {
    this.busy.set(true);
    this.api.post(`/users/${u.id}/unlock`, {}).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
  del(u: UserListItem): void {
    if (!window.confirm(`Delete user ${u.username}?`)) return;
    this.busy.set(true);
    this.api.delete(`/users/${u.id}`).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Delete failed.', true); } });
  }
  private set(msg: string, err: boolean): void { this.banner.set(msg); this.bannerError.set(err); }
}

type RoleArr = Role[];
