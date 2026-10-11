import { convertToParamMap } from '@angular/router';
import { DEFAULT_GRID, GridState, effectiveSort, gridApiParams, gridFromQueryParams, gridToQueryParams, nextSort } from './grid-state';

function roundTrip(state: GridState): GridState {
  const query = Object.fromEntries(
    Object.entries(gridToQueryParams(state)).filter((entry): entry is [string, string | string[]] => entry[1] !== null),
  );
  return gridFromQueryParams(convertToParamMap(query));
}

describe('grid state', () => {
  it('defaults to newest first, page 1, 25 rows, and keeps the URL clean', () => {
    expect(effectiveSort(DEFAULT_GRID)).toEqual({ field: 'orderDate', dir: 'desc' });
    expect(Object.values(gridToQueryParams(DEFAULT_GRID)).every((v) => v === null)).toBe(true);
  });

  it('round-trips through the URL', () => {
    const states: GridState[] = [
      DEFAULT_GRID,
      { statuses: ['refunded', 'cancelled'], q: 'HP-0001', sort: 'lineTotal', dir: 'desc', page: 4, pageSize: 50 },
      { ...DEFAULT_GRID, sort: 'region', dir: 'asc' },
    ];
    for (const state of states) {
      expect(roundTrip(state)).toEqual(state);
    }
  });

  it('ignores invalid URL values', () => {
    const state = gridFromQueryParams(
      convertToParamMap({ status: ['shipped', 'refunded'], sort: 'password', dir: 'up', page: '-2', pageSize: '1000' }),
    );

    expect(state).toEqual({ ...DEFAULT_GRID, statuses: ['refunded'] });
  });

  it('header clicks flip the current column and pick a sensible first direction for a new one', () => {
    const byDateAsc = nextSort(DEFAULT_GRID, 'orderDate');
    expect(effectiveSort(byDateAsc)).toEqual({ field: 'orderDate', dir: 'asc' });

    expect(effectiveSort(nextSort(byDateAsc, 'region'))).toEqual({ field: 'region', dir: 'asc' });
    expect(effectiveSort(nextSort(byDateAsc, 'lineTotal'))).toEqual({ field: 'lineTotal', dir: 'desc' });
    expect(nextSort({ ...DEFAULT_GRID, page: 7 }, 'status').page).toBe(1);
  });

  it('builds API params including paging, and only sends sort when chosen', () => {
    expect(gridApiParams(DEFAULT_GRID)).toEqual({ page: '1', pageSize: '25' });
    expect(gridApiParams({ statuses: ['refunded'], q: 'x', sort: 'quantity', dir: 'asc', page: 2, pageSize: 100 })).toEqual({
      page: '2',
      pageSize: '100',
      status: ['refunded'],
      q: 'x',
      sort: 'quantity',
      dir: 'asc',
    });
  });
});