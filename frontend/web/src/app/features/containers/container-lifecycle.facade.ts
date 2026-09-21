import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import {
  ContainerAction,
  ControlPlaneApiService,
} from '../../data-access/control-plane-api.service';
import type {
  ContainerCommandStatus,
  ContainerInventory,
  EnvironmentState,
  ProblemDetails,
} from '../../data-access/generated/control-plane';

export interface ContainerOperation {
  action: ContainerAction;
  state: ContainerCommandStatus['state'];
  failureCode: string | null;
}

export interface InventoryError {
  status: number | null;
  title: string;
  detail: string;
}

const terminalStates = new Set<ContainerCommandStatus['state']>([
  'succeeded',
  'failed',
  'indeterminate',
]);

@Injectable()
export class ContainerLifecycleFacade {
  private readonly api = inject(ControlPlaneApiService);
  private readonly destroyRef = inject(DestroyRef);
  private requestVersion = 0;
  private environmentVersion = 0;
  private activeEnvironmentId: string | null = null;
  private destroyed = false;

  readonly containers = signal<ContainerInventory[]>([]);
  readonly operations = signal<Record<string, ContainerOperation>>({});
  readonly loading = signal(false);
  readonly loadingMore = signal(false);
  readonly error = signal<InventoryError | null>(null);
  readonly observedAt = signal<string | null>(null);
  readonly ageSeconds = signal(0);
  readonly nextCursor = signal<string | null>(null);
  readonly environment = signal<EnvironmentState | null>(null);

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.requestVersion += 1;
    });
  }

  async load(environmentId: string, append = false): Promise<void> {
    if (this.activeEnvironmentId !== environmentId) {
      this.activeEnvironmentId = environmentId;
      this.environmentVersion += 1;
      this.containers.set([]);
      this.operations.set({});
      this.environment.set(null);
    }
    const version = ++this.requestVersion;
    append ? this.loadingMore.set(true) : this.loading.set(true);
    this.error.set(null);

    try {
      const [environment, page] = await Promise.all([
        append ? Promise.resolve(this.environment()) : this.api.getEnvironment(environmentId),
        this.api.listContainers(
          environmentId,
          append ? (this.nextCursor() ?? undefined) : undefined,
        ),
      ]);
      if (version !== this.requestVersion || this.destroyed) {
        return;
      }

      this.environment.set(environment);
      this.containers.update(current => (append ? [...current, ...page.containers] : page.containers));
      this.observedAt.set(page.observedAt);
      this.ageSeconds.set(page.ageSeconds);
      this.nextCursor.set(page.nextCursor ?? null);
    } catch (error: unknown) {
      if (version === this.requestVersion) {
        this.error.set(toInventoryError(error));
        if (!append) {
          this.containers.set([]);
        }
      }
    } finally {
      if (version === this.requestVersion) {
        this.loading.set(false);
        this.loadingMore.set(false);
      }
    }
  }

  async run(
    environmentId: string,
    container: ContainerInventory,
    action: ContainerAction,
  ): Promise<void> {
    const currentOperation = this.operations()[container.containerId];
    if (currentOperation && !terminalStates.has(currentOperation.state)) {
      return;
    }

    const environmentVersion = this.environmentVersion;
    this.setOperation(container.containerId, { action, state: 'pending', failureCode: null });
    try {
      const commandId = await this.api.submitCommand(environmentId, container, action);
      await this.poll(environmentId, container.containerId, commandId, action, environmentVersion);
    } catch (error: unknown) {
      const problem = toProblemDetails(error);
      this.setOperation(container.containerId, {
        action,
        state: 'failed',
        failureCode: problem?.code ?? 'command_submission_failed',
      });
    }
  }

  clearOperation(containerId: string): void {
    this.operations.update(current => {
      const next = { ...current };
      delete next[containerId];
      return next;
    });
  }

  private async poll(
    environmentId: string,
    containerId: string,
    commandId: string,
    action: ContainerAction,
    environmentVersion: number,
  ): Promise<void> {
    while (!this.destroyed && environmentVersion === this.environmentVersion) {
      const status = await this.api.getCommand(environmentId, commandId);
      this.setOperation(containerId, {
        action,
        state: status.state,
        failureCode: status.failureCode,
      });

      if (terminalStates.has(status.state)) {
        if (environmentVersion === this.environmentVersion) {
          await this.load(environmentId);
        }
        return;
      }

      await delay(1_000);
    }
  }

  private setOperation(containerId: string, operation: ContainerOperation): void {
    this.operations.update(current => ({ ...current, [containerId]: operation }));
  }
}

function delay(milliseconds: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function toInventoryError(error: unknown): InventoryError {
  const problem = toProblemDetails(error);
  if (problem?.status === 403) {
    return { status: 403, title: 'Acesso negado', detail: 'Você não pode consultar este ambiente.' };
  }
  if (problem?.status === 503) {
    return {
      status: 503,
      title: 'Plano de controle indisponível',
      detail: 'A autorização ou a persistência não respondeu. Tente novamente.',
    };
  }

  return {
    status: problem?.status ?? null,
    title: problem?.title ?? 'Falha ao carregar inventário',
    detail: problem?.detail ?? 'Não foi possível consultar os containers deste ambiente.',
  };
}

function toProblemDetails(error: unknown): ProblemDetails | null {
  if (typeof error !== 'object' || error === null) {
    return null;
  }

  const candidate = error as Partial<ProblemDetails>;
  return typeof candidate.title === 'string' && typeof candidate.detail === 'string'
    ? (candidate as ProblemDetails)
    : null;
}