import { get, post, put } from './client';
import type {
  GlobalSettingsDto,
  ManageTranscriptionModelRequest,
  SystemCapabilitiesDto,
  TranscriptionModelCatalogDto,
  TranscriptionModelEntryDto,
  TranscriptionOptionsDto,
  UpdateGlobalSettingsRequest,
} from '../types';

export const settingsApi = {
  get: () => get<GlobalSettingsDto>('/settings'),
  getOptions: () => get<TranscriptionOptionsDto>('/settings/options'),
  getModels: () => get<TranscriptionModelCatalogDto>('/settings/models'),
  getCapabilities: () => get<SystemCapabilitiesDto>('/settings/capabilities'),
  manageModel: (data: ManageTranscriptionModelRequest) => post<TranscriptionModelEntryDto>('/settings/models/manage', data),
  update: (data: UpdateGlobalSettingsRequest) => put<GlobalSettingsDto>('/settings', data),
};
