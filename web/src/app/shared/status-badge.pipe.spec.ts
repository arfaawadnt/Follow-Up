import { StatusBadgePipe } from './status-badge.pipe';

describe('StatusBadgePipe', () => {
  const pipe = new StatusBadgePipe();

  it('maps positive states to b-ok', () => {
    expect(pipe.transform('Active')).toBe('b-ok');
    expect(pipe.transform('Resolved')).toBe('b-ok');
  });

  it('maps in-progress states to b-info', () => {
    expect(pipe.transform('Scheduled')).toBe('b-info');
    expect(pipe.transform('Sent')).toBe('b-info');
  });

  it('maps negative states to b-bad', () => {
    expect(pipe.transform('Missed')).toBe('b-bad');
    expect(pipe.transform('Open')).toBe('b-bad');
  });

  it('falls back to b-neu for unknown/empty', () => {
    expect(pipe.transform('Whatever')).toBe('b-neu');
    expect(pipe.transform(null)).toBe('b-neu');
  });
});
