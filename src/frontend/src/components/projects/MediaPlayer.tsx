import { useRef, useState, useEffect, useCallback } from 'react';
import { Box, IconButton, Slider, Typography, Stack, Select, MenuItem } from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import PauseIcon from '@mui/icons-material/Pause';
import StopIcon from '@mui/icons-material/Stop';
import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import VolumeOffIcon from '@mui/icons-material/VolumeOff';
import { formatDuration } from '../../utils/format';
import type { MediaType } from '../../types';
import { registerMediaPlayerSeek } from './mediaPlayerController';

interface Props {
  src: string;
  mediaType: MediaType;
  onTimeUpdate?: (timeMs: number) => void;
}

export default function MediaPlayer({ src, mediaType, onTimeUpdate }: Props) {
  const mediaRef = useRef<HTMLMediaElement>(null);
  const [playing, setPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(1);
  const [muted, setMuted] = useState(false);
  const [speed, setSpeed] = useState(1);

  const seekTo = useCallback((ms: number) => {
    if (mediaRef.current) {
      mediaRef.current.currentTime = ms / 1000;
    }
  }, []);

  useEffect(() => {
    return registerMediaPlayerSeek(seekTo);
  }, [seekTo]);

  useEffect(() => {
    if (mediaRef.current) {
      mediaRef.current.muted = muted;
    }
  }, [muted]);

  const handleTimeUpdate = useCallback(() => {
    if (mediaRef.current) {
      const time = mediaRef.current.currentTime * 1000;
      setCurrentTime(time);
      onTimeUpdate?.(time);
    }
  }, [onTimeUpdate]);

  const handleLoadedMetadata = useCallback(() => {
    if (mediaRef.current) setDuration(mediaRef.current.duration * 1000);
  }, []);

  const togglePlay = () => {
    if (!mediaRef.current) return;
    if (playing) {
      mediaRef.current.pause();
    } else {
      mediaRef.current.play();
    }
    setPlaying(!playing);
  };

  const handleStop = () => {
    if (!mediaRef.current) return;
    mediaRef.current.pause();
    mediaRef.current.currentTime = 0;
    setPlaying(false);
  };

  const handleSeek = (_: Event, value: number | number[]) => {
    const ms = value as number;
    if (mediaRef.current) mediaRef.current.currentTime = ms / 1000;
    setCurrentTime(ms);
  };

  const handleVolumeChange = (_: Event, value: number | number[]) => {
    const v = value as number;
    setVolume(v);
    if (mediaRef.current) mediaRef.current.volume = v;
    setMuted(v === 0);
  };

  const handleSpeedChange = (newSpeed: number) => {
    setSpeed(newSpeed);
    if (mediaRef.current) mediaRef.current.playbackRate = newSpeed;
  };

  const isVideo = mediaType === 'Video';

  return (
    <Box sx={{ bgcolor: 'grey.900', borderRadius: 1, overflow: 'hidden' }}>
      {isVideo ? (
        <Box
          component="video"
          ref={mediaRef as React.RefObject<HTMLVideoElement>}
          src={src}
          onTimeUpdate={handleTimeUpdate}
          onLoadedMetadata={handleLoadedMetadata}
          onEnded={() => setPlaying(false)}
          sx={{ width: '100%', display: 'block', maxHeight: 360 }}
        />
      ) : (
        <Box
          component="audio"
          ref={mediaRef as React.RefObject<HTMLAudioElement>}
          src={src}
          onTimeUpdate={handleTimeUpdate}
          onLoadedMetadata={handleLoadedMetadata}
          onEnded={() => setPlaying(false)}
          preload="metadata"
        />
      )}
      <Box sx={{ p: 1.5, bgcolor: 'grey.900', color: 'grey.100' }}>
        <Slider
          value={currentTime}
          max={duration || 100}
          onChange={handleSeek}
          size="small"
          sx={{ color: 'primary.light', py: 0.5 }}
        />
        <Stack direction="row" alignItems="center" spacing={0.5}>
          <IconButton onClick={togglePlay} size="small" sx={{ color: 'grey.100' }} aria-label={playing ? 'Pause' : 'Play'}>
            {playing ? <PauseIcon /> : <PlayArrowIcon />}
          </IconButton>
          <IconButton onClick={handleStop} size="small" sx={{ color: 'grey.100' }} aria-label="Stop">
            <StopIcon />
          </IconButton>
          <Typography variant="caption" sx={{ minWidth: 100, textAlign: 'center' }}>
            {formatDuration(currentTime)} / {formatDuration(duration)}
          </Typography>
          <Box sx={{ flexGrow: 1 }} />
          <IconButton onClick={() => setMuted(!muted)} size="small" sx={{ color: 'grey.100' }} aria-label={muted ? 'Unmute' : 'Mute'}>
            {muted ? <VolumeOffIcon /> : <VolumeUpIcon />}
          </IconButton>
          <Slider
            value={muted ? 0 : volume}
            min={0}
            max={1}
            step={0.05}
            onChange={handleVolumeChange}
            size="small"
            sx={{ width: 80, color: 'grey.400' }}
          />
          <Select
            value={speed}
            onChange={(e) => handleSpeedChange(e.target.value as number)}
            size="small"
            variant="standard"
            sx={{ color: 'grey.100', fontSize: '0.75rem', '& .MuiSelect-icon': { color: 'grey.400' } }}
          >
            {[0.5, 0.75, 1, 1.25, 1.5, 2].map((s) => (
              <MenuItem key={s} value={s}>{s}x</MenuItem>
            ))}
          </Select>
        </Stack>
      </Box>
    </Box>
  );
}
