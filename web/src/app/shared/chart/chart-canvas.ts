import { Component, ElementRef, OnDestroy, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { ChartConfiguration } from 'chart.js';
import { CHART_FACTORY, ChartHandle } from './chart-factory';

let nextId = 0;

/**
 * A responsive Chart.js canvas. The canvas is exposed to assistive technology as an image with a text summary;
 * each chart also renders its data as a table (see the widgets), so no information is only visual.
 *
 * With `items` (one description per data item) the chart becomes selectable: a click on an item, or the arrow keys
 * followed by Enter while the canvas has focus, emits `selectItem` with the item's index.
 */
@Component({
  selector: 'app-chart-canvas',
  template: `
    <div class="chart-box">
      <canvas
        #canvas
        role="img"
        [attr.aria-label]="label()"
        [attr.tabindex]="interactive() ? 0 : null"
        [attr.aria-describedby]="interactive() ? hintId : null"
        (keydown)="onKeydown($event)"
        (blur)="setActive(null)"
      ></canvas>
    </div>
    @if (interactive()) {
      <p class="visually-hidden" [id]="hintId">{{ hint() }}</p>
      <p class="visually-hidden" aria-live="polite">{{ announcement() }}</p>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .chart-box {
      position: relative;
      height: var(--chart-height, 18rem);
    }
  `,
})
export class ChartCanvas implements OnDestroy {
  readonly config = input.required<ChartConfiguration>();
  readonly label = input.required<string>();
  /** One text per data item, announced during keyboard navigation. Empty means the chart is not selectable. */
  readonly items = input<readonly string[]>([]);
  readonly hint = input('Use the arrow keys to choose an item, then press Enter to open it.');
  readonly selectItem = output<number>();

  protected readonly hintId = `chart-hint-${nextId++}`;
  protected readonly interactive = computed(() => this.items().length > 0);
  private readonly active = signal<number | null>(null);
  protected readonly announcement = computed(() => {
    const index = this.active();
    return index === null ? '' : `${this.items()[index] ?? ''}. Press Enter to open.`;
  });

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly factory = inject(CHART_FACTORY);
  private chart: ChartHandle | null = null;

  /** The widget's config plus click and hover handling when the chart is selectable. */
  private readonly chartConfig = computed<ChartConfiguration>(() => {
    const config = this.config();
    if (!this.interactive()) {
      return config;
    }
    return {
      ...config,
      options: {
        ...config.options,
        onClick: (_event, elements) => {
          if (elements.length > 0) {
            this.selectItem.emit(elements[0].index);
          }
        },
        onHover: (_event, elements) => {
          this.canvas().nativeElement.style.cursor = elements.length > 0 ? 'pointer' : 'default';
        },
      },
    };
  });

  constructor() {
    effect(() => {
      const config = this.chartConfig();
      if (this.chart === null) {
        this.chart = this.factory(this.canvas().nativeElement, config);
      } else {
        this.chart.update(config);
        this.active.set(null); // the data changed; the highlighted index may point elsewhere now
      }
    });
  }

  protected setActive(index: number | null): void {
    if (this.active() === index) {
      return;
    }
    this.active.set(index);
    this.chart?.highlight(index);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.items().length;
    if (count === 0) {
      return;
    }
    const current = this.active();
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        this.setActive(current === null ? 0 : Math.min(current + 1, count - 1));
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        this.setActive(current === null ? 0 : Math.max(current - 1, 0));
        break;
      case 'Home':
        this.setActive(0);
        break;
      case 'End':
        this.setActive(count - 1);
        break;
      case 'Enter':
      case ' ':
        if (current === null) {
          return;
        }
        this.selectItem.emit(current);
        break;
      case 'Escape':
        this.setActive(null);
        return;
      default:
        return;
    }
    event.preventDefault(); // arrow keys and space would otherwise scroll the page
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
    this.chart = null;
  }
}
