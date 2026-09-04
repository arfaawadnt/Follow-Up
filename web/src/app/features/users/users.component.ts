import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { PagedResult, UserListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface Role { id: string; name: string; privileges: string[]; isBuiltIn: boolean; }

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'users' | t : 'Users' }}</div><h1>{{ 'users' | t : 'Users' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('ManageUsers')) { <button class="btn btn-p" (click)="toggle()">{{ showForm() ? 'Close' : ('new_user_btn' | t : '+ New User') }}</button> }</div>
    </div>

    @if (showForm()) {
      <form class="card" style="padding:20px;margin-bottom:20px" [formGroup]="form" (ngSubmit)="save()">
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'username' | t : 'Username' }}</label><input class="input" formControlName="username"></div>
          @if (!editingId()) {
            <div class="field"><label>{{ 'password' | t : 'Password' }}</label><input class="input" type="password" formControlName="password"></div>
          }
          <div class="field"><label>{{ 'role' | t : 'Role' }}</label><select class="select" formControlName="roleId"><option value="">—</option>@for (r of roles(); track r.id) { <option [value]="r.id">{{ r.name }}</option> }</select></div>
          <div class="field"><label>Email</label><input class="input" formControlName="email"></div>
          <div class="field"><label>{{ 'display_name' | t : 'Display Name' }}</label><input class="input" formControlName="displayName"></div>
          <div class="field"><label>{{ 'language' | t : 'Language' }}</label><select class="select" formControlName="language"><option value="en">English</option><option value="ar">العربية</option></select></div>
        </div>
        <div style="margin-top:12px"><button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">{{ editingId() ? ('update' | t : 'Update') : ('save' | t : 'Create') }}</button></div>
      </form>
    }

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'username' | t : 'Username' }}</th><th>{{ 'display_name' | t : 'Display Name' }}</th><th>{{ 'role' | t : 'Role' }}</th><th>{{ 'language' | t : 'Language' }}</th><th>{{ 'privileges' | t : 'Privileges' }}</th><th></th></tr></thead>
          <tbody>
            @for (u of users(); track u.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ u.username }}</b></td>
                <td>{{ u.displayName ?? '—' }}</td>
                <td>{{ u.roleName }}</td>
                <td>{{ langLabel(u.language) }}</td>
                <td>{{ u.privilegeCount }} {{ 'privileges_2' | t : 'privileges' }}</td>
                <td class="actions">
                  @if (auth.has('ManageUsers')) {
                    <button class="btn-ghost" (click)="edit(u)" [disabled]="busy()">{{ 'edit' | t : 'Edit' }}</button>
                    @if (u.isLocked) { <button class="btn btn-mini btn-p" (click)="unlock(u)" [disabled]="busy()">Unlock</button> }
                    <button class="btn-ghost red" (click)="del(u)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px;justify-content:flex-end}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}`],
})
export class UsersComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly users = signal<UserListItem[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly showForm = signal(false);
  readonly editingId = signal<string | null>(null);

  readonly form = this.fb.group({
    username: this.fb.control('', Validators.required),
    password: this.fb.control('', [Validators.required, Validators.minLength(8)]),
    roleId: this.fb.control('', Validators.required),
    email: this.fb.control(''),
    displayName: this.fb.control(''),
    language: this.fb.control('en'),
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
  langLabel(code: string): string { return code === 'ar' ? 'العربية' : 'English'; }

  toggle(): void {
    if (this.showForm()) { this.closeForm(); return; }
    this.editingId.set(null);
    this.form.reset({ username: '', password: '', roleId: '', email: '', displayName: '', language: 'en' });
    this.form.controls.username.enable();
    this.form.controls.password.setValidators([Validators.required, Validators.minLength(8)]);
    this.form.controls.password.updateValueAndValidity();
    this.showForm.set(true);
  }

  edit(u: UserListItem): void {
    this.editingId.set(u.id);
    this.form.reset({ username: u.username, password: '', roleId: this.roleIdOf(u), email: u.email ?? '', displayName: u.displayName ?? '', language: u.language || 'en' });
    this.form.controls.username.disable();
    this.form.controls.password.clearValidators();
    this.form.controls.password.updateValueAndValidity();
    this.showForm.set(true);
  }

  save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    const v = this.form.getRawValue();
    const id = this.editingId();
    if (id) {
      this.api.put(`/users/${id}`, { roleId: v.roleId, email: v.email || null, displayName: v.displayName || null, language: v.language }).subscribe({
        next: () => { this.busy.set(false); this.closeForm(); this.toast.success('User updated.'); this.load(); },
        error: () => { this.busy.set(false); },
      });
    } else {
      this.api.post('/users', { username: v.username, password: v.password, roleId: v.roleId, email: v.email || null, displayName: v.displayName || null, language: v.language }).subscribe({
        next: () => { this.busy.set(false); this.closeForm(); this.toast.success('User created.'); this.load(); },
        error: () => { this.busy.set(false); },
      });
    }
  }
  unlock(u: UserListItem): void {
    this.busy.set(true);
    this.api.post(`/users/${u.id}/unlock`, {}).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
  del(u: UserListItem): void {
    if (!window.confirm(`Delete user ${u.username}?`)) return;
    this.busy.set(true);
    this.api.delete(`/users/${u.id}`).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => { this.busy.set(false); } });
  }
  private closeForm(): void { this.showForm.set(false); this.editingId.set(null); this.form.reset(); }
}

type RoleArr = Role[];
