import { TestBed } from '@angular/core/testing';
import { ControlPlaneApiService } from '../../data-access/control-plane-api.service';
import type { EnvironmentState } from '../../data-access/generated/control-plane';
import { EnvironmentCatalogFacade } from './environment-catalog.facade';

const environment: EnvironmentState = {
  environmentId: '86ba281e-f3f8-4838-bde1-f1c6de4427ea',
  name: 'Produção',
  host: 'docker-01',
  enabled: true,
  scopes: ['environment:read', 'environment:manage'],
};

describe('EnvironmentCatalogFacade', () => {
  const api = {
    listEnvironments: vi.fn(),
    registerEnvironment: vi.fn(),
  };

  beforeEach(() => {
    vi.resetAllMocks();
    api.listEnvironments.mockResolvedValue({ environments: [environment], nextCursor: null });

    TestBed.configureTestingModule({
      providers: [
        EnvironmentCatalogFacade,
        { provide: ControlPlaneApiService, useValue: api },
      ],
    });
  });

  it('carrega somente os ambientes devolvidos pelo catálogo autorizado', async () => {
    const facade = TestBed.inject(EnvironmentCatalogFacade);

    await facade.load();

    expect(facade.environments()).toEqual([environment]);
    expect(facade.loading()).toBe(false);
  });

  it('traduz sessão expirada em ação compreensível', async () => {
    api.listEnvironments.mockRejectedValue({ status: 401 });
    const facade = TestBed.inject(EnvironmentCatalogFacade);

    await facade.load();

    expect(facade.error()).toEqual({
      status: 401,
      title: 'Sessão expirada',
      detail: 'Entre novamente para consultar seus ambientes.',
    });
  });

  it('cadastra e atualiza o catálogo', async () => {
    api.registerEnvironment.mockResolvedValue(environment.environmentId);
    const facade = TestBed.inject(EnvironmentCatalogFacade);

    const result = await facade.register({
      environmentId: environment.environmentId,
      name: environment.name,
      host: environment.host,
    });

    expect(result).toBe(environment.environmentId);
    expect(api.listEnvironments).toHaveBeenCalledOnce();
    expect(facade.registrationError()).toBeNull();
  });
});