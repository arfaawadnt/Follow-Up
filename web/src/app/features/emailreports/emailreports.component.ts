import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';
import { FilterSelectComponent } from '../../shared/filter-select.component';

interface Smtp { enabled: boolean; host: string; port: number; useSsl: boolean; fromAddress: string; user: string | null; hasPassword: boolean; }
interface RefItem { nameEn: string; }
interface City { name: string; }
interface Area { name: string; }
interface Group { nameEn: string; }
interface UserLookup { id: string; username: string; }
interface Subscription {
  id: string; name: string; includeLabStats: boolean; includeTestStats: boolean; includeAreaStats: boolean;
  filtersJson: string; userIds: string[]; emails: string[]; sendHour: number; sendMinute: number;
  windowDays: number; enabled: boolean; lastStatus: string | null; lastRunAt: string | null;
}
interface Filters { governorates: string[]; cities: string[]; areas: string[]; categories: string[]; segments: string[]; groups: string[]; }
interface Editor {
  id: string | null; name: string; includeLabStats: boolean; includeTestStats: boolean; includeAreaStats: boolean;
  filters: Filters; userIds: string[]; emailsText: string; sendHour: number; sendMinute: number; windowDays: number; enabled: boolean;
}

const EMPTY_FILTERS = (): Filters => ({ governorates: [], cities: [], areas: [], categories: [], segments: [], groups: [] });
const NEW_EDITOR = (): Editor => ({ id: null, name: '', includeLabStats: true, includeTestStats: false, includeAreaStats: false,
  filters: EMPTY_FILTERS(), userIds: [], emailsText: '', sendHour: 6, sendMinute: 0, windowDays: 1, enabled: true });

@Component({
  selector: 'app-emailreports',
  standalone: true,
  imports: [FormsModule, TranslatePipe, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'email_reports' | t : 'Email Reports' }}</div><h1>{{ 'email_reports' | t : 'Email Reports' }}</h1></div>
    </div>
    @if (msg()) { <div class="inline-banner" [class.inline-banner-error]="msgError()">{{ msg() }}</div> }

    <!-- ===== Mail Gateway ===== -->
    <div class="card" style="padding:18px;margin-bottom:18px;max-width:760px">
      <h3 style="margin:0 0 4px">{{ 'mail_gateway' | t : 'Mail Gateway (SMTP)' }}</h3>
      <div class="small muted" style="margin-bottom:14px">{{ 'mail_gateway_hint' | t : 'Configure the outgoing mail server used to send the report emails.' }}</div>
      <div class="frm-grid" style="grid-template-columns:2fr 1fr;gap:12px">
        <div class="field"><label>{{ 'smtp_host' | t : 'Host' }}</label><input class="input" [(ngModel)]="smtp.host" placeholder="smtp.example.com"></div>
        <div class="field"><label>{{ 'smtp_port' | t : 'Port' }}</label><input type="number" class="input" [(ngModel)]="smtp.port"></div>
        <div class="field"><label>{{ 'smtp_from' | t : 'From address' }}</label><input class="input" [(ngModel)]="smtp.fromAddress" placeholder="reports@megalab.local"></div>
        <div class="field"><label>{{ 'smtp_user' | t : 'Username' }}</label><input class="input" [(ngModel)]="smtp.user" autocomplete="off"></div>
        <div class="field"><label>{{ 'smtp_password' | t : 'Password' }}</label><input type="password" class="input" [(ngModel)]="smtpPassword" [placeholder]="smtp.hasPassword ? ('smtp_pw_keep' | t : 'Leave blank to keep current') : ''" autocomplete="new-password"></div>
        <div class="field" style="align-self:end;display:flex;gap:18px;align-items:center">
          <label class="chk"><input type="checkbox" [(ngModel)]="smtp.useSsl"> {{ 'smtp_ssl' | t : 'Use SSL/TLS' }}</label>
          <label class="chk"><input type="checkbox" [(ngModel)]="smtp.enabled"> {{ 'enabled' | t : 'Enabled' }}</label>
        </div>
      </div>
      <div style="display:flex;gap:10px;align-items:center;margin-top:14px;flex-wrap:wrap">
        <button class="btn btn-p" [disabled]="busy()" (click)="saveSmtp()">{{ 'save' | t : 'Save' }}</button>
        <span style="width:1px;height:24px;background:var(--slate-150,#edebe9)"></span>
        <input class="input" style="max-width:240px" [(ngModel)]="testEmail" placeholder="{{ 'smtp_test_to' | t : 'Send a test to…' }}">
        <button class="btn btn-s" [disabled]="busy() || !testEmail.trim()" (click)="sendTest()">{{ 'smtp_send_test' | t : 'Send test' }}</button>
      </div>
    </div>

    <!-- ===== Email Reports ===== -->
    <div class="card" style="padding:18px">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px">
        <h3 style="margin:0">{{ 'scheduled_reports' | t : 'Scheduled reports' }}</h3>
        <button class="btn btn-p btn-s" (click)="startNew()">＋ {{ 'new_report' | t : 'New report' }}</button>
      </div>

      @if (editing()) {
        <div class="card panel" style="padding:16px;margin-bottom:16px;background:var(--slate-50,#f8fafc)">
          <div class="frm-grid" style="grid-template-columns:2fr 1fr 1fr 1fr;gap:12px;align-items:end">
            <div class="field"><label>{{ 'report_name' | t : 'Name' }}</label><input class="input" [(ngModel)]="ed.name" placeholder="e.g. Cairo daily labs"></div>
            <div class="field"><label>{{ 'send_time' | t : 'Send time (Cairo)' }}</label>
              <div style="display:flex;gap:6px;align-items:center"><input type="number" min="0" max="23" class="input" [(ngModel)]="ed.sendHour" style="width:70px">:<input type="number" min="0" max="59" class="input" [(ngModel)]="ed.sendMinute" style="width:70px"></div></div>
            <div class="field"><label>{{ 'window_days' | t : 'Window (days)' }}</label><input type="number" min="1" max="90" class="input" [(ngModel)]="ed.windowDays"></div>
            <div class="field" style="align-self:end"><label class="chk"><input type="checkbox" [(ngModel)]="ed.enabled"> {{ 'enabled' | t : 'Enabled' }}</label></div>
          </div>

          <label class="lbl" style="margin-top:12px">{{ 'reports_included' | t : 'Reports included' }}</label>
          <div style="display:flex;gap:18px;margin-top:4px">
            <label class="chk"><input type="checkbox" [(ngModel)]="ed.includeLabStats"> {{ 'labstats' | t : 'Lab Statistics' }}</label>
            <label class="chk"><input type="checkbox" [(ngModel)]="ed.includeTestStats"> {{ 'teststats' | t : 'Test Statistics' }}</label>
            <label class="chk"><input type="checkbox" [(ngModel)]="ed.includeAreaStats"> {{ 'areastats' | t : 'Area Statistics' }}</label>
          </div>

          <label class="lbl" style="margin-top:14px">{{ 'filters_optional' | t : 'Filters (optional — leave empty for all)' }}</label>
          <div class="frm-grid" style="grid-template-columns:repeat(3,1fr);gap:10px;margin-top:4px">
            <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><app-filter-select [multiple]="true" [options]="govs()" [(ngModel)]="ed.filters.governorates"></app-filter-select></div>
            <div class="field"><label>{{ 'city' | t : 'City' }}</label><app-filter-select [multiple]="true" [options]="cities()" [(ngModel)]="ed.filters.cities"></app-filter-select></div>
            <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [multiple]="true" [options]="areas()" [(ngModel)]="ed.filters.areas"></app-filter-select></div>
            <div class="field"><label>{{ 'category' | t : 'Lab Category' }}</label><app-filter-select [multiple]="true" [options]="categories()" [(ngModel)]="ed.filters.categories"></app-filter-select></div>
            <div class="field"><label>{{ 'segment' | t : 'Segment' }}</label><app-filter-select [multiple]="true" [options]="segments()" [(ngModel)]="ed.filters.segments"></app-filter-select></div>
            <div class="field"><label>{{ 'group' | t : 'Test Group' }}</label><app-filter-select [multiple]="true" [options]="groups()" [(ngModel)]="ed.filters.groups"></app-filter-select></div>
          </div>

          <label class="lbl" style="margin-top:14px">{{ 'recipients' | t : 'Recipients' }}</label>
          <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:10px;margin-top:4px">
            <div class="field"><label>{{ 'users' | t : 'Users' }}</label><app-filter-select [multiple]="true" [options]="userOptions()" [(ngModel)]="ed.userIds"></app-filter-select></div>
            <div class="field"><label>{{ 'extra_emails' | t : 'Extra emails (comma or line separated)' }}</label><textarea class="input" rows="2" [(ngModel)]="ed.emailsText" placeholder="a@x.com, b@y.com"></textarea></div>
          </div>

          <div style="display:flex;gap:8px;margin-top:14px">
            <button class="btn btn-p" [disabled]="busy() || !ed.name.trim()" (click)="save()">{{ 'save' | t : 'Save' }}</button>
            <button class="btn btn-s" (click)="editing.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
          </div>
        </div>
      }

      <table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>{{ 'report_name' | t : 'Name' }}</th><th>{{ 'reports_included' | t : 'Reports' }}</th><th>{{ 'recipients' | t : 'Recipients' }}</th><th>{{ 'send_time' | t : 'Time' }}</th><th>{{ 'enabled' | t : 'Enabled' }}</th><th>{{ 'last_run' | t : 'Last run' }}</th><th class="r">{{ 'actions' | t : 'Actions' }}</th></tr></thead>
        <tbody>
          @for (s of subs(); track s.id) {
            <tr>
              <td style="font-weight:600">{{ s.name }}</td>
              <td>
                @if (s.includeLabStats) { <span class="badge b-info">Lab</span> }
                @if (s.includeTestStats) { <span class="badge b-info">Test</span> }
                @if (s.includeAreaStats) { <span class="badge b-info">Area</span> }
              </td>
              <td>{{ s.userIds.length + s.emails.length }}</td>
              <td class="mono">{{ pad(s.sendHour) }}:{{ pad(s.sendMinute) }}</td>
              <td>@if (s.enabled) { <span class="badge b-ok">Yes</span> } @else { <span class="badge b-neu">No</span> }</td>
              <td class="small muted">{{ s.lastStatus ?? '—' }}</td>
              <td class="r actions">
                <button class="icon-btn" title="Send now" (click)="sendNow(s)">✉</button>
                <button class="icon-btn" title="Edit" (click)="startEdit(s)">✎</button>
                <button class="icon-btn del" title="Delete" (click)="del(s)">🗑</button>
              </td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty" style="text-align:center;padding:20px">{{ 'no_records_found' | t : 'No reports yet.' }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .actions .icon-btn{margin-inline-start:4px}
    th.r,td.r{text-align:right}
    .chk{display:inline-flex;align-items:center;gap:6px;font-size:13px}
    .lbl{display:block;font-size:12px;font-weight:600;color:var(--slate-700,#605e5c)}
  `],
})
export class EmailReportsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly busy = signal(false);
  readonly msg = signal<string | null>(null);
  readonly msgError = signal(false);

  smtp: Smtp = { enabled: false, host: '', port: 587, useSsl: true, fromAddress: '', user: null, hasPassword: false };
  smtpPassword = '';
  testEmail = '';

  readonly subs = signal<Subscription[]>([]);
  readonly editing = signal(false);
  ed: Editor = NEW_EDITOR();

  // filter option lists
  readonly govs = signal<string[]>([]);
  readonly cities = signal<string[]>([]);
  readonly areas = signal<string[]>([]);
  readonly categories = signal<string[]>([]);
  readonly segments = signal<string[]>([]);
  readonly groups = signal<string[]>([]);
  readonly users = signal<UserLookup[]>([]);
  readonly userOptions = computed(() => this.users().map((u) => ({ value: u.id, label: u.username })));

  constructor() {
    this.loadSmtp();
    this.loadSubs();
    this.api.get<RefItem[]>('/setup/refs', { type: 'Governorate' }).subscribe({ next: (r) => this.govs.set(r.map((x) => x.nameEn).sort()) });
    this.api.get<City[]>('/setup/cities').subscribe({ next: (r) => this.cities.set([...new Set(r.map((x) => x.name))].sort()) });
    this.api.get<Area[]>('/setup/areas').subscribe({ next: (r) => this.areas.set([...new Set(r.map((x) => x.name))].sort()) });
    this.api.get<RefItem[]>('/setup/refs', { type: 'LabCategory' }).subscribe({ next: (r) => this.categories.set(r.map((x) => x.nameEn).sort()) });
    this.api.get<RefItem[]>('/setup/refs', { type: 'Segment' }).subscribe({ next: (r) => this.segments.set(r.map((x) => x.nameEn).sort()) });
    this.api.get<Group[]>('/test-groups').subscribe({ next: (r) => this.groups.set(r.map((x) => x.nameEn).sort()) });
    this.api.get<UserLookup[]>('/users/lookup').subscribe({ next: (u) => this.users.set(u) });
  }

  pad(n: number): string { return String(n).padStart(2, '0'); }
  private note(text: string, err = false): void { this.msgError.set(err); this.msg.set(text); }
  private run<T>(obs: Observable<T>, ok: (v: T) => void): void {
    this.busy.set(true);
    obs.subscribe({ next: (v) => { this.busy.set(false); ok(v); }, error: (e) => { this.busy.set(false); this.note(e?.error?.detail ?? 'Request failed.', true); } });
  }

  private loadSmtp(): void { this.api.get<Smtp>('/email/smtp').subscribe({ next: (s) => { this.smtp = s; } }); }
  private loadSubs(): void { this.api.get<Subscription[]>('/email/subscriptions').subscribe({ next: (s) => this.subs.set(s) }); }

  saveSmtp(): void {
    const body = { ...this.smtp, password: this.smtpPassword.trim() || null };
    this.run(this.api.post('/email/smtp', body), () => { this.smtpPassword = ''; this.note('Mail gateway saved.'); this.loadSmtp(); });
  }
  sendTest(): void { this.run(this.api.post('/email/smtp/test', { toEmail: this.testEmail.trim() }), () => this.note(`Test email sent to ${this.testEmail.trim()}.`)); }

  startNew(): void { this.ed = NEW_EDITOR(); this.editing.set(true); }
  startEdit(s: Subscription): void {
    let filters = EMPTY_FILTERS();
    try { filters = { ...EMPTY_FILTERS(), ...JSON.parse(s.filtersJson || '{}') }; } catch { /* ignore */ }
    this.ed = { id: s.id, name: s.name, includeLabStats: s.includeLabStats, includeTestStats: s.includeTestStats,
      includeAreaStats: s.includeAreaStats, filters, userIds: [...s.userIds], emailsText: s.emails.join(', '),
      sendHour: s.sendHour, sendMinute: s.sendMinute, windowDays: s.windowDays, enabled: s.enabled };
    this.editing.set(true);
  }
  save(): void {
    const emails = this.ed.emailsText.split(/[,\n;]+/).map((e) => e.trim()).filter(Boolean);
    const body = {
      name: this.ed.name.trim(), includeLabStats: this.ed.includeLabStats, includeTestStats: this.ed.includeTestStats,
      includeAreaStats: this.ed.includeAreaStats, filtersJson: JSON.stringify(this.ed.filters), userIds: this.ed.userIds,
      emails, sendHour: +this.ed.sendHour, sendMinute: +this.ed.sendMinute, windowDays: +this.ed.windowDays, enabled: this.ed.enabled,
    };
    const req = this.ed.id ? this.api.put(`/email/subscriptions/${this.ed.id}`, body) : this.api.post('/email/subscriptions', body);
    this.run(req, () => { this.editing.set(false); this.note('Report saved.'); this.loadSubs(); });
  }
  del(s: Subscription): void { if (confirm(`Delete "${s.name}"?`)) this.run(this.api.delete(`/email/subscriptions/${s.id}`), () => { this.note('Report deleted.'); this.loadSubs(); }); }
  sendNow(s: Subscription): void {
    this.run(this.api.post<{ sent: boolean; recipients: number; failures: number; status: string }>(`/email/subscriptions/${s.id}/send-now`, {}),
      (r) => { this.note(`"${s.name}": ${r.status}.`, r.failures > 0 && r.recipients === 0); this.loadSubs(); });
  }
}
