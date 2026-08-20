import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslatePipe } from '../../core/i18n';

/** Temporary stand-in for reference pages still being rebuilt. Each is replaced by its real screen. */
@Component({
  selector: 'app-placeholder',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <div class="breadcrumbs"><span>{{ titleKey | t }}</span></div>
    <div class="card" style="padding:40px;text-align:center">
      <i data-lucide="hammer" style="width:40px;height:40px;color:var(--slate-500)"></i>
      <h2 style="margin:16px 0 6px;font:700 18px var(--disp);color:var(--slate-900)">{{ titleKey | t }}</h2>
      <p class="empty" style="margin:0">This screen is being rebuilt to match the reference platform.</p>
    </div>
  `,
})
export class PlaceholderComponent {
  private readonly route = inject(ActivatedRoute);
  titleKey = this.route.snapshot.data['titleKey'] ?? 'app_title';
}
