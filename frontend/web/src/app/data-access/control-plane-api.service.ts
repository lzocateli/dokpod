import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  getContainerCommand,
  getEnvironment,
  listEnvironmentContainers,
  listEnvironments,
  registerEnvironment,
  submitContainerCommand,
  type ContainerCommandStatus,
  type ContainerInventory,
  type EnvironmentState,
  type EnvironmentPage,
  type InventoryPage,
  type RegisterEnvironmentRequest,
  type SubmitContainerCommandRequest,
} from './generated/control-plane';
import { client } from './generated/control-plane/client.gen';

export type ContainerAction = SubmitContainerCommandRequest['action'];

interface AntiforgeryResponse {
  requestToken: string;
}

@Injectable({ providedIn: 'root' })
export class ControlPlaneApiService {
  private readonly http = inject(HttpClient);

  constructor() {
    client.setConfig({
      baseUrl: document.baseURI.replace(/\/$/, ''),
      credentials: 'same-origin',
    });
    client.interceptors.error.use((error, response) => {
      if (!response) {
        return error;
      }

      return typeof error === 'object' && error !== null
        ? { ...error, status: response.status }
        : { status: response.status };
    });
  }

  async listContainers(environmentId: string, cursor?: string): Promise<InventoryPage> {
    const result = await listEnvironmentContainers({
      path: { environmentId },
      query: { cursor, limit: 50 },
      throwOnError: true,
    });

    return result.data;
  }

  async listEnvironments(cursor?: string): Promise<EnvironmentPage> {
    const result = await listEnvironments({
      query: { cursor, limit: 20 },
      throwOnError: true,
    });

    return result.data;
  }

  async registerEnvironment(request: RegisterEnvironmentRequest): Promise<string> {
    const { requestToken } = await firstValueFrom(
      this.http.get<AntiforgeryResponse>('bff/antiforgery'),
    );
    const result = await registerEnvironment({
      headers: { 'X-Dokpod-Antiforgery': requestToken },
      body: request,
      throwOnError: true,
    });

    return result.data.environmentId;
  }

  async getEnvironment(environmentId: string): Promise<EnvironmentState> {
    const result = await getEnvironment({
      path: { environmentId },
      throwOnError: true,
    });

    return result.data;
  }

  async getCommand(environmentId: string, commandId: string): Promise<ContainerCommandStatus> {
    const result = await getContainerCommand({
      path: { environmentId, commandId },
      throwOnError: true,
    });

    return result.data;
  }

  async submitCommand(
    environmentId: string,
    container: ContainerInventory,
    action: ContainerAction,
  ): Promise<string> {
    const { requestToken } = await firstValueFrom(
      this.http.get<AntiforgeryResponse>('bff/antiforgery'),
    );
    const accepted = await submitContainerCommand({
      path: { environmentId, containerId: container.containerId },
      headers: {
        'Idempotency-Key': crypto.randomUUID(),
        'X-Dokpod-Antiforgery': requestToken,
      },
      body: {
        action,
        expectedContainerRevision: container.revision,
        deadlineUtc: new Date(Date.now() + 2 * 60_000).toISOString(),
      },
      throwOnError: true,
    });

    return accepted.data.commandId;
  }
}