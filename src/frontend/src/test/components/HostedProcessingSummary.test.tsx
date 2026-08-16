import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import HostedProcessingSummary from '../../components/projects/HostedProcessingSummary';

const settings = {
  engine: 'Xai',
  model: 'grok-stt-1.0',
  languageMode: 'Fixed' as const,
  languageCode: 'es',
  audioNormalizationEnabled: true,
  diarizationEnabled: true,
  diarizationSource: 'Provider' as const,
  diarizationMode: 'Basic',
  speakerRoleAttributionEnabled: true,
};

describe('HostedProcessingSummary', () => {
  it('reports direct native xAI diarization as included in STT without a separate charge', () => {
    render(<HostedProcessingSummary
      metadata={{
        sttProvider: 'xAI',
        sttModel: 'grok-stt-1.0',
        audioDurationMs: 3_600_000,
        requestCount: 1,
        nativeDiarizationUsed: true,
        sttCostUsd: 0.1,
        sttRateUsdPerHour: 0.1,
        sttCostClassification: 'Estimated',
        diarizationSource: 'Provider',
        diarizationProvider: 'xAI',
        diarizationModel: 'grok-stt-1.0',
        diarizationRequestCount: 1,
        diarizationCostUsd: null,
        diarizationRateUsdPerHour: null,
        diarizationCostClassification: null,
        roleAttributionModel: 'google/gemini-3.7-flash',
        roleAttributionStatus: 'Completed',
        roleAttributionPromptTokens: 1200,
        roleAttributionOutputTokens: 80,
        roleAttributionCostUsd: 0.002,
        totalCostUsd: 0.102,
        totalContainsEstimate: true,
      }}
      debugTimings={null}
      settings={settings}
      projectDurationMs={3_600_000}
    />);

    expect(screen.getByText('Speaker diarization')).toBeInTheDocument();
    expect(screen.getByText('Diarization cost: Included in STT (no separate diarization charge)')).toBeInTheDocument();
    expect(screen.getByText(/Total \(includes estimate\): \$0\.1020/)).toBeInTheDocument();
  });

  it('keeps server-provided hybrid actual and estimated component costs distinct', () => {
    render(<HostedProcessingSummary
      metadata={{
        sttProvider: 'OpenRouter',
        sttModel: 'openai/whisper-large-v3',
        audioDurationMs: 3_600_000,
        requestCount: 3,
        nativeDiarizationUsed: false,
        sttCostUsd: 0.07,
        sttRateUsdPerHour: 0.07,
        sttCostClassification: 'Actual',
        diarizationSource: 'Xai',
        diarizationProvider: 'xAI',
        diarizationModel: 'grok-stt-1.0',
        diarizationRequestCount: 1,
        diarizationCostUsd: 0.1,
        diarizationRateUsdPerHour: 0.1,
        diarizationCostClassification: 'Estimated',
        roleAttributionModel: 'google/gemini-3.7-flash',
        roleAttributionStatus: 'Completed',
        roleAttributionPromptTokens: 1200,
        roleAttributionOutputTokens: 80,
        roleAttributionCostUsd: 0.002,
        totalCostUsd: 0.172,
        totalContainsEstimate: true,
      }}
      debugTimings={null}
      settings={settings}
      projectDurationMs={3_600_000}
    />);

    expect(screen.getByText(/STT cost: \$0\.0700 \(Actual\)/)).toBeInTheDocument();
    expect(screen.getByText(/Diarization cost: \$0\.1000 \(Estimated\)/)).toBeInTheDocument();
    expect(screen.getByText(/Role cost: \$0\.0020 \(Actual\)/)).toBeInTheDocument();
    expect(screen.getByText(/Total \(includes estimate\): \$0\.1720/)).toBeInTheDocument();
  });

  it('renders the xAI diarization source brand while keeping partial provider checkpoints explicit', () => {
    render(<HostedProcessingSummary
      metadata={{
        sttProvider: 'OpenRouter',
        sttModel: 'openai/whisper-large-v3',
        audioDurationMs: null,
        requestCount: 1,
        nativeDiarizationUsed: false,
        sttCostUsd: null,
        sttRateUsdPerHour: null,
        sttCostClassification: null,
        diarizationSource: 'Xai',
        diarizationProvider: null,
        diarizationModel: null,
        diarizationRequestCount: 0,
        diarizationCostUsd: null,
        diarizationRateUsdPerHour: null,
        diarizationCostClassification: null,
        roleAttributionModel: null,
        roleAttributionStatus: 'Failed',
        roleAttributionPromptTokens: null,
        roleAttributionOutputTokens: null,
        roleAttributionCostUsd: null,
        totalCostUsd: null,
        totalContainsEstimate: false,
      }}
      debugTimings={null}
      settings={settings}
      projectDurationMs={null}
    />);

    expect(screen.getByText('Diarization source: xAI')).toBeInTheDocument();
    expect(screen.getByText('Diarization provider/model: Not reported')).toBeInTheDocument();
    expect(screen.getByText('Diarization cost: Not reported')).toBeInTheDocument();
    expect(screen.getByText('Status: Failed')).toBeInTheDocument();
    expect(screen.queryByText(/undefined|NaN/)).not.toBeInTheDocument();
  });

  it('supports keyboard expansion and a local legacy fallback without hosted cost fields', async () => {
    const user = userEvent.setup();
    render(<HostedProcessingSummary
      metadata={null}
      debugTimings={null}
      settings={{ ...settings, engine: 'WhisperNet', model: 'small', diarizationSource: 'Local' }}
      projectDurationMs={null}
    />);

    const accordion = screen.getByRole('button', { name: /Processing Details/ });
    expect(accordion).toHaveAttribute('aria-controls', 'processing-details-panel');
    expect(accordion).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByText('STT model / engine')).not.toBeVisible();

    await user.tab();
    await user.keyboard('{Enter}');

    expect(accordion).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('Local diarization: Basic')).toBeVisible();
    expect(screen.getByText('Speaker diarization')).toBeVisible();
    expect(screen.queryByText(/STT cost:|Diarization cost:|Total \(/)).not.toBeInTheDocument();

    await user.keyboard(' ');

    expect(accordion).toHaveAttribute('aria-expanded', 'false');
  });
});
