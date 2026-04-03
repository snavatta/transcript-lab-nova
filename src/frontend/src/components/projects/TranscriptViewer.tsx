import { useState, useMemo } from 'react';
import { Box, Typography, TextField, ToggleButtonGroup, ToggleButton, IconButton, Tooltip, InputAdornment } from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import { formatTimestamp } from '../../utils/format';
import { seekMediaPlayer } from './mediaPlayerController';
import { useNotification } from '../notifications';
import type { TranscriptDto, TranscriptViewMode } from '../../types';
import { useIsMobile } from '../../hooks/useIsMobile';

interface Props {
  transcript: TranscriptDto;
  defaultViewMode?: TranscriptViewMode;
}

export default function TranscriptViewer({ transcript, defaultViewMode = 'Readable' }: Props) {
  const isMobile = useIsMobile();
  const [viewMode, setViewMode] = useState<TranscriptViewMode>(defaultViewMode);
  const [search, setSearch] = useState('');
  const { notify } = useNotification();

  const filteredSegments = useMemo(() => {
    if (!search.trim()) return transcript.segments;
    const q = search.toLowerCase();
    return transcript.segments.filter((s) => s.text.toLowerCase().includes(q));
  }, [transcript.segments, search]);

  const handleCopy = () => {
    const text = viewMode === 'Readable'
      ? transcript.plainText
      : transcript.segments.map((s) => `[${formatTimestamp(s.startMs)}] ${s.speaker ? `${s.speaker}: ` : ''}${s.text}`).join('\n');
    navigator.clipboard.writeText(text).then(() => notify('Transcript copied'));
  };

  const handleTimestampClick = (ms: number) => {
    seekMediaPlayer(ms);
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25, mb: 2 }}>
        <TextField
          size="small"
          placeholder="Search transcript..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
          sx={{ flexGrow: 1 }}
        />
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <ToggleButtonGroup
            value={viewMode}
            exclusive
            onChange={(_, v) => { if (v) setViewMode(v); }}
            size="small"
            sx={{
              width: { xs: '100%', sm: 'auto' },
              '& .MuiToggleButton-root': {
                flex: { xs: 1, sm: '0 0 auto' },
              },
            }}
          >
            <ToggleButton value="Readable">Readable</ToggleButton>
            <ToggleButton value="Timestamped">Timestamped</ToggleButton>
          </ToggleButtonGroup>
          <Tooltip title="Copy transcript">
            <IconButton onClick={handleCopy} size="small" aria-label="Copy transcript">
              <ContentCopyIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Box>
      </Box>

      <Box sx={{ maxHeight: { xs: 'none', md: 'calc(100vh - 340px)' }, overflow: { xs: 'visible', md: 'auto' }, pr: { xs: 0, md: 1 } }}>
        {viewMode === 'Readable' ? (
          <Typography
            variant="body1"
            sx={{ lineHeight: 1.8, whiteSpace: 'pre-wrap' }}
          >
            {search.trim()
              ? filteredSegments.map((s) => s.text).join(' ')
              : transcript.plainText}
          </Typography>
        ) : (
          <Box>
            {filteredSegments.map((segment, idx) => (
              <Box
                key={idx}
                sx={{
                  display: 'grid',
                  gridTemplateColumns: { xs: '1fr', sm: '60px minmax(0, 1fr)' },
                  gap: { xs: 0.5, sm: 1.5 },
                  mb: 1,
                  '&:hover': { bgcolor: 'action.hover' },
                  borderRadius: 1,
                  p: 0.5,
                }}
              >
                <Typography
                  variant="caption"
                  sx={{
                    color: 'primary.main',
                    cursor: 'pointer',
                    fontFamily: 'monospace',
                    pt: 0.3,
                    flexShrink: 0,
                    '&:hover': { textDecoration: 'underline' },
                  }}
                  onClick={() => handleTimestampClick(segment.startMs)}
                >
                  {formatTimestamp(segment.startMs)}
                </Typography>
                <Box>
                  {segment.speaker && (
                    <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 600, mr: 0.5 }}>
                      {segment.speaker}:
                    </Typography>
                  )}
                  <Typography variant="body2" component="span">{segment.text}</Typography>
                </Box>
              </Box>
            ))}
          </Box>
        )}
      </Box>

      {transcript.detectedLanguage && (
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
          Detected language: {transcript.detectedLanguage}{isMobile ? ' • ' : ' • '}{transcript.segmentCount} segments
        </Typography>
      )}
    </Box>
  );
}
