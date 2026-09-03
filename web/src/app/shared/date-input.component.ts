import { Component, ElementRef, HostListener, Input, OnDestroy, forwardRef, inject } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

/**
 * Locale-independent date picker. Always displays/edits dates as dd/MM/yyyy and
 * exposes the value as a yyyy-MM-dd string (or '' when empty) — a drop-in
 * replacement for <input type="date"> that works with [(ngModel)] and formControlName.
 * The calendar popup is fully rendered by this component, so it never falls back to
 * the browser/OS locale format the way a native date input does.
 */
const WD = ['Sa', 'Su', 'Mo', 'Tu', 'We', 'Th', 'Fr'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const pad = (n: number) => String(n).padStart(2, '0');

@Component({
  selector: 'app-date-input',
  standalone: true,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DateInputComponent), multi: true }],
  template: `
    <div class="di-wrap">
      <input class="input di-text" dir="ltr" [value]="text" [placeholder]="placeholder" [disabled]="disabled"
             autocomplete="off" inputmode="numeric" maxlength="10"
             (input)="onInput($event)" (blur)="commit()" (click)="openCal()" (focus)="openCal()"
             (keydown.enter)="commit(); $event.preventDefault()">
      <button type="button" class="di-btn" [disabled]="disabled" (click)="toggle()" aria-label="Open calendar" tabindex="-1">📅</button>
      @if (open) {
        <div class="di-pop" dir="ltr" [style.left.px]="popLeft"
             [style.top.px]="popAbove ? null : popTop" [style.bottom.px]="popAbove ? popBottom : null">
          <div class="di-head">
            <button type="button" class="di-nav" (click)="prevYear()" title="Previous year">«</button>
            <button type="button" class="di-nav" (click)="prevMonth()" title="Previous month">‹</button>
            <span class="di-title">{{ monthLabel }}</span>
            <button type="button" class="di-nav" (click)="nextMonth()" title="Next month">›</button>
            <button type="button" class="di-nav" (click)="nextYear()" title="Next year">»</button>
          </div>
          <div class="di-grid di-wd">
            @for (w of weekdays; track w) { <span class="di-wdc">{{ w }}</span> }
          </div>
          <div class="di-grid">
            @for (c of cells; track $index) {
              @if (c) {
                <button type="button" class="di-day" [class.di-sel]="isSelected(c)" [class.di-today]="isToday(c)"
                        [disabled]="isDisabled(c)" (click)="pick(c)">{{ c }}</button>
              } @else { <span class="di-day di-empty"></span> }
            }
          </div>
          <div class="di-foot">
            <button type="button" class="di-link" (click)="today()">Today</button>
            <button type="button" class="di-link" (click)="clear()">Clear</button>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .di-wrap { position: relative; display: block; }
    .di-text { width: 100%; padding-inline-end: 34px; }
    .di-btn { position: absolute; inset-inline-end: 4px; top: 50%; transform: translateY(-50%);
      border: 0; background: transparent; cursor: pointer; font-size: 15px; line-height: 1; padding: 4px; border-radius: 6px; }
    .di-btn:disabled { opacity: .4; cursor: default; }
    .di-pop { position: fixed; z-index: 1000; width: 252px;
      background: #fff; color: #323130; border: 1px solid #C8C6C4; border-radius: 10px;
      box-shadow: var(--shadow-pop, 0 12px 20px -4px rgba(0,0,0,.15)); padding: 10px; }
    .di-head { display: flex; align-items: center; gap: 2px; margin-bottom: 8px; }
    .di-title { flex: 1; text-align: center; font-weight: 700; font-size: 13px; color: #323130; }
    .di-nav { width: 26px; height: 26px; border: 0; background: transparent; cursor: pointer; border-radius: 6px;
      font-size: 14px; color: #605E5C; }
    .di-nav:hover { background: #F3F2F1; color: #0078D4; }
    .di-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 2px; }
    .di-wd { margin-bottom: 4px; }
    .di-wdc { text-align: center; font-size: 11px; font-weight: 700; color: #A19F9D; padding: 2px 0; }
    .di-day { height: 30px; border: 0; background: transparent; cursor: pointer; border-radius: 7px;
      font-size: 12px; color: #323130; padding: 0; }
    .di-day:hover:not(:disabled) { background: rgba(0,120,212,.10); }
    .di-day:disabled { color: #C8C6C4; cursor: default; }
    .di-empty { visibility: hidden; }
    .di-today { box-shadow: inset 0 0 0 1px #0078D4; }
    .di-sel, .di-sel:hover { background: #0078D4 !important; color: #fff; font-weight: 700; }
    .di-foot { display: flex; justify-content: space-between; margin-top: 8px; padding-top: 8px; border-top: 1px solid #EDEBE9; }
    .di-link { border: 0; background: transparent; cursor: pointer; font-size: 12px; color: #0078D4; padding: 4px 6px; border-radius: 6px; }
    .di-link:hover { background: #F3F2F1; }
  `],
})
export class DateInputComponent implements ControlValueAccessor, OnDestroy {
  @Input() placeholder = 'dd/mm/yyyy';
  @Input() min?: string;   // yyyy-MM-dd, inclusive
  @Input() max?: string;   // yyyy-MM-dd, inclusive

  private readonly host = inject(ElementRef<HTMLElement>);

  // Close on any scroll (capture phase catches scrolling in nested containers, not just the window).
  private readonly scrollHandler = (): void => { if (this.open) this.close(); };
  constructor() { document.addEventListener('scroll', this.scrollHandler, true); }
  ngOnDestroy(): void { document.removeEventListener('scroll', this.scrollHandler, true); }

  value = '';   // yyyy-MM-dd or ''
  text = '';    // dd/MM/yyyy display
  open = false;
  disabled = false;
  viewYear = new Date().getFullYear();
  viewMonth = new Date().getMonth();

  private onChange: (v: string) => void = () => {};
  private onTouched: () => void = () => {};

  // --- ControlValueAccessor ---
  writeValue(v: string | null): void {
    this.value = this.normalize(v);
    this.text = this.value ? this.fmt(this.value) : '';
    this.syncView();
  }
  registerOnChange(fn: (v: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(d: boolean): void { this.disabled = d; }

  private normalize(v: string | null | undefined): string {
    if (!v) return '';
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(v));
    return m ? `${m[1]}-${m[2]}-${m[3]}` : '';
  }
  private fmt(ymd: string): string { const [y, m, d] = ymd.split('-'); return `${d}/${m}/${y}`; }
  private syncView(): void {
    if (this.value) { const [y, m] = this.value.split('-').map(Number); this.viewYear = y; this.viewMonth = m - 1; }
    else { const n = new Date(); this.viewYear = n.getFullYear(); this.viewMonth = n.getMonth(); }
  }

  // --- typing ---
  onInput(ev: Event): void {
    const el = ev.target as HTMLInputElement;
    const digits = el.value.replace(/\D/g, '').slice(0, 8);
    let t = digits.slice(0, 2);
    if (digits.length > 2) t += '/' + digits.slice(2, 4);
    if (digits.length > 4) t += '/' + digits.slice(4, 8);
    this.text = t;
    el.value = t;
  }
  commit(): void {
    this.onTouched();
    const digits = this.text.replace(/\D/g, '');
    if (digits.length === 0) { this.setValue(''); return; }
    if (digits.length === 8) {
      const d = +digits.slice(0, 2), m = +digits.slice(2, 4), y = +digits.slice(4, 8);
      if (this.valid(y, m, d)) { this.setValue(`${y}-${pad(m)}-${pad(d)}`); return; }
    }
    this.text = this.value ? this.fmt(this.value) : '';   // revert invalid entry
  }
  private valid(y: number, m: number, d: number): boolean {
    if (m < 1 || m > 12 || d < 1 || d > 31 || y < 1000 || y > 9999) return false;
    const dt = new Date(y, m - 1, d);
    return dt.getFullYear() === y && dt.getMonth() === m - 1 && dt.getDate() === d;
  }
  private setValue(v: string): void {
    this.value = v;
    this.text = v ? this.fmt(v) : '';
    this.syncView();
    this.onChange(v);
  }

  // --- calendar ---
  popLeft = 0; popTop = 0; popBottom = 0; popAbove = false;

  openCal(): void { if (!this.disabled && !this.open) { this.open = true; this.syncView(); this.position(); } }
  toggle(): void { if (this.disabled) return; this.open = !this.open; if (this.open) { this.syncView(); this.position(); } }

  /** Position the fixed popup from the field's viewport rect so no ancestor's overflow can clip it. */
  private position(): void {
    const r = this.host.nativeElement.getBoundingClientRect();
    const W = 252, H = 316, pad = 8;
    this.popLeft = Math.max(pad, Math.min(r.left, window.innerWidth - W - pad));
    const below = window.innerHeight - r.bottom;
    if (below < H && r.top > below) { this.popAbove = true; this.popBottom = window.innerHeight - r.top + 4; }
    else { this.popAbove = false; this.popTop = r.bottom + 4; }
  }

  @HostListener('window:resize')
  onResize(): void { if (this.open) this.close(); }
  close(): void { this.open = false; }
  get monthLabel(): string { return `${MONTHS[this.viewMonth]} ${this.viewYear}`; }
  get weekdays(): string[] { return WD; }
  prevMonth(): void { if (this.viewMonth === 0) { this.viewMonth = 11; this.viewYear--; } else this.viewMonth--; }
  nextMonth(): void { if (this.viewMonth === 11) { this.viewMonth = 0; this.viewYear++; } else this.viewMonth++; }
  prevYear(): void { this.viewYear--; }
  nextYear(): void { this.viewYear++; }

  get cells(): (number | null)[] {
    const lead = (new Date(this.viewYear, this.viewMonth, 1).getDay() + 1) % 7;   // Saturday-first
    const days = new Date(this.viewYear, this.viewMonth + 1, 0).getDate();
    const arr: (number | null)[] = [];
    for (let i = 0; i < lead; i++) arr.push(null);
    for (let d = 1; d <= days; d++) arr.push(d);
    while (arr.length % 7 !== 0) arr.push(null);
    return arr;
  }
  private ymd(d: number): string { return `${this.viewYear}-${pad(this.viewMonth + 1)}-${pad(d)}`; }
  isSelected(d: number | null): boolean { return !!d && !!this.value && this.value === this.ymd(d); }
  isToday(d: number | null): boolean {
    if (!d) return false;
    const n = new Date();
    return n.getFullYear() === this.viewYear && n.getMonth() === this.viewMonth && n.getDate() === d;
  }
  isDisabled(d: number | null): boolean {
    if (!d) return true;
    const v = this.ymd(d);
    return (!!this.min && v < this.min) || (!!this.max && v > this.max);
  }
  pick(d: number | null): void { if (d && !this.isDisabled(d)) { this.setValue(this.ymd(d)); this.close(); } }
  today(): void { const n = new Date(); this.viewYear = n.getFullYear(); this.viewMonth = n.getMonth(); this.pick(n.getDate()); }
  clear(): void { this.setValue(''); this.close(); }

  @HostListener('document:click', ['$event'])
  onDocClick(ev: MouseEvent): void {
    if (this.open && !this.host.nativeElement.contains(ev.target as Node)) this.close();
  }
  @HostListener('keydown.escape')
  onEsc(): void { this.close(); }
}
