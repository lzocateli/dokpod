import { inject, Injectable, signal } from '@angular/core';
import { ControlPlaneApiService } from '../../data-access/control-plane-api.service';
import type {
  EnvironmentState,
  ProblemDetails,
  RegisterEnvironmentRequest,
} from '../../data-access/generated/control-plane';

export interface EnvironmentCatalogError {
  status: number | null;
  title: string;
  detail: string;
}

@Injectable()
export class EnvironmentCatalogFacade {
  private readonly api = inject(ControlPlaneApiService);

  readonly environments = signal<EnvironmentState[]>([]);
  readonly loading = signal(false);
  readonly loadingMore = signal(false);
  readonly registering = signal(false);
  readonly error = signal<EnvironmentCatalogError | null>(null);
  readonly registrationError = signal<EnvironmentCatalogError | null>(null);
  readonly nextCursor = signal<string | null>(null);

  async load(append = false): Promise<void> {
    append ? this.loadingMore.set(true) : this.loading.set(true);
    this.error.set(null);

    try {
      const page = await this.api.listEnvironments(
        append ? (this.nextCursor() ?? undefined) : undefined,
      );
      this.environments.update(current =>
        append ? [...current, ...page.environments] : page.environments,
      );
      this.nextCursor.set(page.nextCursor ?? null);
    } catch (error: unknown) {
      this.error.set(toCatalogError(error));
      if (!append) {
        this.environments.set([]);
      }
    } finally {
      this.loading.set(false);
      this.loadingMore.set(false);
    }
  }

  async register(request: RegisterEnvironmentRequest): Promise<string | null> {
    this.registering.set(true);
    this.registrationError.set(null);

    try {
      const environmentId = await this.api.registerEnvironment(request);
      await this.load();
      return environmentId;
    } catch (error: unknown) {
      this.registrationError.set(toRegistrationError(error));
      return null;
    } finally {
      this.registering.set(false);
    }
  }
}

function toCatalogError(error: unknown): EnvironmentCatalogError {
  const problem = toProblemDetails(error);
  const status = getErrorStatus(error);
  if (status === 401) {
    return {
      status: 401,
      title: 'Sessão expirada',
      detail: 'Entre novamente para consultar seus ambientes.',
    };
  }
  if (status === 503) {
    return {
      status: 503,
      title: 'Catálogo indisponível',
      detail: 'A autorização ou a persistência não respondeu. Tente novamente.',
    };
  }

  return {
    status,
    title: problem?.title ?? 'Falha ao carregar ambientes',
    detail: problem?.detail ?? 'Não foi possível consultar o catálogo de ambientes.',
  };
}

function toRegistrationError(error: unknown): EnvironmentCatalogError {
  const problem = toProblemDetails(error);
  const status = getErrorStatus(error);
  if (status === 403) {
    return {
      status: 403,
      title: 'Cadastro não autorizado',
      detail: 'Confirme se o ambiente e sua permissão de gestão foram provisionados.',
    };
  }
  if (status === 409) {
    return {
      status: 409,
      title: 'Ambiente já cadastrado',
      detail: 'Use outro identificador ou abra o ambiente existente no catálogo.',
    };
  }

  return toCatalogError(error);
}

function getErrorStatus(error: unknown): number | null {
  if (typeof error !== 'object' || error === null || !('status' in error)) {
    return null;
  }

  return typeof error.status === 'number' ? error.status : null;
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