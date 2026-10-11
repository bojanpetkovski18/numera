import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';

interface PlaceholderData {
  readonly heading: string;
  readonly description: string;
}

@Component({
  selector: 'app-placeholder-page',
  template: `
    <h1>{{ page().heading }}</h1>
    <p>{{ page().description }}</p>
  `,
  styles: `
    h1 {
      font-size: var(--font-size-xl);
    }
    p {
      color: var(--color-muted);
      margin-top: var(--space-2);
    }
  `,
})
export class PlaceholderPage {
  private readonly route = inject(ActivatedRoute);
  private readonly routeData = toSignal(this.route.data, { initialValue: this.route.snapshot.data });

  protected readonly page = computed<PlaceholderData>(() => {
    const data = this.routeData();
    return { heading: String(data['heading'] ?? ''), description: String(data['description'] ?? '') };
  });
}
