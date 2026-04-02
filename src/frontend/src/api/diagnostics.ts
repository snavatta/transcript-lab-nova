import { get } from './client';
import type { DiagnosticsDto } from '../types';

export const diagnosticsApi = {
  get: () => get<DiagnosticsDto>('/diagnostics'),
};
