import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit } from '@angular/core';
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

  protected session: BffSession | null = null;
  protected loading = true;
  protected errorMessage: string | null = null;

  ngOnInit(): void {
    this.loadSession();
  }

  protected loadSession(): void {
    this.loading = true;
    this.errorMessage = null;

    this.http.get<BffSession>('/bff/session').subscribe({
      next: session => {
        this.session = session;
        this.loading = false;
      },
      error: () => {
        this.errorMessage = 'Não foi possível carregar a sessão do BFF.';
        this.session = null;
        this.loading = false;
      },
    });
  }

  protected login(): void {
    window.location.assign('/bff/login?returnUrl=/');
  }
}
