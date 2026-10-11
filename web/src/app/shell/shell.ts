import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

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

  protected closeNav(): void {
    this.navOpen.set(false);
  }
}
