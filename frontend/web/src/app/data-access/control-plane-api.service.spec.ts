import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ControlPlaneApiService } from './control-plane-api.service';

describe('ControlPlaneApiService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('lista ambientes autorizados com paginação', async () => {
    const fetchRequest = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ environments: [], nextCursor: null }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const service = TestBed.inject(ControlPlaneApiService);

    await expect(service.listEnvironments('cursor-1')).resolves.toEqual({
      environments: [],
      nextCursor: null,
    });
    expect((fetchRequest.mock.calls[0][0] as Request).url).toContain(
      '/api/v1/environments?cursor=cursor-1&limit=20',
    );
  });

  it('envia antiforgery ao cadastrar um ambiente', async () => {
    const fetchRequest = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ environmentId: '86ba281e-f3f8-4838-bde1-f1c6de4427ea' }), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const service = TestBed.inject(ControlPlaneApiService);

    const registration = service.registerEnvironment({
      environmentId: '86ba281e-f3f8-4838-bde1-f1c6de4427ea',
      name: 'Produção',
      host: 'docker-01',
      enabled: true,
      scopes: ['environment:read', 'environment:manage'],
    });
    TestBed.inject(HttpTestingController)
      .expectOne('bff/antiforgery')
      .flush({ requestToken: 'antiforgery-token' });

    await expect(registration).resolves.toBe('86ba281e-f3f8-4838-bde1-f1c6de4427ea');
    const request = fetchRequest.mock.calls[0][0] as Request;
    expect(request.headers.get('X-Dokpod-Antiforgery')).toBe('antiforgery-token');
    await expect(request.clone().json()).resolves.toEqual(
      expect.objectContaining({ name: 'Produção', host: 'docker-01' }),
    );
  });

  it('envia antiforgery e idempotência antes de submeter comando', async () => {
    const fetchRequest = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ commandId: 'command-1' }), {
        status: 202,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const service = TestBed.inject(ControlPlaneApiService);

    const command = service.submitCommand(
      'environment-1',
      {
        containerId: 'a'.repeat(64),
        name: 'gateway',
        imageReference: 'nginx:1.29',
        state: 'running',
        revision: 'revision-1',
        observedAt: '2026-09-21T12:00:00Z',
      },
      'restart',
    );
    TestBed.inject(HttpTestingController)
      .expectOne('bff/antiforgery')
      .flush({ requestToken: 'antiforgery-token' });

    await expect(command).resolves.toBe('command-1');
    const request = fetchRequest.mock.calls[0][0] as Request;
    expect(request.url).toContain('/api/v1/environments/environment-1/containers/');
    expect(request.headers.get('Idempotency-Key')).toEqual(expect.any(String));
    expect(request.headers.get('X-Dokpod-Antiforgery')).toBe('antiforgery-token');
    await expect(request.clone().json()).resolves.toEqual(
      expect.objectContaining({ action: 'restart', expectedContainerRevision: 'revision-1' }),
    );
  });
});