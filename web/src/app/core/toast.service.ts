import { Injectable, signal } from '@angular/core';

export type ToastKind = 'error' | 'success' | 'warning' | 'info';
export interface Toast { id: number; kind: ToastKind; text: string; }

/**
 * Transient, dismissible system notifications shown app-wide (host mounted once at the root). Fed by the HTTP
 * error interceptor (so failed requests are never silent) and used by every page for success / warning /
 * error confirmations. Signal-based so the host renders reactively; each toast auto-dismisses after 5s.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private seq = 0;

  /** Default display time for every system message (ms). */
  static readonly DEFAULT_MS = 5000;

  show(text: string, kind: ToastKind = 'info', timeoutMs = ToastService.DEFAULT_MS): void {
    const clean = (text ?? '').toString().trim();
    if (!clean) return;
    const id = ++this.seq;
    this.toasts.update((list) => [...list, { id, kind, text: clean }]);
    if (timeoutMs > 0) setTimeout(() => this.dismiss(id), timeoutMs);
  }

  /** A failure the user must notice (server/network error). */
  error(text: string): void { this.show(text, 'error'); }
  /** A completed action (saved, deleted, synced…). */
  success(text: string): void { this.show(text, 'success'); }
  /** Missing/invalid data or a soft problem the user should fix (validation, nothing found). */
  warning(text: string): void { this.show(text, 'warning'); }
  /** Neutral information. */
  info(text: string): void { this.show(text, 'info'); }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
