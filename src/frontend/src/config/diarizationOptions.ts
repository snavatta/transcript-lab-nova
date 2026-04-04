export type DiarizationMode = 'Basic' | 'Improved';

export const DIARIZATION_MODES: { value: DiarizationMode; label: string; description: string }[] = [
  {
    value: 'Basic',
    label: 'Basic',
    description: 'Pitch-based clustering, up to 3 speakers',
  },
  {
    value: 'Improved',
    label: 'Improved',
    description: 'Richer spectral analysis, up to 6 speakers',
  },
];
