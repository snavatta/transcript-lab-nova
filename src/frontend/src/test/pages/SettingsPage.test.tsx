import type { ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SettingsPage from '../../pages/SettingsPage';
import { XAI_DIARIZATION_DISCLOSURE } from '../../config/diarizationOptions';

const controls = vi.hoisted(() => ({
  mutate: vi.fn(),
  notify: vi.fn(),
  options: undefined as unknown,
  update: vi.fn(),
}));

vi.mock('../../hooks/useData', () => ({
  useSettings: () => ({
    data: {
      defaultEngine: 'OpenRouter',
      defaultModel: 'openai/whisper-large-v3',
      defaultLanguageMode: 'Auto',
      defaultLanguageCode: null,
      defaultAudioNormalizationEnabled: false,
      defaultDiarizationEnabled: true,
      defaultDiarizationSource: 'Xai',
      defaultDiarizationMode: 'Basic',
      defaultSpeakerRoleAttributionEnabled: false,
      defaultTranscriptViewMode: 'Readable',
    },
    isLoading: false,
  }),
  useTranscriptionOptions: () => ({ data: controls.options }),
}));

vi.mock('../../api', () => ({
  ApiError: class extends Error {},
  settingsApi: { update: controls.update },
}));

vi.mock('../../components/notifications', () => ({
  useNotification: () => ({ notify: controls.notify }),
}));

vi.mock('swr', () => ({
  useSWRConfig: () => ({ mutate: controls.mutate }),
}));

vi.mock('../../components/shell/TopBar', () => ({
  default: ({ actions }: { readonly actions?: ReactNode }) => <header>{actions}</header>,
}));

vi.mock('../../components/settings/LocalModelManagerPanel', () => ({
  LocalModelManagerPanel: () => null,
}));

vi.mock('../../components/settings/SystemCapabilitiesPanel', () => ({
  SystemCapabilitiesPanel: () => null,
}));

const supportedOptions = {
  engines: [
    { engine: 'WhisperNet', models: ['small'], providerDiarizationModels: [], wordTimestampModels: [] },
    { engine: 'OpenRouter', models: ['openai/whisper-large-v3'], providerDiarizationModels: [], wordTimestampModels: ['openai/whisper-large-v3'] },
  ],
  speakerRoleAttributionAvailable: true,
  speakerRoleAttributionModel: 'google/gemini-3.7-flash',
  recommendedHostedEngine: null,
  recommendedHostedModel: null,
  xaiDiarizationAvailable: true,
  xaiDiarizationModel: 'grok-stt-1.0',
};

const directXaiRemovedOptions = {
  ...supportedOptions,
  xaiDiarizationAvailable: false,
  xaiDiarizationModel: null,
};

describe('SettingsPage', () => {
  beforeEach(() => {
    controls.options = supportedOptions;
    controls.update.mockResolvedValue(undefined);
    controls.mutate.mockReset();
    controls.notify.mockReset();
    controls.update.mockClear();
  });

  it('coerces an initialized xAI source to Local when refreshed options remove direct xAI', async () => {
    const user = userEvent.setup();
    const { rerender } = render(<SettingsPage />);

    await expect(screen.getByRole('combobox', { name: 'Diarization Source' })).toHaveTextContent('xAI mode');
    expect(screen.getByText(XAI_DIARIZATION_DISCLOSURE)).toBeVisible();

    await user.click(screen.getAllByRole('switch')[0]);
    controls.options = directXaiRemovedOptions;
    rerender(<SettingsPage />);

    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Diarization Source' })).toHaveTextContent('Local mode'));
    expect(screen.queryByText(XAI_DIARIZATION_DISCLOSURE)).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Local Diarization Mode' })).toBeVisible();

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(controls.update).toHaveBeenCalledWith(expect.objectContaining({
      defaultAudioNormalizationEnabled: true,
      defaultDiarizationSource: 'Local',
    })));
  });
});
