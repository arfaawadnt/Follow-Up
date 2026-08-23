import { Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

/** Fixed-position host that renders the ToastService queue. Mounted once at the app root. */
@Component({
  selector: 'app-toast',
  standalone: true,
  template: `
    <div class="toast-host" role="status" aria-live="polite">
      @for (t of toasts.toasts(); track t.id) {
        <div class="toast toast-{{ t.kind }}">
          <span class="toast-msg">{{ t.text }}</span>
          <button class="toast-x" type="button" aria-label="Dismiss" (click)="toasts.dismiss(t.id)">×</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-host { position: fixed; bottom: 20px; inset-inline-end: 20px; z-index: 9999;
      display: flex; flex-direction: column; gap: 10px; max-width: min(92vw, 420px); pointer-events: none; }
    .toast { pointer-events: auto; display: flex; align-items: flex-start; gap: 12px;
      padding: 12px 14px; border-radius: 10px; color: #fff; font-size: 14px; line-height: 1.4;
      box-shadow: 0 8px 24px rgba(0,0,0,.22); animation: toast-in .18s ease-out; }
    .toast-msg { flex: 1; word-break: break-word; }
    .toast-x { pointer-events: auto; background: transparent; border: 0; color: inherit; opacity: .8;
      font-size: 20px; line-height: 1; cursor: pointer; padding: 0 2px; }
    .toast-x:hover { opacity: 1; }
    .toast-error { background: #dc2626; }
    .toast-success { background: #16a34a; }
    .toast-info { background: #334155; }
    @keyframes toast-in { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }
  `],
})
export class ToastComponent {
  readonly toasts = inject(ToastService);
}
