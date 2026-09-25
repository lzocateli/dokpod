import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';

export type ControlPlaneRealtimeStatus = 'disconnected' | 'connecting' | 'connected' | 'error';
export interface InventoryChangedNotification {
  environmentId: string;
  revision: number;
}

interface AntiforgeryResponse {
  requestToken: string;
}

export function controlPlaneHubUrl(baseUri: string): string {
  return `${baseUri.replace(/\/$/, '')}/hubs/control-plane`;
}

@Injectable({ providedIn: 'root' })
export class ControlPlaneRealtimeService {
  private readonly http = inject(HttpClient);
  readonly status = signal<ControlPlaneRealtimeStatus>('disconnected');
  readonly inventoryChanged = signal<InventoryChangedNotification | null>(null);

  private connection: HubConnection | null = null;
  private generation = 0;

  async joinEnvironment(environmentId: string): Promise<void> {
    const generation = ++this.generation;
    await this.stopConnection();
    const { requestToken } = await firstValueFrom(
      this.http.get<AntiforgeryResponse>('bff/antiforgery'),
    );

    const connection = new HubConnectionBuilder()
      .withUrl(controlPlaneHubUrl(document.baseURI), {
        headers: { 'X-Dokpod-Antiforgery': requestToken },
        withCredentials: true,
      })
      .withAutomaticReconnect()
      .build();

    connection.onreconnecting(() => {
      if (generation === this.generation) this.status.set('connecting');
    });
    connection.onreconnected(() => void this.rejoinEnvironment(connection, environmentId, generation));
    connection.onclose(() => {
      if (generation === this.generation && this.status() !== 'error') {
        this.status.set('disconnected');
      }
    });
    connection.on('inventoryChanged', (notification: InventoryChangedNotification) => {
      if (generation === this.generation) this.inventoryChanged.set(notification);
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
