import { Injectable, signal } from '@angular/core';

export type ToastKind = 'error' | 'success' | 'info';
export interface Toast { id: number; kind: ToastKind; text: string; }

/**
 * Transient, dismissible notifications. Fed by the HTTP error interceptor (so failed requests are never
 * silent again) and available to components for success confirmations. Signal-based so the host renders
 * reactively; each toast auto-dismisses after its timeout.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private seq = 0;

  show(text: string, kind: ToastKind = 'info', timeoutMs = 6000): void {
    const id = ++this.seq;
    this.toasts.update((list) => [...list, { id, kind, text }]);
    if (timeoutMs > 0) setTimeout(() => this.dismiss(id), timeoutMs);
  }

  error(text: string): void { this.show(text, 'error', 8000); }
  success(text: string): void { this.show(text, 'success', 4000); }
  info(text: string): void { this.show(text, 'info', 5000); }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
