import { postFormData } from './client';
import type { BatchUploadResultDto, ProjectSettingsDto } from '../types';

export interface UploadFileItem {
  originalFileName: string;
  projectName?: string;
}

export interface BatchUploadParams {
  folderId: string;
  autoQueue: boolean;
  settings: ProjectSettingsDto;
  files: File[];
  items: UploadFileItem[];
}

export const uploadsApi = {
  batch: (params: BatchUploadParams) => {
    const formData = new FormData();
    formData.append('folderId', params.folderId);
    formData.append('autoQueue', String(params.autoQueue));
    formData.append('settings', JSON.stringify(params.settings));
    formData.append('items', JSON.stringify(params.items));
    for (const file of params.files) {
      formData.append('files', file);
    }
    return postFormData<BatchUploadResultDto>('/uploads/batch', formData);
  },
};
