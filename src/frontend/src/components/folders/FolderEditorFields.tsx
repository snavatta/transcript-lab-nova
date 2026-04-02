import {
  Autocomplete,
  Box,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import FolderAppearanceAvatar from './FolderAppearanceAvatar';
import {
  DEFAULT_FOLDER_ICON_KEY,
  FOLDER_COLOR_SWATCHES,
  FOLDER_ICON_OPTIONS,
  getFolderIconOption,
  normalizeFolderColorHex,
} from '../../folders/appearance';

interface Props {
  name: string;
  iconKey: string;
  colorHex: string;
  nameError?: string;
  colorError?: string;
  onNameChange: (value: string) => void;
  onIconKeyChange: (value: string) => void;
  onColorHexChange: (value: string) => void;
}

export default function FolderEditorFields({
  name,
  iconKey,
  colorHex,
  nameError,
  colorError,
  onNameChange,
  onIconKeyChange,
  onColorHexChange,
}: Props) {
  const normalizedColorHex = normalizeFolderColorHex(colorHex);
  const selectedIconOption = getFolderIconOption(iconKey);

  return (
    <Stack spacing={2}>
      <TextField
        autoFocus
        label="Folder name"
        fullWidth
        margin="dense"
        value={name}
        onChange={(event) => onNameChange(event.target.value)}
        error={!!nameError}
        helperText={nameError}
        slotProps={{ htmlInput: { maxLength: 120 } }}
      />

      <Autocomplete
        options={FOLDER_ICON_OPTIONS}
        value={selectedIconOption}
        onChange={(_, value) => onIconKeyChange(value?.key ?? DEFAULT_FOLDER_ICON_KEY)}
        isOptionEqualToValue={(option, value) => option.key === value.key}
        getOptionLabel={(option) => option.label}
        filterOptions={(options, state) => {
          const normalizedInput = state.inputValue.trim().toLowerCase();
          const filteredOptions = normalizedInput
            ? options.filter((option) => option.searchText.includes(normalizedInput))
            : options;

          return filteredOptions.slice(0, 100);
        }}
        renderOption={(props, option) => (
          <Box component="li" {...props}>
            <Stack spacing={0.25}>
              <Typography variant="body2">{option.label}</Typography>
              <Typography variant="caption" color="text.secondary">
                {option.key}
              </Typography>
            </Stack>
          </Box>
        )}
        renderInput={(params) => (
          <TextField
            {...params}
            label="Folder icon"
            helperText="Search MUI icon names"
          />
        )}
      />

      <Box>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Folder color
        </Typography>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap sx={{ mb: 1.5 }}>
          {FOLDER_COLOR_SWATCHES.map((swatch) => (
            <Box
              key={swatch}
              component="button"
              type="button"
              onClick={() => onColorHexChange(swatch)}
              aria-label={`Use color ${swatch}`}
              sx={{
                width: 32,
                height: 32,
                borderRadius: 1,
                border: normalizedColorHex === swatch ? '2px solid' : '1px solid',
                borderColor: normalizedColorHex === swatch ? 'text.primary' : 'divider',
                bgcolor: swatch,
                cursor: 'pointer',
              }}
            />
          ))}
        </Stack>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ sm: 'flex-start' }}>
          <TextField
            label="Color hex"
            value={colorHex}
            onChange={(event) => onColorHexChange(event.target.value)}
            error={!!colorError}
            helperText={colorError || 'Use #RRGGBB format'}
            slotProps={{ htmlInput: { maxLength: 7 } }}
            sx={{ flex: 1 }}
          />
          <TextField
            label="Pick color"
            type="color"
            value={normalizedColorHex}
            onChange={(event) => onColorHexChange(event.target.value)}
            sx={{ width: { xs: '100%', sm: 120 } }}
          />
        </Stack>
      </Box>

      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          gap: 1.5,
          p: 1.5,
          border: 1,
          borderColor: 'divider',
          borderRadius: 1.5,
        }}
      >
        <FolderAppearanceAvatar iconKey={iconKey} colorHex={colorHex} size={40} />
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="body2" fontWeight={600} noWrap>
            {name.trim() || 'Folder preview'}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            Icon and color preview
          </Typography>
        </Box>
      </Box>
    </Stack>
  );
}
