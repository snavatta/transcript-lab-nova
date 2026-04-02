let mediaPlayerSeek: ((ms: number) => void) | null = null;

export function registerMediaPlayerSeek(handler: (ms: number) => void) {
  mediaPlayerSeek = handler;

  return () => {
    if (mediaPlayerSeek === handler) {
      mediaPlayerSeek = null;
    }
  };
}

export function seekMediaPlayer(ms: number) {
  mediaPlayerSeek?.(ms);
}
