import { Component, ElementRef, OnDestroy, effect, inject, input, viewChild } from '@angular/core';
import { ChartConfiguration } from 'chart.js';
import { CHART_FACTORY, ChartHandle } from './chart-factory';

/**
 * A responsive Chart.js canvas. The canvas is exposed to assistive technology as an image with a text summary;
 * each chart also renders its data as a table (see the widgets), so no information is only visual.
 */
@Component({
  selector: 'app-chart-canvas',
  template: `<div class="chart-box"><canvas #canvas role="img" [attr.aria-label]="label()"></canvas></div>`,
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

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly factory = inject(CHART_FACTORY);
  private chart: ChartHandle | null = null;

  constructor() {
    effect(() => {
      const config = this.config();
      if (this.chart === null) {
        this.chart = this.factory(this.canvas().nativeElement, config);
      } else {
        this.chart.update(config);
      }
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
    this.chart = null;
  }
}