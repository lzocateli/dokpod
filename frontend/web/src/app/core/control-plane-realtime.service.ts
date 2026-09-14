import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';

export type ControlPlaneRealtimeStatus = 'disconnected' | 'connecting' | 'connected' | 'error';

@Injectable({ providedIn: 'root' })
export class ControlPlaneRealtimeService {
  readonly status = signal<ControlPlaneRealtimeStatus>('disconnected');

  private connection: HubConnection | null = null;
  private generation = 0;

  async joinEnvironment(environmentId: string): Promise<void> {
    const generation = ++this.generation;
    await this.stopConnection();

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/control-plane', { withCredentials: true })
      .withAutomaticReconnect()
      .build();

    connection.onreconnecting(() => {
      if (generation === this.generation) this.status.set('connecting');
    });
    connection.onreconnected(() => void this.rejoinEnvironment(connection, environmentId, generation));
    connection.onclose(() => {
      if (generation === this.generation) this.status.set('disconnected');
    });

    this.connection = connection;
    this.status.set('connecting');

    try {
      await connection.start();
      if (generation !== this.generation) {
        await connection.stop();
        return;
      }
      await connection.invoke('JoinEnvironmentAsync', environmentId);
      this.status.set('connected');
    } catch (error) {
      if (generation !== this.generation) return;
      this.status.set('error');
      await this.stopConnection();
      throw error;
    }
  }

  async disconnect(): Promise<void> {
    this.generation++;
    await this.stopConnection();
    this.status.set('disconnected');
  }

  private async stopConnection(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    if (connection && connection.state !== HubConnectionState.Disconnected) {
      await connection.stop();
    }
  }

  private async rejoinEnvironment(
    connection: HubConnection,
    environmentId: string,
    generation: number,
  ): Promise<void> {
    if (generation !== this.generation) return;
    try {
      await connection.invoke('JoinEnvironmentAsync', environmentId);
      if (generation === this.generation) this.status.set('connected');
    } catch {
      if (generation === this.generation) this.status.set('error');
    }
  }
}
