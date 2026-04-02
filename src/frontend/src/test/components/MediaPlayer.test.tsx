import { describe, it, expect } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import MediaPlayer from '../../components/projects/MediaPlayer';

describe('MediaPlayer', () => {
  it('applies mute state to the underlying media element', () => {
    const { container } = render(<MediaPlayer src="/lesson.mp3" mediaType="Audio" />);
    const mediaElement = container.querySelector('audio');

    expect(mediaElement).not.toBeNull();
    expect(mediaElement?.muted).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Mute' }));
    expect(mediaElement?.muted).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Unmute' }));
    expect(mediaElement?.muted).toBe(false);
  });
});
