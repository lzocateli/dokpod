import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { ContainerAction } from '../../data-access/control-plane-api.service';
import type { ContainerInventory } from '../../data-access/generated/control-plane';
import { ControlPlaneRealtimeService } from '../../core/control-plane-realtime.service';
import { ContainerLifecycleFacade } from './container-lifecycle.facade';

const actionScopes = {
  start: 'container:start',
  stop: 'container:stop',
  restart: 'container:restart',
  delete: 'container:delete',
} as const;

@Component({
  imports: [DatePipe, RouterLink],
  providers: [ContainerLifecycleFacade],
  selector: 'app-container-list-page',
  templateUrl: './container-list.page.html',
  styleUrl: './container-list.page.css',
})
export class ContainerListPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly realtime = inject(ControlPlaneRealtimeService);
  protected readonly facade = inject(ContainerLifecycleFacade);
  protected readonly environmentId = signal('');
  protected readonly pendingDelete = signal<ContainerInventory | null>(null);
  protected readonly stale = computed(() => this.facade.ageSeconds() >= 60);

  constructor() {
    effect(() => {
      const notification = this.realtime.inventoryChanged();
      const environmentId = this.environmentId();
      if (notification?.environmentId === environmentId) {
        void this.facade.load(environmentId);
      }
    });
    this.destroyRef.onDestroy(() => void this.realtime.disconnect());
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const environmentId = params.get('environmentId') ?? '';
      this.environmentId.set(environmentId);
      void this.facade.load(environmentId);
      void this.realtime.joinEnvironment(environmentId).catch(() => undefined);
    });
  }

  protected can(action: ContainerAction): boolean {
    return this.facade.environment()?.scopes.includes(actionScopes[action]) ?? false;
  }

  protected isBusy(containerId: string): boolean {
    const state = this.facade.operations()[containerId]?.state;
    return state === 'pending' || state === 'dispatched' || state === 'accepted';
  }

  protected run(container: ContainerInventory, action: ContainerAction): void {
    if (action === 'delete') {
      this.pendingDelete.set(container);
      return;
    }
    void this.facade.run(this.environmentId(), container, action);
  }

  protected confirmDelete(): void {
    const container = this.pendingDelete();
    if (!container) {
      return;
    }
    this.pendingDelete.set(null);
    void this.facade.run(this.environmentId(), container, 'delete');
  }
}