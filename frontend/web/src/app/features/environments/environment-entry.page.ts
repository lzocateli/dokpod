import { Component, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import type { RegisterEnvironmentRequest } from '../../data-access/generated/control-plane';
import { EnvironmentCatalogFacade } from './environment-catalog.facade';

const environmentScopes: RegisterEnvironmentRequest['scopes'] = [
  'environment:read',
  'environment:manage',
  'container:start',
  'container:stop',
  'container:restart',
  'container:delete',
  'audit:read',
];

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  providers: [EnvironmentCatalogFacade],
  selector: 'app-environment-entry-page',
  templateUrl: './environment-entry.page.html',
  styleUrl: './environment-entry.page.css',
})
export class EnvironmentEntryPage implements OnInit {
  protected readonly facade = inject(EnvironmentCatalogFacade);
  protected readonly registrationOpen = signal(false);
  protected readonly registeredEnvironmentId = signal<string | null>(null);
  protected readonly registrationForm = new FormGroup({
    environmentId: new FormControl(crypto.randomUUID(), {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(uuidPattern)],
    }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(128)],
    }),
    host: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(255)],
    }),
    enabled: new FormControl(true, { nonNullable: true }),
  });

  ngOnInit(): void {
    void this.facade.load();
  }

  protected openRegistration(): void {
    this.registeredEnvironmentId.set(null);
    this.registrationOpen.set(true);
  }

  protected closeRegistration(): void {
    this.registrationOpen.set(false);
    this.facade.registrationError.set(null);
  }

  protected async register(): Promise<void> {
    if (this.registrationForm.invalid) {
      this.registrationForm.markAllAsTouched();
      return;
    }

    const environmentId = await this.facade.register({
      ...this.registrationForm.getRawValue(),
      scopes: environmentScopes,
    });
    if (!environmentId) {
      return;
    }

    this.registeredEnvironmentId.set(environmentId);
    this.registrationOpen.set(false);
    this.registrationForm.reset({
      environmentId: crypto.randomUUID(),
      name: '',
      host: '',
      enabled: true,
    });
  }

  protected signIn(): void {
    const returnUrl = `${window.location.pathname}${window.location.search}`;
    window.location.assign(`bff/login?returnUrl=${encodeURIComponent(returnUrl)}`);
  }
}