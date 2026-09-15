import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';

interface BffSession {
  authenticated: boolean;
  subject?: string | null;
  name?: string | null;
}

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);

  protected readonly session = signal<BffSession | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.loadSession();
  }

  protected loadSession(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.http.get<BffSession>('bff/session').subscribe({
      next: session => {
        this.session.set(session);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar a sessão do BFF.');
        this.session.set(null);
        this.loading.set(false);
      },
    });
  }

  protected login(): void {
    const returnUrl = `${window.location.pathname}${window.location.search}`;
    window.location.assign(`bff/login?returnUrl=${encodeURIComponent(returnUrl)}`);
  }
}
