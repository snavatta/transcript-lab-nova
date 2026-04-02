import { get, post, put, del } from './client';
import type {
  FolderSummaryDto,
  FolderDetailDto,
  CreateFolderRequest,
  UpdateFolderRequest,
} from '../types';

export const foldersApi = {
  list: () => get<FolderSummaryDto[]>('/folders'),
  getById: (id: string) => get<FolderDetailDto>(`/folders/${id}`),
  create: (data: CreateFolderRequest) => post<FolderDetailDto>('/folders', data),
  update: (id: string, data: UpdateFolderRequest) => put<FolderSummaryDto>(`/folders/${id}`, data),
  remove: (id: string) => del(`/folders/${id}`),
};
