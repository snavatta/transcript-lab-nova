import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Box,
  Chip,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import type {
  DiarizationSource,
  HostedProcessingMetadataDto,
  ProjectDebugTimingsDto,
  ProjectSettingsDto,
} from '../../types';
import { formatDuration } from '../../utils/format';
import { formatEngineLabel } from '../../utils/transcription';

interface Props {
  metadata: HostedProcessingMetadataDto | null;
  debugTimings: ProjectDebugTimingsDto | null;
  settings: ProjectSettingsDto;
  projectDurationMs: number | null;
}

const DIARIZATION_SOURCE_LABELS = {
  Local: 'Local',
  Provider: 'Provider',
  Xai: 'xAI',
} as const satisfies Record<DiarizationSource, string>;

function formatUsd(value: number | null): string {
  return value == null ? 'Not reported' : `$${value.toFixed(4)}`;
}

function formatDebugDuration(ms: number | null | undefined): string {
  if (ms == null) return 'Not recorded';
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

function formatRealtimeFactor(value: number | null | undefined): string {
  return value == null ? 'Not recorded' : `${value.toFixed(2)}x`;
}

function formatDiarizationSource(source: string): string {
  switch (source) {
    case 'Local':
      return DIARIZATION_SOURCE_LABELS.Local;
    case 'Provider':
      return DIARIZATION_SOURCE_LABELS.Provider;
    case 'Xai':
      return DIARIZATION_SOURCE_LABELS.Xai;
    default:
      return source;
  }
}

export default function HostedProcessingSummary({
  metadata,
  debugTimings,
  settings,
  projectDurationMs,
}: Props) {
  const sttProvider = metadata?.sttProvider ?? formatEngineLabel(settings.engine);
  const sttModel = metadata?.sttModel ?? settings.model;
  const sttExecution = metadata ? 'Hosted' : 'Local';
  const roleUsed = metadata?.roleAttributionModel != null || metadata?.roleAttributionStatus != null;
  const totalLabel = metadata?.totalContainsEstimate ? 'Total (includes estimate)' : 'Total (actual)';
  const localDiarization = settings.diarizationEnabled && settings.diarizationSource === 'Local';
  const directNativeDiarization = metadata?.nativeDiarizationUsed === true;
  const diarizationSource = metadata?.diarizationSource
    ?? (directNativeDiarization ? 'Provider' : localDiarization ? 'Local' : 'Not used');
  const diarizationSourceLabel = formatDiarizationSource(diarizationSource);
  const diarizationExecution = directNativeDiarization || (metadata?.diarizationSource != null && metadata.diarizationSource !== 'Local')
    ? 'Hosted'
    : diarizationSource === 'Local'
      ? 'Local'
      : 'Not used';
  const diarizationProviderModel = [metadata?.diarizationProvider, metadata?.diarizationModel]
    .filter((value): value is string => value != null && value.length > 0)
    .join(' · ');

  return (
    <Accordion variant="outlined" disableGutters>
      <AccordionSummary
        id="processing-details-header"
        aria-controls="processing-details-panel"
        expandIcon={<ExpandMoreIcon />}
      >
        <Box sx={{ width: '100%', pr: 1, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1.5, flexWrap: 'wrap' }}>
          <Box>
            <Typography variant="subtitle1">Processing Details</Typography>
            <Typography variant="body2" color="text.secondary">
              STT: {sttProvider} · Local pipeline · Speaker roles: {roleUsed ? 'used' : 'not used'}
            </Typography>
          </Box>
          {metadata?.totalCostUsd != null && (
            <Chip label={`${totalLabel}: ${formatUsd(metadata.totalCostUsd)}`} color="primary" variant="outlined" />
          )}
        </Box>
      </AccordionSummary>

      <AccordionDetails sx={{ pt: 0 }}>
        <Stack spacing={1.5}>
          {debugTimings && (
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
              <Chip size="small" variant="outlined" label={`Overall time: ${formatDebugDuration(debugTimings.totalElapsedMs)}`} />
              <Chip size="small" variant="outlined" label={`Overall realtime factor: ${formatRealtimeFactor(debugTimings.totalRealtimeFactor)}`} />
            </Box>
          )}

          <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 2 } }}>
            <Stack spacing={1}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                <Typography variant="subtitle2">STT model / engine</Typography>
                <Chip size="small" label={sttExecution} color={metadata ? 'primary' : 'default'} variant="outlined" />
              </Box>
              <Typography variant="body2" color="text.secondary">
                {sttProvider} · {sttModel}
              </Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 0.75 }}>
                <Typography variant="body2">
                  Audio duration: {metadata?.audioDurationMs == null && projectDurationMs == null
                    ? 'Unknown'
                    : formatDuration(metadata?.audioDurationMs ?? projectDurationMs ?? 0)}
                </Typography>
                <Typography variant="body2">STT requests: {metadata?.requestCount ?? 1}</Typography>
                <Typography variant="body2">Transcription time: {formatDebugDuration(debugTimings?.transcriptionElapsedMs)}</Typography>
                <Typography variant="body2">Transcription realtime factor: {formatRealtimeFactor(debugTimings?.transcriptionRealtimeFactor)}</Typography>
                {metadata && (
                  <Typography variant="body2">
                    STT cost: {formatUsd(metadata.sttCostUsd)}{metadata.sttCostClassification ? ` (${metadata.sttCostClassification})` : ''}
                  </Typography>
                )}
                {metadata?.sttRateUsdPerHour != null && (
                  <Typography variant="body2">Rate snapshot: ${metadata.sttRateUsdPerHour.toFixed(4)}/hour</Typography>
                )}
              </Box>
            </Stack>
          </Paper>

          <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 2 } }}>
            <Stack spacing={1}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                <Typography variant="subtitle2">Speaker diarization</Typography>
                <Chip
                  size="small"
                  label={diarizationExecution}
                  color={diarizationExecution === 'Hosted' ? 'primary' : 'default'}
                  variant="outlined"
                />
              </Box>
              <Typography variant="body2" color="text.secondary">
                {directNativeDiarization
                  ? 'Native speaker labels were returned with hosted STT.'
                  : diarizationExecution === 'Local'
                    ? 'Speaker diarization ran in the local processing pipeline.'
                    : diarizationExecution === 'Hosted'
                      ? 'Speaker diarization used the hosted provider recorded for this transcript.'
                      : 'No speaker diarization was recorded for this transcript.'}
              </Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 0.75 }}>
                <Typography variant="body2">Diarization source: {diarizationSourceLabel}</Typography>
                {diarizationExecution === 'Local' && (
                  <Typography variant="body2">Local diarization mode: {settings.diarizationMode}</Typography>
                )}
                {metadata && diarizationExecution === 'Hosted' && (
                  <>
                    <Typography variant="body2">Diarization provider/model: {diarizationProviderModel || 'Not reported'}</Typography>
                    <Typography variant="body2">Diarization requests: {metadata.diarizationRequestCount ?? 'Not reported'}</Typography>
                    {directNativeDiarization ? (
                      <Typography variant="body2">Diarization cost: Included in STT (no separate diarization charge)</Typography>
                    ) : (
                      <Typography variant="body2">
                        Diarization cost: {formatUsd(metadata.diarizationCostUsd ?? null)}{metadata.diarizationCostClassification ? ` (${metadata.diarizationCostClassification})` : ''}
                      </Typography>
                    )}
                    {!directNativeDiarization && metadata.diarizationRateUsdPerHour != null && (
                      <Typography variant="body2">Diarization rate snapshot: ${metadata.diarizationRateUsdPerHour.toFixed(4)}/hour</Typography>
                    )}
                  </>
                )}
              </Box>
            </Stack>
          </Paper>

          <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 2 } }}>
            <Stack spacing={1}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                <Typography variant="subtitle2">Local processing</Typography>
                <Chip size="small" label="Local" variant="outlined" />
              </Box>
              <Typography variant="body2" color="text.secondary">
                Media preparation, inspection, extraction, normalization, and result persistence run on this machine.
              </Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 0.75 }}>
                <Typography variant="body2">Audio normalization: {settings.audioNormalizationEnabled ? 'Enabled' : 'Disabled'}</Typography>
                <Typography variant="body2">Local diarization: {localDiarization ? settings.diarizationMode : 'Not used'}</Typography>
                <Typography variant="body2">Prepare: {formatDebugDuration(debugTimings?.preparationElapsedMs)}</Typography>
                <Typography variant="body2">Inspect: {formatDebugDuration(debugTimings?.inspectElapsedMs)}</Typography>
                <Typography variant="body2">Extract: {formatDebugDuration(debugTimings?.extractElapsedMs)}</Typography>
                <Typography variant="body2">Normalize: {formatDebugDuration(debugTimings?.normalizeElapsedMs)}</Typography>
                <Typography variant="body2">Persist: {formatDebugDuration(debugTimings?.persistElapsedMs)}</Typography>
              </Box>
            </Stack>
          </Paper>

          <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 2 } }}>
            <Stack spacing={1}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                <Typography variant="subtitle2">Speaker-role attribution</Typography>
                <Chip size="small" label={roleUsed ? 'Hosted' : 'Not used'} color={roleUsed ? 'primary' : 'default'} variant="outlined" />
              </Box>
              <Typography variant="body2" color="text.secondary">
                {roleUsed
                  ? 'Timestamped speaker turns, not audio, were sent through OpenRouter to the role model.'
                  : 'No speaker-role model was used for this transcript.'}
              </Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 0.75 }}>
                <Typography variant="body2">Status: {metadata?.roleAttributionStatus ?? (settings.speakerRoleAttributionEnabled ? 'Requested; no usage recorded' : 'Disabled')}</Typography>
                {metadata?.roleAttributionModel && (
                  <Typography variant="body2">Role model: {metadata.roleAttributionModel}</Typography>
                )}
                {(metadata?.roleAttributionPromptTokens != null || metadata?.roleAttributionOutputTokens != null) && (
                  <Typography variant="body2">
                    Role tokens: {metadata.roleAttributionPromptTokens ?? 0} input / {metadata.roleAttributionOutputTokens ?? 0} output
                  </Typography>
                )}
                {metadata?.roleAttributionCostUsd != null && (
                  <Typography variant="body2">Role cost: {formatUsd(metadata.roleAttributionCostUsd)} (Actual)</Typography>
                )}
              </Box>
            </Stack>
          </Paper>
        </Stack>
      </AccordionDetails>
    </Accordion>
  );
}
