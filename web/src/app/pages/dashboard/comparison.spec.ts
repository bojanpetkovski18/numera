import { changeView, compareParams } from './comparison';

describe('changeView', () => {
  it('shows a rise as up, with its sign, and colours it by whether higher is better', () => {
    expect(changeView(0.124, 'percent', true)).toEqual({ arrow: '▲', text: '+12.4%', spoken: 'Up 12.4%', tone: 'good' });
    expect(changeView(0.124, 'percent', false)?.tone).toBe('bad');
  });

  it('shows a fall as down', () => {
    expect(changeView(-0.0556, 'percent', true)).toEqual({ arrow: '▼', text: '-5.6%', spoken: 'Down 5.6%', tone: 'bad' });
    expect(changeView(-0.0833, 'points', false)).toEqual({ arrow: '▼', text: '-8.3 pts', spoken: 'Down 8.3 pts', tone: 'good' });
  });

  it('calls a change that rounds to zero "no change", so the arrow never contradicts the number', () => {
    expect(changeView(0, 'percent', true)).toEqual({ arrow: '■', text: '0.0%', spoken: 'No change, 0.0%', tone: 'neutral' });
    expect(changeView(0.0004, 'percent', true)?.arrow).toBe('■');
    expect(changeView(-0.0004, 'points', false)).toEqual({ arrow: '■', text: '0.0 pts', spoken: 'No change, 0.0 pts', tone: 'neutral' });
  });

  it('has nothing to show without a comparison value', () => {
    expect(changeView(null, 'percent', true)).toBeNull();
    expect(changeView(undefined, 'points', false)).toBeNull();
  });
});

describe('compareParams', () => {
  it('adds the parameter only when there is a comparison', () => {
    expect(compareParams('year')).toEqual({ compare: 'year' });
    expect(compareParams(null)).toEqual({});
  });
});
