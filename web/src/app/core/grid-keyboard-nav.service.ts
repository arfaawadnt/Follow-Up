import { DOCUMENT } from '@angular/common';
import { Injectable, OnDestroy, inject } from '@angular/core';

/**
 * Arrow-key navigation for every data grid in the app.
 *
 * Works by event delegation on the document, so no page has to opt in: any `<table>` inside a `.grid-scroll`
 * wrapper (the convention every data table follows — see GridConventionTests) becomes focusable and gets an
 * "active cell" that the arrow keys move. This keeps the behaviour in one place and guarantees new pages get it
 * for free.
 *
 * Keys (only while the table itself, or a non-editable cell inside it, has focus — typing in an input inside a
 * grid is never hijacked; Escape in such an input hands focus back to the table):
 *   ↑ ↓            previous / next data row (same column)
 *   ← →            previous / next cell (follows the writing direction, so ← is always "towards the start")
 *   Home / End     first / last cell of the row;  Ctrl+Home / Ctrl+End  first / last row
 *   PageUp / PageDown  ten rows
 *   Enter / Space  activate the cell: click its single button/link, focus its input/select, or click a clickable row
 *
 * The active cell is scrolled into view inside the wrapper, so a frozen header never hides it.
 */
@Injectable({ providedIn: 'root' })
export class GridKeyboardNavService implements OnDestroy {
  private readonly doc = inject(DOCUMENT);
  private started = false;
  private observer: MutationObserver | null = null;

  static readonly CELL = 'gk-cell';
  static readonly ROW = 'gk-row';
  private static readonly PAGE = 10;

  /** Idempotent; called once by the shell. */
  start(): void {
    if (this.started) return;
    this.started = true;
    this.doc.addEventListener('keydown', this.onKeyDown, true);
    this.doc.addEventListener('mousedown', this.onMouseDown, true);
    this.doc.addEventListener('focusin', this.onFocusIn, true);
    this.makeTablesFocusable();
    this.observer = new MutationObserver(() => this.makeTablesFocusable());
    this.observer.observe(this.doc.body, { childList: true, subtree: true });
  }

  ngOnDestroy(): void {
    this.doc.removeEventListener('keydown', this.onKeyDown, true);
    this.doc.removeEventListener('mousedown', this.onMouseDown, true);
    this.doc.removeEventListener('focusin', this.onFocusIn, true);
    this.observer?.disconnect();
    this.started = false;
  }

  /** Grids must be reachable with Tab; Angular templates don't carry a tabindex, so add one as tables appear. */
  private makeTablesFocusable(): void {
    this.doc.querySelectorAll<HTMLTableElement>('.grid-scroll > table:not([tabindex])').forEach((t) => t.setAttribute('tabindex', '0'));
  }

  // ---- event handlers (arrow functions so they can be removed) -----------------------------------------------

  private readonly onMouseDown = (e: MouseEvent): void => {
    const cell = this.cellOf(e.target);
    if (cell) this.activate(cell, /* scroll */ false);
  };

  private readonly onFocusIn = (e: FocusEvent): void => {
    const t = e.target as HTMLElement | null;
    if (t instanceof HTMLTableElement && this.gridOf(t) && !this.activeCell(t)) {
      const first = this.dataRows(t)[0]?.cells[0];
      if (first) this.activate(first, false);
    }
  };

  private readonly onKeyDown = (e: KeyboardEvent): void => {
    const target = e.target as HTMLElement | null;
    if (!target) return;
    const table = this.gridOf(target);
    if (!table) return;

    // Never hijack typing / native control keys inside the grid; Escape returns focus to the grid.
    if (this.isEditable(target)) {
      if (e.key === 'Escape') { table.focus({ preventScroll: true }); e.preventDefault(); }
      return;
    }

    const current = this.cellOf(target) ?? this.activeCell(table) ?? this.dataRows(table)[0]?.cells[0];
    if (!current) return;

    switch (e.key) {
      case 'ArrowDown': this.moveRows(table, current, +1); break;
      case 'ArrowUp': this.moveRows(table, current, -1); break;
      case 'PageDown': this.moveRows(table, current, +GridKeyboardNavService.PAGE); break;
      case 'PageUp': this.moveRows(table, current, -GridKeyboardNavService.PAGE); break;
      case 'ArrowRight': this.moveCols(table, current, this.isRtl(table) ? -1 : +1); break;
      case 'ArrowLeft': this.moveCols(table, current, this.isRtl(table) ? +1 : -1); break;
      case 'Home':
        if (e.ctrlKey) this.moveRows(table, current, -Infinity); else this.moveCols(table, current, -Infinity);
        break;
      case 'End':
        if (e.ctrlKey) this.moveRows(table, current, +Infinity); else this.moveCols(table, current, +Infinity);
        break;
      case 'Enter':
      case ' ':
        this.activateCell(current);
        break;
      default: return; // not ours — leave the event alone
    }
    e.preventDefault();
    e.stopPropagation();
  };

  // ---- movement -----------------------------------------------------------------------------------------------

  private moveRows(table: HTMLTableElement, from: HTMLTableCellElement, delta: number): void {
    const rows = this.dataRows(table);
    const row = from.parentElement as HTMLTableRowElement;
    const i = rows.indexOf(row);
    const base = i < 0 ? 0 : i;
    const j = Math.min(rows.length - 1, Math.max(0, delta === Infinity ? rows.length - 1 : delta === -Infinity ? 0 : base + delta));
    const target = rows[j];
    if (!target) return;
    const col = Math.min(from.cellIndex, target.cells.length - 1);
    const cell = target.cells[Math.max(0, col)];
    if (cell) this.activate(cell, true);
  }

  private moveCols(table: HTMLTableElement, from: HTMLTableCellElement, delta: number): void {
    const row = from.parentElement as HTMLTableRowElement;
    const n = row.cells.length;
    const j = Math.min(n - 1, Math.max(0, delta === Infinity ? n - 1 : delta === -Infinity ? 0 : from.cellIndex + delta));
    const cell = row.cells[j];
    if (cell) this.activate(cell, true);
  }

  private activate(cell: HTMLTableCellElement, scroll: boolean): void {
    const table = cell.closest('table');
    if (!table) return;
    table.querySelectorAll('.' + GridKeyboardNavService.CELL).forEach((c) => c.classList.remove(GridKeyboardNavService.CELL));
    table.querySelectorAll('.' + GridKeyboardNavService.ROW).forEach((r) => r.classList.remove(GridKeyboardNavService.ROW));
    cell.classList.add(GridKeyboardNavService.CELL);
    cell.parentElement?.classList.add(GridKeyboardNavService.ROW);
    if (scroll) {
      cell.scrollIntoView({ block: 'nearest', inline: 'nearest' });
      // Keep the header from covering the cell we just scrolled to (the header is sticky inside the wrapper).
      const wrap = table.parentElement;
      const thead = table.tHead ?? this.headerRow(table);
      if (wrap && thead) {
        const hb = thead.getBoundingClientRect().bottom;
        const ct = cell.getBoundingClientRect().top;
        if (ct < hb) wrap.scrollTop -= (hb - ct);
      }
      if (this.doc.activeElement !== table && !this.isEditable(this.doc.activeElement)) table.focus({ preventScroll: true });
    }
  }

  /** Enter / Space on a cell: one control → use it; otherwise a clickable row → click it. */
  private activateCell(cell: HTMLTableCellElement): void {
    const controls = cell.querySelectorAll<HTMLElement>('button, a[href], a[routerlink], input, select, textarea, [role="button"]');
    if (controls.length === 1) {
      const c = controls[0];
      if (c instanceof HTMLInputElement && !['checkbox', 'radio', 'button', 'submit'].includes(c.type)) { c.focus(); c.select?.(); return; }
      if (c instanceof HTMLSelectElement || c instanceof HTMLTextAreaElement) { c.focus(); return; }
      c.click();
      return;
    }
    if (controls.length > 1) { controls[0].focus(); return; }
    const row = cell.parentElement as HTMLTableRowElement | null;
    if (row && (row.classList.contains('clickable') || row.onclick || row.hasAttribute('ng-reflect-router-link') || getComputedStyle(row).cursor === 'pointer')) {
      row.click();
    }
  }

  // ---- helpers ----------------------------------------------------------------------------------------------------

  private gridOf(el: Element | null): HTMLTableElement | null {
    const table = el?.closest('table');
    return table && table.parentElement?.classList.contains('grid-scroll') ? (table as HTMLTableElement) : null;
  }

  private cellOf(target: EventTarget | null): HTMLTableCellElement | null {
    const el = target instanceof Element ? target : null;
    const cell = el?.closest('td, th') as HTMLTableCellElement | null;
    if (!cell || !this.gridOf(cell)) return null;
    return cell;
  }

  private activeCell(table: HTMLTableElement): HTMLTableCellElement | null {
    return table.querySelector('.' + GridKeyboardNavService.CELL);
  }

  /**
   * Rows that carry data — any row with at least one <td>. Uses table.rows (not tBodies) because Angular appends a
   * template's bare <tr>s straight under <table> with no implicit <tbody> (daily / dashboard), and header rows written
   * into the body (all <th>) are skipped either way.
   */
  private dataRows(table: HTMLTableElement): HTMLTableRowElement[] {
    return Array.from(table.rows).filter((r) => Array.from(r.cells).some((c) => c.tagName === 'TD'));
  }

  /** A header row written without <thead>: the first row, when every cell in it is a <th>. */
  private headerRow(table: HTMLTableElement): HTMLTableRowElement | null {
    const first = table.rows[0];
    return first && first.cells.length > 0 && Array.from(first.cells).every((c) => c.tagName === 'TH') ? first : null;
  }

  private isEditable(el: Element | null): boolean {
    if (!el) return false;
    if (el instanceof HTMLInputElement) return !['checkbox', 'radio', 'button', 'submit', 'file'].includes(el.type);
    return el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement || (el as HTMLElement).isContentEditable === true;
  }

  private isRtl(el: Element): boolean { return getComputedStyle(el).direction === 'rtl'; }
}
