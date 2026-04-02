export interface LanguageOption {
  code: string;
  label: string;
}

export const LANGUAGE_OPTIONS: LanguageOption[] = [
  { code: "en", label: "English" },
  { code: "es", label: "Spanish" },
  { code: "fr", label: "French" },
  { code: "de", label: "German" },
  { code: "it", label: "Italian" },
  { code: "pt", label: "Portuguese" },
  { code: "nl", label: "Dutch" },
  { code: "pl", label: "Polish" },
  { code: "cs", label: "Czech" },
  { code: "tr", label: "Turkish" },
  { code: "uk", label: "Ukrainian" },
  { code: "ru", label: "Russian" },
  { code: "ar", label: "Arabic" },
  { code: "hi", label: "Hindi" },
  { code: "zh", label: "Chinese" },
  { code: "ja", label: "Japanese" },
  { code: "ko", label: "Korean" },
  { code: "yue", label: "Cantonese" },
];

export const DEFAULT_FIXED_LANGUAGE_CODE = LANGUAGE_OPTIONS[0].code;

const ENGINE_LANGUAGE_CODES: Record<string, readonly string[]> = {
  SherpaOnnxSenseVoice: ['zh', 'en', 'ja', 'ko', 'yue'],
};

export function getLanguageOptionsForEngine(engine: string): LanguageOption[] {
  const allowedCodes = ENGINE_LANGUAGE_CODES[engine];
  if (!allowedCodes) {
    return LANGUAGE_OPTIONS;
  }

  return allowedCodes
    .map((code) => LANGUAGE_OPTIONS.find((option) => option.code === code))
    .filter((option): option is LanguageOption => Boolean(option));
}

export function coerceFixedLanguageCodeForEngine(engine: string, languageCode: string | null | undefined): string {
  const options = getLanguageOptionsForEngine(engine);
  if (languageCode && options.some((option) => option.code === languageCode)) {
    return languageCode;
  }

  return options[0]?.code ?? DEFAULT_FIXED_LANGUAGE_CODE;
}

export function coerceFixedLanguageCode(languageCode: string | null | undefined): string {
  return coerceFixedLanguageCodeForEngine('', languageCode);
}

export function getLanguageLabel(languageCode: string | null | undefined): string | null {
  if (!languageCode) {
    return null;
  }

  return LANGUAGE_OPTIONS.find((option) => option.code === languageCode)?.label ?? languageCode;
}
