import { Routes } from '@angular/router';

export const routes: Routes = [
	{
		path: '',
		loadComponent: () => import('./features/environments/environment-entry.page').then(module => module.EnvironmentEntryPage),
	},
	{
		path: 'environments/:environmentId/containers',
		loadComponent: () => import('./features/containers/container-list.page').then(module => module.ContainerListPage),
	},
	{ path: '**', redirectTo: '' },
];
