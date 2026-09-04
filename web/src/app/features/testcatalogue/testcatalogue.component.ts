import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';

interface TestGroup { id: string; code: string; nameEn: string; nameAr: string | null; }
interface TestSetup { id: string; code: string; nameEn: string; nameAr: string | null; groupId: string | null; }

@Component({
  selector: 'app-testcatalogue',
  standalone: true,
  imports: [FormsModule],
  template: `
    <h1 class="display page-title">Test Catalogue</h1>

    <div class="grid">
      <div class="dcard"><div class="cbody">
        <h3 class="sec">Test groups</h3>
        @if (auth.has('AddGroups')) {
          <div class="addrow">
            <input placeholder="Code" [(ngModel)]="newGroup.code" class="in code">
            <input placeholder="Name (EN)" [(ngModel)]="newGroup.nameEn" class="in">
            <input placeholder="Name (AR)" [(ngModel)]="newGroup.nameAr" class="in">
            <button class="btn btn-mini btn-p" [disabled]="!newGroup.code || !newGroup.nameEn || busy()" (click)="addGroup()">Add</button>
          </div>
        }
        <table class="app">
          <thead><tr><th>Code</th><th>Name (EN)</th><th>Name (AR)</th><th></th></tr></thead>
          <tbody>
            @for (g of groups(); track g.id) {
              <tr>
                <td class="mono">{{ g.code }}</td>
                <td>@if (editG() === g.id) { <input class="in" [(ngModel)]="draftG.nameEn"> } @else { {{ g.nameEn }} }</td>
                <td>@if (editG() === g.id) { <input class="in" [(ngModel)]="draftG.nameAr"> } @else { {{ g.nameAr ?? '—' }} }</td>
                <td class="actions">
                  @if (editG() === g.id) {
                    <button class="btn btn-mini btn-p" (click)="saveGroup(g)" [disabled]="busy()">Save</button>
                    <button class="btn btn-mini btn-s" (click)="editG.set(null)">Cancel</button>
                  } @else {
                    @if (auth.has('UpdateGroups')) { <button class="btn btn-mini btn-s" (click)="startEditG(g)">Edit</button> }
                    @if (auth.has('DeleteGroups')) { <button class="btn btn-mini btn-d" (click)="delGroup(g)" [disabled]="busy()">Delete</button> }
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="4" class="empty">No groups.</td></tr> }
          </tbody>
        </table>
      </div></div>

      <div class="dcard"><div class="cbody">
        <h3 class="sec">Test setups</h3>
        @if (auth.has('AddTestsetup')) {
          <div class="addrow">
            <input placeholder="Code" [(ngModel)]="newSetup.code" class="in code">
            <input placeholder="Name (EN)" [(ngModel)]="newSetup.nameEn" class="in">
            <select [(ngModel)]="newSetup.groupId" class="in">
              <option [ngValue]="null">— no group —</option>
              @for (g of groups(); track g.id) { <option [ngValue]="g.id">{{ g.code }}</option> }
            </select>
            <button class="btn btn-mini btn-p" [disabled]="!newSetup.code || !newSetup.nameEn || busy()" (click)="addSetup()">Add</button>
          </div>
        }
        <table class="app">
          <thead><tr><th>Code</th><th>Name (EN)</th><th>Group</th><th></th></tr></thead>
          <tbody>
            @for (s of setups(); track s.id) {
              <tr>
                <td class="mono">{{ s.code }}</td>
                <td>@if (editS() === s.id) { <input class="in" [(ngModel)]="draftS.nameEn"> } @else { {{ s.nameEn }} }</td>
                <td>
                  @if (editS() === s.id) {
                    <select class="in" [(ngModel)]="draftS.groupId"><option [ngValue]="null">—</option>
                      @for (g of groups(); track g.id) { <option [ngValue]="g.id">{{ g.code }}</option> }</select>
                  } @else { {{ groupCode(s.groupId) }} }
                </td>
                <td class="actions">
                  @if (editS() === s.id) {
                    <button class="btn btn-mini btn-p" (click)="saveSetup(s)" [disabled]="busy()">Save</button>
                    <button class="btn btn-mini btn-s" (click)="editS.set(null)">Cancel</button>
                  } @else {
                    @if (auth.has('UpdateTestsetup')) { <button class="btn btn-mini btn-s" (click)="startEditS(s)">Edit</button> }
                    @if (auth.has('DeleteTestsetup')) { <button class="btn btn-mini btn-d" (click)="delSetup(s)" [disabled]="busy()">Delete</button> }
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="4" class="empty">No setups.</td></tr> }
          </tbody>
        </table>
      </div></div>
    </div>
  `,
  styles: [`
    .page-title { font-size:22px; margin:0 0 16px; }
    .grid { display:grid; grid-template-columns:1fr 1fr; gap:16px; }
    @media (max-width: 980px) { .grid { grid-template-columns:1fr; } }
    .sec { font:700 12px var(--ui); text-transform:uppercase; letter-spacing:.04em; color:var(--slate-500); margin:0 0 12px; }
    .addrow { display:flex; gap:8px; margin-bottom:12px; flex-wrap:wrap; }
    .in { border:1px solid var(--slate-300); border-radius:var(--r-input); padding:6px 9px; font-size:12.5px; background:var(--white); color:var(--slate-900); flex:1; min-width:90px; }
    .in.code { flex:0 0 90px; }
    .empty { color:var(--slate-500); text-align:center; padding:20px; }
    .actions { display:flex; gap:6px; } .btn-mini { padding:4px 9px; font-size:11px; border-radius:var(--r-btn); }
    .btn-d { background:#fee2e2; color:#991b1b; border:1px solid #fecaca; }
  `],
})
export class TestCatalogueComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly busy = signal(false);
  readonly groups = signal<TestGroup[]>([]);
  readonly setups = signal<TestSetup[]>([]);
  readonly editG = signal<string | null>(null);
  readonly editS = signal<string | null>(null);

  newGroup = { code: '', nameEn: '', nameAr: '' };
  newSetup: { code: string; nameEn: string; groupId: string | null } = { code: '', nameEn: '', groupId: null };
  draftG = { nameEn: '', nameAr: '' };
  draftS: { nameEn: string; groupId: string | null } = { nameEn: '', groupId: null };

  constructor() { this.load(); }

  load(): void {
    this.api.get<TestGroup[]>('/test-groups').subscribe({ next: (g) => this.groups.set(g) });
    this.api.get<TestSetup[]>('/test-setups').subscribe({ next: (s) => this.setups.set(s) });
  }

  groupCode(id: string | null): string { return id ? (this.groups().find((g) => g.id === id)?.code ?? '—') : '—'; }

  addGroup(): void {
    this.run(this.api.post('/test-groups', { code: this.newGroup.code, nameEn: this.newGroup.nameEn, nameAr: this.newGroup.nameAr || null }),
      () => { this.newGroup = { code: '', nameEn: '', nameAr: '' }; });
  }
  startEditG(g: TestGroup): void { this.editG.set(g.id); this.draftG = { nameEn: g.nameEn, nameAr: g.nameAr ?? '' }; }
  saveGroup(g: TestGroup): void {
    this.run(this.api.put(`/test-groups/${g.id}`, { id: g.id, nameEn: this.draftG.nameEn, nameAr: this.draftG.nameAr || null }), () => this.editG.set(null));
  }
  delGroup(g: TestGroup): void { this.run(this.api.delete(`/test-groups/${g.id}`)); }

  addSetup(): void {
    this.run(this.api.post('/test-setups', { code: this.newSetup.code, nameEn: this.newSetup.nameEn, nameAr: null, groupId: this.newSetup.groupId }),
      () => { this.newSetup = { code: '', nameEn: '', groupId: null }; });
  }
  startEditS(s: TestSetup): void { this.editS.set(s.id); this.draftS = { nameEn: s.nameEn, groupId: s.groupId }; }
  saveSetup(s: TestSetup): void {
    this.run(this.api.put(`/test-setups/${s.id}`, { id: s.id, nameEn: this.draftS.nameEn, nameAr: s.nameAr, groupId: this.draftS.groupId }), () => this.editS.set(null));
  }
  delSetup(s: TestSetup): void { this.run(this.api.delete(`/test-setups/${s.id}`)); }

  private run(obs: { subscribe: Function }, onOk?: () => void): void {
    this.busy.set(true);
    (obs as any).subscribe({
      next: () => { this.busy.set(false); onOk?.(); this.load(); },
      error: () => { this.busy.set(false); },
    });
  }
}
