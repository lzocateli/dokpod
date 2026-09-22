import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should sign out through the BFF with antiforgery validation', async () => {
    const httpMock = TestBed.inject(HttpTestingController);

    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    app.ngOnInit();

    httpMock.expectOne('bff/session').flush({ authenticated: true, subject: 'user-1', name: 'User' });

    const assignSpy = vi.fn();
    vi.stubGlobal('location', { ...window.location, assign: assignSpy });

    const logoutPromise = (app as any).logout();

    const antiforgeryRequest = httpMock.expectOne('bff/antiforgery');
    expect(antiforgeryRequest.request.method).toBe('GET');
    antiforgeryRequest.flush({ requestToken: 'csrf-token' });

    await Promise.resolve();

    const logoutRequest = httpMock.expectOne('bff/logout');
    expect(logoutRequest.request.method).toBe('POST');
    expect(logoutRequest.request.headers.get('X-Dokpod-Antiforgery')).toBe('csrf-token');
    logoutRequest.flush(null);

    await logoutPromise;

    expect(assignSpy).toHaveBeenCalledWith('/');

    vi.unstubAllGlobals();
    httpMock.verify();
  });
});
