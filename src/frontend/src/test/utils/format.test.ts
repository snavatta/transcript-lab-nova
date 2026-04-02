import { describe, it, expect } from 'vitest';
import { formatBytes, formatDuration, formatTimestamp, removeExtension, isAcceptedFile } from '../../utils/format';

describe('formatBytes', () => {
  it('returns dash for null', () => expect(formatBytes(null)).toBe('—'));
  it('returns dash for 0', () => expect(formatBytes(0)).toBe('—'));
  it('formats bytes', () => expect(formatBytes(500)).toBe('500 B'));
  it('formats KB', () => expect(formatBytes(1536)).toBe('1.5 KB'));
  it('formats MB', () => expect(formatBytes(1048576)).toBe('1.0 MB'));
  it('formats GB', () => expect(formatBytes(1073741824)).toBe('1.0 GB'));
});

describe('formatDuration', () => {
  it('returns dash for null', () => expect(formatDuration(null)).toBe('—'));
  it('formats seconds', () => expect(formatDuration(45000)).toBe('0:45'));
  it('formats minutes', () => expect(formatDuration(125000)).toBe('2:05'));
  it('formats hours', () => expect(formatDuration(3661000)).toBe('1:01:01'));
});

describe('formatTimestamp', () => {
  it('formats without hours', () => expect(formatTimestamp(125000)).toBe('2:05'));
  it('formats with hours', () => expect(formatTimestamp(3661000)).toBe('1:01:01'));
});

describe('removeExtension', () => {
  it('removes .mp4', () => expect(removeExtension('file.mp4')).toBe('file'));
  it('removes .tar.gz last ext', () => expect(removeExtension('file.tar.gz')).toBe('file.tar'));
  it('handles no extension', () => expect(removeExtension('noext')).toBe('noext'));
});

describe('isAcceptedFile', () => {
  it('accepts mp3', () => {
    const file = new File([''], 'test.mp3', { type: 'audio/mpeg' });
    expect(isAcceptedFile(file)).toBe(true);
  });

  it('accepts mp4', () => {
    const file = new File([''], 'video.mp4', { type: 'video/mp4' });
    expect(isAcceptedFile(file)).toBe(true);
  });

  it('rejects txt', () => {
    const file = new File([''], 'readme.txt', { type: 'text/plain' });
    expect(isAcceptedFile(file)).toBe(false);
  });
});
