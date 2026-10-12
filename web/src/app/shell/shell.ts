import { Component, ElementRef, Injector, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
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
  host: { '(document:keydown.escape)': 'closeNavFromKeyboard()' },
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');
  private readonly menuButton = viewChild.required<ElementRef<HTMLButtonElement>>('menuButton');
  protected readonly navOpen = signal(false);

  protected readonly navItems: readonly NavItem[] = [
    { label: 'Dashboard', path: '/dashboard' },
    { label: 'Orders', path: '/orders' },
    { label: 'Import', path: '/import' },
    { label: 'Settings', path: '/settings' },
  ];

  constructor() {
    // A single-page app does not announce a new page by itself. When the page (not just its query string) changes,
    // focus moves to the main content, so screen readers read the new page and Tab continues from there.
    // The shell itself appears after signing in: that is a page change too. On a fresh page load (no earlier
    // navigation) focus stays at the top of the document, where screen readers start anyway.
    if (this.router.currentNavigation()?.previousNavigation) {
      afterNextRender(() => this.focusMain(), { injector: this.injector });
    }
    let lastPath = pathOf(this.router.url);
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => {
        const path = pathOf(event.urlAfterRedirects);
        if (path !== lastPath) {
          lastPath = path;
          afterNextRender(() => this.focusMain(), { injector: this.injector });
        }
      });
  }

  protected toggleNav(): void {
    this.navOpen.update((open) => !open);
  }

  protected logout(): void {
    this.auth.logout();
  }

  protected closeNav(): void {
    this.navOpen.set(false);
  }

  protected closeNavFromKeyboard(): void {
    if (this.navOpen()) {
      this.navOpen.set(false);
      this.menuButton().nativeElement.focus();
    }
  }

  protected skipToMain(event: Event): void {
    event.preventDefault(); // a #fragment link would change the URL; only focus is wanted
    this.focusMain();
  }

  private focusMain(): void {
    this.main().nativeElement.focus({ preventScroll: true });
  }
}

function pathOf(url: string): string {
  return url.split(/[?#]/)[0];
}
