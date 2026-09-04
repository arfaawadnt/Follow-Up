import { Component, ElementRef, HostListener, Input, OnDestroy, forwardRef, inject, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface FilterOption { value: string; label: string; }

/**
 * A drop-in replacement for the filter `<select class="select">` dropdowns.
 * - `[multiple]="true"` turns it into a checkbox multi-select whose value is a string[] ([] = no filter / "All").
 *   Single mode keeps a string value ('' = no filter / "All"), matching the native selects it replaces.
 * - A search box appears automatically once the option list exceeds `searchThreshold` (default 10), so long
 *   lists (governorates, cities, areas, categories, reps…) stay easy to scan.
 * - `options` accepts a plain `string[]` (label = value) or `{ value, label }[]` for id/label pairs.
 * Works with `[(ngModel)]` / `[ngModel]`+`(ngModelChange)`; the popup is fixed-positioned like app-date-input
 * so no ancestor `overflow` can clip it.
 */
@Component({
  selector: 'app-filter-select',
  standalone: true,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => FilterSelectComponent), multi: true }],
  template: `
    <div class="fs-wrap">
      <button type="button" class="select fs-trigger" [class.fs-open]="open()" [disabled]="disabled"
              (click)="toggle()" [title]="summary()">
        <span class="fs-label" [class.fs-placeholder]="isEmpty()">{{ summary() }}</span>
        <span class="fs-caret">▾</span>
      </button>
      @if (open()) {
        <div class="fs-pop" [style.left.px]="popLeft" [style.width.px]="popWidth"
             [style.top.px]="popAbove ? null : popTop" [style.bottom.px]="popAbove ? popBottom : null">
          @if (showSearch()) {
            <div class="fs-search">
              <input class="input fs-search-input" [value]="query()" (input)="query.set(qEl.value)" #qEl
                     [placeholder]="searchPlaceholder" autocomplete="off">
            </div>
          }
          @if (multiple || clearable) {
            <div class="fs-actions">
              @if (multiple) {
                <button type="button" class="fs-link" (click)="selectAllVisible()">{{ selectAllLabel }}</button>
                <button type="button" class="fs-link" (click)="clear()">{{ clearLabel }}</button>
              } @else {
                <button type="button" class="fs-opt fs-all" [class.fs-sel]="isEmpty()" (click)="pickSingle(allValue)">{{ placeholder }}</button>
              }
            </div>
          }
          <div class="fs-list">
            @for (o of visible(); track o.value) {
              @if (multiple) {
                <label class="fs-opt fs-check">
                  <input type="checkbox" [checked]="has(o.value)" (change)="toggleValue(o.value)">
                  <span>{{ o.label }}</span>
                </label>
              } @else {
                <button type="button" class="fs-opt" [class.fs-sel]="single() === o.value" (click)="pickSingle(o.value)">{{ o.label }}</button>
              }
            } @empty { <div class="fs-empty">{{ noneLabel }}</div> }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .fs-wrap { position: relative; display: block; }
    .fs-trigger { display: flex; align-items: center; gap: 6px; text-align: start; cursor: pointer; }
    .fs-trigger:disabled { opacity: .5; cursor: default; }
    .fs-label { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .fs-placeholder { color: var(--slate-500, #A19F9D); }
    .fs-caret { color: var(--slate-500, #A19F9D); font-size: 11px; }
    .fs-open .fs-caret { color: var(--primary-blue, #0078D4); }
    .fs-pop { position: fixed; z-index: 1000; min-width: 200px; max-width: 360px;
      background: #fff; border: 1px solid var(--slate-300, #C8C6C4); border-radius: 10px;
      box-shadow: var(--shadow-pop, 0 12px 20px -4px rgba(0,0,0,.15)); padding: 8px; }
    .fs-search { margin-bottom: 6px; }
    .fs-search-input { width: 100%; }
    .fs-actions { display: flex; gap: 8px; align-items: center; padding: 0 2px 6px; border-bottom: 1px solid var(--slate-150, #EDEBE9); margin-bottom: 4px; }
    .fs-link { border: 0; background: transparent; cursor: pointer; font-size: 12px; color: var(--primary-blue, #0078D4); padding: 2px 4px; border-radius: 6px; }
    .fs-link:hover { background: var(--slate-100, #F3F2F1); }
    .fs-list { max-height: 240px; overflow-y: auto; display: flex; flex-direction: column; }
    .fs-opt { display: flex; align-items: center; gap: 8px; width: 100%; text-align: start; border: 0; background: transparent;
      cursor: pointer; font-size: 13px; color: var(--slate-900, #323130); padding: 6px 8px; border-radius: 7px; }
    .fs-opt:hover { background: rgba(0,120,212,.08); }
    .fs-sel { background: rgba(0,120,212,.12); font-weight: 600; }
    .fs-check input { width: 15px; height: 15px; accent-color: var(--primary-blue, #0078D4); margin: 0; }
    .fs-all { color: var(--slate-700, #605E5C); }
    .fs-empty { padding: 10px 8px; color: var(--slate-500, #A19F9D); font-size: 12.5px; }
  `],
})
export class FilterSelectComponent implements ControlValueAccessor, OnDestroy {
  @Input() options: (string | FilterOption)[] = [];
  @Input() multiple = false;
  /** Single-mode value that means "no filter" — set to 'All' for pages that use that sentinel ('' by default). */
  @Input() allValue = '';
  /** Single mode only: when false, hides the "All" (clear) option for a required record selector. */
  @Input() clearable = true;
  @Input() placeholder = 'All';
  @Input() searchThreshold = 10;
  @Input() searchPlaceholder = 'Search…';
  @Input() selectAllLabel = 'Select all';
  @Input() clearLabel = 'Clear';
  @Input() noneLabel = 'No matches';

  private readonly host = inject(ElementRef<HTMLElement>);
  readonly open = signal(false);
  readonly query = signal('');
  disabled = false;

  readonly single = signal('');            // single mode: the selected value ('' = none)
  readonly multi = signal<string[]>([]);   // multi mode: selected values ([] = none)

  private onChange: (v: string | string[]) => void = () => {};
  private onTouched: () => void = () => {};

  private readonly scrollHandler = (): void => { if (this.open()) this.close(); };
  constructor() { document.addEventListener('scroll', this.scrollHandler, true); }
  ngOnDestroy(): void { document.removeEventListener('scroll', this.scrollHandler, true); }

  private norm(): FilterOption[] {
    return this.options.map((o) => (typeof o === 'string' ? { value: o, label: o } : o));
  }
  private labelOf(value: string): string {
    const o = this.norm().find((x) => x.value === value);
    return o ? o.label : value;
  }

  // --- ControlValueAccessor ---
  writeValue(v: string | string[] | null): void {
    if (this.multiple) this.multi.set(Array.isArray(v) ? [...v] : v ? [v] : []);
    else this.single.set(typeof v === 'string' ? v : Array.isArray(v) ? v[0] ?? '' : '');
  }
  registerOnChange(fn: (v: string | string[]) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(d: boolean): void { this.disabled = d; }

  // --- display ---
  isEmpty(): boolean { return this.multiple ? this.multi().length === 0 : (!this.single() || this.single() === this.allValue); }
  summary(): string {
    if (this.multiple) {
      const m = this.multi();
      return m.length === 0 ? this.placeholder : m.length === 1 ? this.labelOf(m[0]) : `${m.length} selected`;
    }
    return this.isEmpty() ? this.placeholder : this.labelOf(this.single());
  }
  showSearch(): boolean { return this.options.length > this.searchThreshold; }
  visible(): FilterOption[] {
    const q = this.query().trim().toLowerCase();
    const all = this.norm();
    return q ? all.filter((o) => o.label.toLowerCase().includes(q)) : all;
  }

  // --- selection (operates on values) ---
  has(value: string): boolean { return this.multi().includes(value); }
  toggleValue(value: string): void {
    const set = new Set(this.multi());
    set.has(value) ? set.delete(value) : set.add(value);
    const next = this.norm().map((o) => o.value).filter((v) => set.has(v)); // keep option order
    this.multi.set(next);
    this.onChange(next);
  }
  selectAllVisible(): void {
    const set = new Set([...this.multi(), ...this.visible().map((o) => o.value)]);
    const next = this.norm().map((o) => o.value).filter((v) => set.has(v));
    this.multi.set(next);
    this.onChange(next);
  }
  clear(): void { this.multi.set([]); this.onChange([]); }
  pickSingle(value: string): void { this.single.set(value); this.onChange(value); this.close(); }

  // --- popup ---
  popLeft = 0; popTop = 0; popBottom = 0; popWidth = 220; popAbove = false;
  toggle(): void {
    if (this.disabled) return;
    if (this.open()) { this.close(); return; }
    this.onTouched();
    this.query.set('');
    this.open.set(true);
    this.position();
  }
  close(): void { this.open.set(false); }
  private position(): void {
    const r = this.host.nativeElement.getBoundingClientRect();
    const H = 320, pad = 8;
    this.popWidth = Math.max(200, r.width);
    this.popLeft = Math.max(pad, Math.min(r.left, window.innerWidth - this.popWidth - pad));
    const below = window.innerHeight - r.bottom;
    if (below < H && r.top > below) { this.popAbove = true; this.popBottom = window.innerHeight - r.top + 4; }
    else { this.popAbove = false; this.popTop = r.bottom + 4; }
  }
  @HostListener('window:resize') onResize(): void { if (this.open()) this.close(); }
  @HostListener('document:click', ['$event'])
  onDocClick(ev: MouseEvent): void { if (this.open() && !this.host.nativeElement.contains(ev.target as Node)) this.close(); }
  @HostListener('keydown.escape') onEsc(): void { this.close(); }
}
