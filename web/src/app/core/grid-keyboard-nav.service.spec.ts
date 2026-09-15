import { TestBed } from '@angular/core/testing';
import { GridKeyboardNavService } from './grid-keyboard-nav.service';

describe('GridKeyboardNavService', () => {
  let svc: GridKeyboardNavService;
  let host: HTMLDivElement;
  let table: HTMLTableElement;
  let clicks: string[];

  const key = (el: Element, k: string, init: KeyboardEventInit = {}) =>
    el.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true, ...init }));
  const active = () => table.querySelector('.' + GridKeyboardNavService.CELL) as HTMLTableCellElement | null;
  const at = (r: number, c: number) => table.tBodies[0].rows[r].cells[c];

  beforeEach(async () => {
    TestBed.configureTestingModule({});
    svc = TestBed.inject(GridKeyboardNavService);
    svc.start();
    clicks = [];
    host = document.createElement('div');
    host.innerHTML = `
      <div class="grid-scroll">
        <table>
          <thead><tr><th>A</th><th>B</th><th>C</th></tr></thead>
          <tbody>
            <tr class="clickable"><td>r0c0</td><td>r0c1</td><td><button type="button" id="btn">go</button></td></tr>
            <tr class="clickable"><td>r1c0</td><td><input id="txt" type="text"></td><td>r1c2</td></tr>
            <tr class="clickable"><td>r2c0</td><td>r2c1</td><td>r2c2</td></tr>
          </tbody>
        </table>
      </div>
      <table id="plain"><tbody><tr><td>outside</td></tr></tbody></table>`;
    document.body.appendChild(host);
    table = host.querySelector('.grid-scroll > table') as HTMLTableElement;
    table.tBodies[0].querySelectorAll('tr').forEach((r, i) => r.addEventListener('click', () => clicks.push(`row${i}`)));
    (host.querySelector('#btn') as HTMLButtonElement).addEventListener('click', (e) => { e.stopPropagation(); clicks.push('btn'); });
    // The MutationObserver runs asynchronously; give it a tick to add tabindex.
    await new Promise((r) => setTimeout(r, 0));
  });

  afterEach(() => { host.remove(); svc.ngOnDestroy(); });

  it('makes grid tables focusable but leaves other tables alone', () => {
    expect(table.getAttribute('tabindex')).toBe('0');
    expect((host.querySelector('#plain') as HTMLElement).hasAttribute('tabindex')).toBeFalse();
  });

  it('focusing the table activates the first data cell (never the header)', () => {
    table.focus();
    expect(active()).toBe(at(0, 0));
  });

  it('arrows move the active cell and clamp at the edges', () => {
    table.focus();
    key(table, 'ArrowDown'); expect(active()).toBe(at(1, 0));
    key(table, 'ArrowRight'); expect(active()).toBe(at(1, 1));
    key(table, 'ArrowDown'); key(table, 'ArrowDown'); key(table, 'ArrowDown'); expect(active()).toBe(at(2, 1));
    key(table, 'ArrowRight'); key(table, 'ArrowRight'); expect(active()).toBe(at(2, 2));
    key(table, 'ArrowUp'); key(table, 'ArrowUp'); key(table, 'ArrowUp'); expect(active()).toBe(at(0, 2));
    key(table, 'Home'); expect(active()).toBe(at(0, 0));
    key(table, 'End'); expect(active()).toBe(at(0, 2));
    key(table, 'End', { ctrlKey: true }); expect(active()).toBe(at(2, 2));
    key(table, 'Home', { ctrlKey: true }); expect(active()).toBe(at(0, 2));
  });

  it('left/right follow the writing direction in RTL', () => {
    host.style.direction = 'rtl';
    table.focus();
    key(table, 'ArrowLeft'); expect(active()).toBe(at(0, 1)); // ← moves towards the row end in RTL
    key(table, 'ArrowRight'); expect(active()).toBe(at(0, 0));
  });

  it('the arrow keydown is consumed (page does not scroll) and unknown keys are left alone', () => {
    table.focus();
    const consumed = !table.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    const untouched = table.dispatchEvent(new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true }));
    expect(consumed).toBeTrue();
    expect(untouched).toBeTrue();
  });

  it('Enter clicks a clickable row, or the single button in the cell', () => {
    table.focus();
    key(table, 'Enter'); expect(clicks).toEqual(['row0']);
    key(table, 'End'); key(table, 'Enter'); expect(clicks).toEqual(['row0', 'btn']);
  });

  it('Enter on a cell with an input focuses it; typing there is not hijacked; Escape returns to the grid', () => {
    table.focus();
    key(table, 'ArrowDown'); key(table, 'ArrowRight');
    key(table, 'Enter');
    const txt = host.querySelector('#txt') as HTMLInputElement;
    expect(document.activeElement).toBe(txt);
    const before = active();
    const notConsumed = txt.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    expect(notConsumed).toBeTrue();
    expect(active()).toBe(before);
    key(txt, 'Escape');
    expect(document.activeElement).toBe(table);
  });

  it('handles a table whose rows are direct children (Angular renders bare <tr>s with no <tbody>)', async () => {
    // Build with DOM APIs on purpose: innerHTML would auto-insert a <tbody>, which is exactly what Angular does NOT do.
    const wrap = document.createElement('div'); wrap.className = 'grid-scroll';
    const t = document.createElement('table');
    const mk = (tag: 'th' | 'td', texts: string[]) => { const tr = document.createElement('tr'); for (const s of texts) { const c = document.createElement(tag); c.textContent = s; tr.appendChild(c); } return tr; };
    t.appendChild(mk('th', ['H1', 'H2']));
    t.appendChild(mk('td', ['a0', 'a1']));
    t.appendChild(mk('td', ['b0', 'b1']));
    wrap.appendChild(t); host.appendChild(wrap);
    await new Promise((r) => setTimeout(r, 0));
    expect(t.tBodies.length).toBe(0);
    expect(t.getAttribute('tabindex')).toBe('0');
    t.focus();
    const activeIn = () => t.querySelector('.' + GridKeyboardNavService.CELL) as HTMLTableCellElement;
    expect(activeIn()).toBe(t.rows[1].cells[0]); // header row skipped
    key(t, 'ArrowUp'); expect(activeIn()).toBe(t.rows[1].cells[0]); // cannot climb into the header
    key(t, 'ArrowDown'); key(t, 'End'); expect(activeIn()).toBe(t.rows[2].cells[1]);
  });

  it('clicking a cell makes it the active cell', () => {
    at(2, 1).dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    expect(active()).toBe(at(2, 1));
    expect(at(2, 1).parentElement!.classList.contains(GridKeyboardNavService.ROW)).toBeTrue();
  });
});
