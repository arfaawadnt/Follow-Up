import { Injectable } from '@angular/core';

/**
 * Bridges Angular rendering to the vendored Lucide script (loaded in index.html). Templates use
 * `<i data-lucide="name">` exactly like the reference platform; after Angular renders, `render()` swaps
 * those placeholders for SVGs. Calls are coalesced into a single animation frame.
 */
@Injectable({ providedIn: 'root' })
export class IconsService {
  private scheduled = false;

  render(): void {
    if (this.scheduled) return;
    this.scheduled = true;
    requestAnimationFrame(() => {
      this.scheduled = false;
      // Only convert actual <i data-lucide> placeholders. Skipping when none remain means we never
      // touch (destroy/recreate) icons that are already rendered — that per-cycle node churn was racing
      // real clicks (the pressed element vanished before mouseup) and made the sidebar toggle
      // intermittently "ignore" clicks.
      if (!document.querySelector('i[data-lucide]')) return;
      const lucide = (window as unknown as { lucide?: { createIcons: () => void } }).lucide;
      if (!lucide?.createIcons) return;
      lucide.createIcons();
      // The vendored Lucide keeps data-lucide on the generated <svg>; strip it so a later createIcons()
      // can never reprocess/replace an already-converted icon.
      document.querySelectorAll('svg[data-lucide]').forEach((el) => el.removeAttribute('data-lucide'));
    });
  }
}
