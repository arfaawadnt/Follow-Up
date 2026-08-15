import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { LoginResult } from './models';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const result: LoginResult = {
    token: 'tok', expiresAt: new Date(Date.now() + 3600_000).toISOString(),
    username: 'admin', roleName: 'Admin', privileges: ['ManageUsers', 'ViewDashboard'],
    scope: { branches: ['*'], governorates: ['*'], cities: ['*'], areas: ['*'], categories: ['*'], segments: ['*'] },
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('starts unauthenticated', () => {
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.token).toBeNull();
  });

  it('stores the session and exposes privileges on login', () => {
    service.login('admin', 'pw').subscribe();
    httpMock.expectOne((r) => r.url.endsWith('/auth/login')).flush(result);

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.token).toBe('tok');
    expect(service.has('ManageUsers')).toBeTrue();
    expect(service.has('OracleIntegration')).toBeFalse();
  });

  it('clears state on logout', () => {
    service.login('admin', 'pw').subscribe();
    httpMock.expectOne((r) => r.url.endsWith('/auth/login')).flush(result);
    service.logout();
    httpMock.expectOne((r) => r.url.endsWith('/auth/logout')).flush({});
    expect(service.isAuthenticated()).toBeFalse();
  });
});
