import muiIconNames from './muiIconNames.json';

export type FolderIconKey = string;

export type FolderIconOption = {
  key: FolderIconKey;
  label: string;
  searchText: string;
};

export const DEFAULT_FOLDER_ICON_KEY: FolderIconKey = 'Folder';
export const DEFAULT_FOLDER_COLOR_HEX = '#546E7A';

function formatFolderIconLabel(iconKey: string) {
  return iconKey
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
}

const iconKeys = muiIconNames as string[];

const CURATED_FOLDER_ICON_KEYS = [
  'Folder',
  'School',
  'MenuBook',
  'AutoStories',
  'Science',
  'Biotech',
  'Calculate',
  'Psychology',
  'HistoryEdu',
  'Language',
  'Public',
  'Computer',
  'Code',
  'Mic',
  'Movie',
  'MusicNote',
  'Assignment',
  'Description',
] as const;

export const FOLDER_ICON_OPTIONS: readonly FolderIconOption[] = [
  DEFAULT_FOLDER_ICON_KEY,
  ...iconKeys.filter((iconKey) => iconKey !== DEFAULT_FOLDER_ICON_KEY),
].map((iconKey) => {
  const label = formatFolderIconLabel(iconKey);

  return {
    key: iconKey,
    label,
    searchText: `${iconKey} ${label}`.toLowerCase(),
  };
});

const folderIconKeySet = new Set(FOLDER_ICON_OPTIONS.map((option) => option.key));
const folderIconOptionMap = new Map(FOLDER_ICON_OPTIONS.map((option) => [option.key, option]));

export const FOLDER_COLOR_SWATCHES = [
  '#546E7A',
  '#1565C0',
  '#00838F',
  '#2E7D32',
  '#6A1B9A',
  '#C62828',
  '#EF6C00',
  '#5D4037',
] as const;

export function getFolderIconOption(iconKey?: string) {
  return folderIconOptionMap.get(iconKey ?? DEFAULT_FOLDER_ICON_KEY)
    ?? folderIconOptionMap.get(DEFAULT_FOLDER_ICON_KEY)!;
}

export function getFolderIconPickerOptions(selectedIconKey?: string) {
  const selectedOption = selectedIconKey ? folderIconOptionMap.get(selectedIconKey) : null;
  const curatedOptions = CURATED_FOLDER_ICON_KEYS
    .map((iconKey) => folderIconOptionMap.get(iconKey))
    .filter((option): option is FolderIconOption => Boolean(option));

  if (selectedOption && !curatedOptions.some((option) => option.key === selectedOption.key)) {
    return [selectedOption, ...curatedOptions];
  }

  return curatedOptions;
}

export function isSupportedFolderIconKey(iconKey?: string) {
  return !!iconKey && folderIconKeySet.has(iconKey);
}

export function normalizeFolderColorHex(colorHex?: string) {
  if (!colorHex) {
    return DEFAULT_FOLDER_COLOR_HEX;
  }

  const trimmed = colorHex.trim();
  if (!/^#[0-9A-Fa-f]{6}$/.test(trimmed)) {
    return DEFAULT_FOLDER_COLOR_HEX;
  }

  return trimmed.toUpperCase();
}

export function isValidFolderColorHex(colorHex: string) {
  return /^#[0-9A-Fa-f]{6}$/.test(colorHex.trim());
}
