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
      const lucide = (window as unknown as { lucide?: { createIcons: () => void } }).lucide;
      lucide?.createIcons();
    });
  }
}
