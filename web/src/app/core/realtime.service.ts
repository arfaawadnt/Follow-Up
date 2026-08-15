import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

/**
 * SignalR client for the notifications hub (ADR-0003). The token is supplied via the access-token factory
 * (never in the URL by hand). Messages are treated as hints — <c>dataChange</c> bumps a signal that feature
 * screens watch to re-fetch through the normal scope-enforced query path.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);
  private connection?: HubConnection;

  readonly connected = signal(false);
  /** Increments on each server hint; screens can `effect(() => { this.rt.tick(); this.reload(); })`. */
  readonly tick = signal(0);
  readonly lastNotification = signal<string | null>(null);

  async start(): Promise<void> {
    if (this.connection || !this.auth.isAuthenticated()) return;

    this.connection = new HubConnectionBuilder()
      .withUrl(`${environment.hubBase}/notifications`, { accessTokenFactory: () => this.auth.token ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('dataChange', () => this.tick.update((t) => t + 1));
    this.connection.on('notification', (title: string) => { this.lastNotification.set(title); this.tick.update((t) => t + 1); });
    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => this.connected.set(false));

    try {
      await this.connection.start();
      this.connected.set(true);
    } catch {
      this.connected.set(false);
    }
  }

  async stop(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      await this.connection.stop();
    }
    this.connection = undefined;
    this.connected.set(false);
  }
}
