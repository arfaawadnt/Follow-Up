import { Component, computed, inject, input, output } from '@angular/core';
import { TranslatePipe } from '../core/i18n';
import { UiService } from '../core/ui.service';
import { HelpPage, helpFor } from '../core/help-content';

/**
 * The page help popup (2026-09-20): opened from the ? icon in the header for the current route. Shows the page's purpose
 * and business, its workflow as a numbered progress bar (each step with a short description), how the page works, tips
 * and the privileges involved — in the user's language, from `help-content.ts`.
 */
@Component({
  selector: 'app-help-dialog',
  standalone: true,
  imports: [TranslatePipe],
  styles: [`
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1200;padding:16px}
    .dlg{background:var(--white,#fff);border-radius:14px;width:min(94vw,860px);max-height:92vh;display:flex;flex-direction:column;box-shadow:0 16px 48px rgba(0,0,0,.25);border:1px solid var(--slate-150,#edebe9)}
    .head{display:flex;justify-content:space-between;align-items:flex-start;padding:16px 20px;border-bottom:1px solid var(--slate-150,#edebe9);background:linear-gradient(90deg,var(--primary-blue,#0078D4),#004578);color:#fff;border-radius:14px 14px 0 0}
    .head h2{font-size:17px;margin:0}.head .sub{font-size:12.5px;opacity:.9;margin-top:4px}
    .head button{background:rgba(255,255,255,.18);color:#fff;border:none;border-radius:6px;padding:4px 9px;cursor:pointer}
    .body{padding:16px 20px;overflow:auto;font-size:13.5px;line-height:1.55}
    h3{font-size:12px;text-transform:uppercase;letter-spacing:.06em;color:var(--slate-500,#8a8886);margin:16px 0 6px}
    h3:first-child{margin-top:0}
    p{margin:0 0 6px}
    ul{margin:0;padding-inline-start:20px}li{margin:3px 0}
    .steps{display:grid;grid-auto-flow:column;grid-auto-columns:1fr;gap:0;position:relative;margin:10px 0 4px}
    .steps::before{content:'';position:absolute;top:14px;left:8%;right:8%;height:4px;background:var(--slate-150,#edebe9);border-radius:2px}
    .steps::after{content:'';position:absolute;top:14px;left:8%;width:84%;height:4px;background:linear-gradient(90deg,var(--primary-blue,#0078D4),#15803d);border-radius:2px;opacity:.55}
    .step{position:relative;text-align:center;padding:0 6px}
    .num{width:30px;height:30px;border-radius:50%;background:var(--primary-blue,#0078D4);color:#fff;font-weight:700;display:flex;align-items:center;justify-content:center;margin:0 auto 6px;position:relative;z-index:1;box-shadow:0 0 0 4px var(--white,#fff)}
    .step:last-child .num{background:#15803d}
    .step b{display:block;font-size:12.5px}.step span{display:block;font-size:11.5px;color:var(--slate-600,#605e5c)}
    .priv{display:inline-block;background:var(--slate-100,#f3f2f1);border-radius:10px;padding:1px 9px;font-size:11.5px;margin:2px 4px 2px 0;font-family:ui-monospace,monospace}
    .empty{padding:24px;text-align:center;color:var(--slate-500,#8a8886)}
    @media (max-width:700px){.steps{grid-auto-flow:row}.steps::before,.steps::after{display:none}.step{display:flex;gap:10px;text-align:start;margin:4px 0}.num{margin:0;flex:none}}
  `],
  template: `
    <div class="overlay" (click)="closed.emit()">
      <div class="dlg" (click)="$event.stopPropagation()" role="dialog" aria-modal="true">
        <div class="head">
          <div><h2>{{ page()?.title || ('help' | t : 'Help') }}</h2><div class="sub">{{ page()?.purpose }}</div></div>
          <button type="button" (click)="closed.emit()">✕</button>
        </div>
        <div class="body">
          @if (page(); as p) {
            <h3>{{ 'help_business' | t : 'Business' }}</h3>
            <p>{{ p.business }}</p>
            <h3>{{ 'help_workflow' | t : 'How the page works' }}</h3>
            <div class="steps">
              @for (s of p.steps; track $index) {
                <div class="step"><div class="num">{{ $index + 1 }}</div><b>{{ s.title }}</b><span>{{ s.text }}</span></div>
              }
            </div>
            <ul>@for (h of p.how; track $index) { <li>{{ h }}</li> }</ul>
            @if (p.tips.length) {
              <h3>{{ 'help_tips' | t : 'Tips' }}</h3>
              <ul>@for (t of p.tips; track $index) { <li>{{ t }}</li> }</ul>
            }
            @if (p.privileges.length) {
              <h3>{{ 'help_privileges' | t : 'Privileges' }}</h3>
              <div>@for (pr of p.privileges; track pr) { <span class="priv">{{ pr }}</span> }</div>
            }
          } @else {
            <div class="empty">{{ 'help_not_available' | t : 'No help is written for this page yet.' }}</div>
          }
        </div>
      </div>
    </div>
  `,
})
export class HelpDialogComponent {
  private readonly ui = inject(UiService);
  /** The current router url (path only). */
  readonly url = input.required<string>();
  readonly closed = output<void>();
  readonly page = computed<HelpPage | null>(() => helpFor(this.url(), this.ui.lang() === 'ar' ? 'ar' : 'en'));
}
