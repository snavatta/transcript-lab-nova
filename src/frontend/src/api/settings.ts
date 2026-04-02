import { get, put } from './client';
import type { GlobalSettingsDto, TranscriptionOptionsDto, UpdateGlobalSettingsRequest } from '../types';

export const settingsApi = {
  get: () => get<GlobalSettingsDto>('/settings'),
  getOptions: () => get<TranscriptionOptionsDto>('/settings/options'),
  update: (data: UpdateGlobalSettingsRequest) => put<GlobalSettingsDto>('/settings', data),
};
