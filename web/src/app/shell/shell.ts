import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';

interface NavItem {
  readonly label: string;
  readonly path: string;
}

@Component({
  selector: 'app-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  private readonly auth = inject(AuthService);
  protected readonly navOpen = signal(false);

  protected readonly navItems: readonly NavItem[] = [
    { label: 'Dashboard', path: '/dashboard' },
    { label: 'Orders', path: '/orders' },
    { label: 'Import', path: '/import' },
    { label: 'Settings', path: '/settings' },
  ];

  protected toggleNav(): void {
    this.navOpen.update((open) => !open);
  }

  protected logout(): void {
    this.auth.logout();
  }

  protected closeNav(): void {
    this.navOpen.set(false);
  }
}

