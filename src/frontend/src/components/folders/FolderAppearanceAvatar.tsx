import { createElement, useEffect, useState, type ComponentType } from 'react';
import { Box } from '@mui/material';
import type { SvgIconProps } from '@mui/material/SvgIcon';
import { alpha } from '@mui/material/styles';
import FolderIcon from '@mui/icons-material/Folder';
import {
  DEFAULT_FOLDER_ICON_KEY,
  isSupportedFolderIconKey,
  normalizeFolderColorHex,
} from '../../folders/appearance';

interface Props {
  iconKey?: string;
  colorHex?: string;
  size?: number;
}

const iconModules = import.meta.glob<{ default: ComponentType<SvgIconProps> }>(
  '../../node_modules/@mui/icons-material/*.js',
);
const iconComponentCache = new Map<string, ComponentType<SvgIconProps>>();

const FALLBACK_ICON_PATH = `../../node_modules/@mui/icons-material/${DEFAULT_FOLDER_ICON_KEY}.js`;
const fallbackIconLoader = iconModules[FALLBACK_ICON_PATH]!;
iconComponentCache.set(DEFAULT_FOLDER_ICON_KEY, FolderIcon);

function getResolvedIconKey(iconKey?: string): string {
  if (iconKey && isSupportedFolderIconKey(iconKey)) {
    return iconKey;
  }

  return DEFAULT_FOLDER_ICON_KEY;
}

async function loadIconComponent(iconKey: string) {
  const cachedComponent = iconComponentCache.get(iconKey);
  if (cachedComponent) {
    return cachedComponent;
  }

  const modulePath = `../../node_modules/@mui/icons-material/${iconKey}.js`;
  const loader = iconModules[modulePath] ?? fallbackIconLoader;
  const loadedModule = await loader();
  const loadedComponent = loadedModule.default as ComponentType<SvgIconProps>;
  iconComponentCache.set(iconKey, loadedComponent);
  return loadedComponent;
}

export default function FolderAppearanceAvatar({ iconKey, colorHex, size = 44 }: Props) {
  const resolvedColorHex = normalizeFolderColorHex(colorHex);
  const resolvedIconKey = getResolvedIconKey(iconKey);
  const cachedIconComponent = iconComponentCache.get(resolvedIconKey) ?? null;
  const [loadedIconKey, setLoadedIconKey] = useState(resolvedIconKey);
  const [loadedIconComponent, setLoadedIconComponent] = useState<ComponentType<SvgIconProps> | null>(() => (
    iconComponentCache.get(resolvedIconKey) ?? null
  ));

  useEffect(() => {
    let cancelled = false;
    if (cachedIconComponent) {
      return () => {
        cancelled = true;
      };
    }

    void loadIconComponent(resolvedIconKey).then((loadedComponent) => {
      if (!cancelled) {
        setLoadedIconKey(resolvedIconKey);
        setLoadedIconComponent(() => loadedComponent);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [cachedIconComponent, resolvedIconKey]);

  const IconComponent = (
    loadedIconKey === resolvedIconKey && loadedIconComponent
      ? loadedIconComponent
      : (cachedIconComponent ?? FolderIcon)
  );

  return (
    <Box
      sx={{
        width: size,
        height: size,
        borderRadius: 1.5,
        display: 'grid',
        placeItems: 'center',
        bgcolor: alpha(resolvedColorHex, 0.12),
        color: resolvedColorHex,
        border: `1px solid ${alpha(resolvedColorHex, 0.24)}`,
        flexShrink: 0,
      }}
    >
      {createElement(IconComponent)}
    </Box>
  );
}
