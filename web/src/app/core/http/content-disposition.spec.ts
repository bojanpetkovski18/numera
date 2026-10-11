import { fileNameFromDisposition } from './content-disposition';

describe('fileNameFromDisposition', () => {
  it('reads the name ASP.NET Core sends (plain and RFC 5987 forms), preferring the encoded one', () => {
    expect(
      fileNameFromDisposition("attachment; filename=numera-orders_all.csv; filename*=UTF-8''numera-orders_%C3%A9.csv"),
    ).toBe('numera-orders_é.csv');
    expect(fileNameFromDisposition('attachment; filename=numera-orders_all.csv')).toBe('numera-orders_all.csv');
    expect(fileNameFromDisposition('attachment; filename="with space.csv"')).toBe('with space.csv');
  });

  it('returns null when there is no usable name', () => {
    expect(fileNameFromDisposition(null)).toBeNull();
    expect(fileNameFromDisposition('attachment')).toBeNull();
    expect(fileNameFromDisposition('attachment; filename=""')).toBeNull();
  });

  it('never returns a path', () => {
    expect(fileNameFromDisposition('attachment; filename="../../evil.csv"')).toBe('.._.._evil.csv');
    expect(fileNameFromDisposition("attachment; filename*=UTF-8''..%2F..%5Cevil.csv")).toBe('.._.._evil.csv');
  });
});
