import { get } from './client';
import type { QueueOverviewDto } from '../types';

export const queueApi = {
  overview: () => get<QueueOverviewDto>('/queue'),
};
