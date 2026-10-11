import { InjectionToken } from '@angular/core';
import {
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  ChartConfiguration,
  Filler,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
} from 'chart.js';

/** The handful of Chart.js parts the dashboard uses (keeps the bundle small). */
Chart.register(BarController, BarElement, CategoryScale, Filler, LineController, LineElement, LinearScale, PointElement, Tooltip);

export interface ChartHandle {
  update(config: ChartConfiguration): void;
  /** Shows the hover state and tooltip for one item (keyboard navigation); null clears it. */
  highlight(index: number | null): void;
  destroy(): void;
}

export type ChartFactory = (canvas: HTMLCanvasElement, config: ChartConfiguration) => ChartHandle;

/** Creates charts. A token so component tests can run without a canvas implementation. */
export const CHART_FACTORY = new InjectionToken<ChartFactory>('CHART_FACTORY', {
  providedIn: 'root',
  factory: () => (canvas, config) => {
    const chart = new Chart(canvas, config);
    return {
      update(next) {
        chart.data = next.data;
        chart.options = next.options ?? {};
        chart.update();
      },
      highlight(index) {
        const active = index === null ? [] : chart.data.datasets.map((_, datasetIndex) => ({ datasetIndex, index }));
        chart.setActiveElements(active);
        chart.tooltip?.setActiveElements(active, { x: 0, y: 0 });
        chart.update();
      },
      destroy() {
        chart.destroy();
      },
    };
  },
});