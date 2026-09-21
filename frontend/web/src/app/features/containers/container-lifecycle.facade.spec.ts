import { TestBed } from '@angular/core/testing';
import { ControlPlaneApiService } from '../../data-access/control-plane-api.service';
import type {
  ContainerCommandStatus,
  ContainerInventory,
  EnvironmentState,
  InventoryPage,
} from '../../data-access/generated/control-plane';
import { ContainerLifecycleFacade } from './container-lifecycle.facade';

const container: ContainerInventory = {
  containerId: 'a'.repeat(64),
  name: 'gateway',
  imageReference: 'nginx:1.29',
  state: 'running',
  revision: 'revision-1',
  observedAt: '2026-09-21T12:00:00Z',
};

const environment: EnvironmentState = {
  environmentId: 'environment-1',
  name: 'Produção',
  host: 'docker-01',
  enabled: true,
  scopes: ['environment:read', 'container:restart'],
};

const page: InventoryPage = {
  environmentId: environment.environmentId,
  revision: 4,
  observedAt: '2026-09-21T12:00:00Z',
  ageSeconds: 5,
  containers: [container],
  nextCursor: null,
};

const succeededCommand: ContainerCommandStatus = {
  environmentId: environment.environmentId,
  commandId: 'command-1',
  action: 'restart',
  containerId: container.containerId,
  expectedContainerRevision: container.revision,
  state: 'succeeded',
  failureCode: null,
  observedContainerRevision: 'revision-2',
  deadlineUtc: '2026-09-21T12:02:00Z',
  createdAtUtc: '2026-09-21T12:00:00Z',
  updatedAtUtc: '2026-09-21T12:00:01Z',
  completedAtUtc: '2026-09-21T12:00:01Z',
};

describe('ContainerLifecycleFacade', () => {
  const api = {
    getEnvironment: vi.fn(),
    listContainers: vi.fn(),
    submitCommand: vi.fn(),
    getCommand: vi.fn(),
  };

  beforeEach(() => {
    vi.resetAllMocks();
    api.getEnvironment.mockResolvedValue(environment);
    api.listContainers.mockResolvedValue(page);

    TestBed.configureTestingModule({
      providers: [
        ContainerLifecycleFacade,
        { provide: ControlPlaneApiService, useValue: api },
      ],
    });
  });

  it('carrega cadastro e inventário do ambiente', async () => {
    const facade = TestBed.inject(ContainerLifecycleFacade);

    await facade.load(environment.environmentId);

    expect(facade.environment()).toEqual(environment);
    expect(facade.containers()).toEqual([container]);
    expect(facade.ageSeconds()).toBe(5);
    expect(facade.loading()).toBe(false);
  });

  it('traduz negação de autorização em estado operacional', async () => {
    api.getEnvironment.mockRejectedValue({
      status: 403,
      title: 'Forbidden',
      detail: 'Denied',
    });
    const facade = TestBed.inject(ContainerLifecycleFacade);

    await facade.load(environment.environmentId);

    expect(facade.error()).toEqual({
      status: 403,
      title: 'Acesso negado',
      detail: 'Você não pode consultar este ambiente.',
    });
    expect(facade.containers()).toEqual([]);
  });

  it('acompanha o comando até o estado terminal e atualiza o inventário', async () => {
    api.submitCommand.mockResolvedValue('command-1');
    api.getCommand.mockResolvedValue(succeededCommand);
    const facade = TestBed.inject(ContainerLifecycleFacade);
    await facade.load(environment.environmentId);

    await facade.run(environment.environmentId, container, 'restart');

    expect(api.submitCommand).toHaveBeenCalledWith(environment.environmentId, container, 'restart');
    expect(api.getCommand).toHaveBeenCalledWith(environment.environmentId, 'command-1');
    expect(api.listContainers).toHaveBeenCalledTimes(2);
    expect(facade.operations()[container.containerId]).toEqual({
      action: 'restart',
      state: 'succeeded',
      failureCode: null,
    });
  });
});