import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type SyntheticEvent } from 'react';
import { Box, Button, Skeleton, Tab, Tabs } from '@mui/material';
import RestoreIcon from '@mui/icons-material/Restore';
import SaveIcon from '@mui/icons-material/Save';
import { useSWRConfig } from 'swr';
import TopBar from '../components/shell/TopBar';
import { useNotification } from '../components/notifications';
import { LocalModelManagerPanel } from '../components/settings/LocalModelManagerPanel';
import { SettingsFormPanel } from '../components/settings/SettingsFormPanel';
import { SystemCapabilitiesPanel } from '../components/settings/SystemCapabilitiesPanel';
import { ApiError, settingsApi } from '../api';
import { useSettings, useTranscriptionOptions } from '../hooks/useData';
import type { GlobalSettingsDto, TranscriptionOptionsDto, UpdateGlobalSettingsRequest } from '../types';
import { coerceFixedLanguageCodeForEngine } from '../utils/languages';
import { coerceDiarizationSource } from '../config/diarizationOptions';

type SettingsTab = 0 | 1 | 2;

const settingsTabIds = ['settings', 'local-model-manager', 'system-capabilities'] as const;
const followingSettingsTabs: Record<SettingsTab, SettingsTab> = { 0: 1, 1: 2, 2: 0 };
const precedingSettingsTabs: Record<SettingsTab, SettingsTab> = { 0: 2, 1: 0, 2: 1 };

function scrollTabIntoTabScroller(tabsRoot: HTMLDivElement | null, tab: SettingsTab) {
  const selectedTab = tabsRoot?.querySelector<HTMLElement>(`#${settingsTabIds[tab]}-tab`);
  const scroller = selectedTab?.parentElement?.parentElement;
  if (!selectedTab || !scroller) return;

  const tabBounds = selectedTab.getBoundingClientRect();
  const scrollerBounds = scroller.getBoundingClientRect();
  if (tabBounds.left < scrollerBounds.left) {
    scroller.scrollTo({ left: scroller.scrollLeft + tabBounds.left - scrollerBounds.left });
  } else if (tabBounds.right > scrollerBounds.right) {
    scroller.scrollTo({ left: scroller.scrollLeft + tabBounds.right - scrollerBounds.right });
  }
}

function keyboardTargetTab(activeTab: SettingsTab, key: string): SettingsTab | null {
  switch (key) {
    case 'ArrowRight':
      return followingSettingsTabs[activeTab];
    case 'ArrowLeft':
      return precedingSettingsTabs[activeTab];
    case 'Home':
      return 0;
    case 'End':
      return 2;
    default:
      return null;
  }
}

function toSettingsForm(
  settings: GlobalSettingsDto,
  transcriptionOptions: TranscriptionOptionsDto | undefined,
): UpdateGlobalSettingsRequest {
  return {
    defaultEngine: settings.defaultEngine,
    defaultModel: settings.defaultModel,
    defaultLanguageMode: settings.defaultLanguageMode,
    defaultLanguageCode: settings.defaultLanguageMode === 'Fixed'
      ? coerceFixedLanguageCodeForEngine(settings.defaultEngine, settings.defaultLanguageCode)
      : null,
    defaultAudioNormalizationEnabled: settings.defaultAudioNormalizationEnabled,
    defaultDiarizationEnabled: settings.defaultDiarizationEnabled,
    defaultDiarizationSource: coerceDiarizationSource(
      transcriptionOptions,
      settings.defaultDiarizationSource,
      settings.defaultEngine,
      settings.defaultModel,
    ),
    defaultDiarizationMode: settings.defaultDiarizationMode ?? 'Basic',
    defaultSpeakerRoleAttributionEnabled: settings.defaultSpeakerRoleAttributionEnabled,
    defaultTranscriptViewMode: settings.defaultTranscriptViewMode,
  };
}

function tabProps(tab: SettingsTab) {
  const id = settingsTabIds[tab];
  return {
    id: `${id}-tab`,
    'aria-controls': `${id}-tabpanel`,
  };
}

export default function SettingsPage() {
  const { data: settings, isLoading } = useSettings();
  const { data: transcriptionOptions } = useTranscriptionOptions();
  const { notify } = useNotification();
  const { mutate } = useSWRConfig();
  const [form, setForm] = useState<UpdateGlobalSettingsRequest | null>(null);
  const [saving, setSaving] = useState(false);
  const [activeTab, setActiveTab] = useState<SettingsTab>(0);
  const [visitedTabs, setVisitedTabs] = useState<readonly SettingsTab[]>([0]);
  const tabsRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (settings && transcriptionOptions && !form) {
      setForm(toSettingsForm(settings, transcriptionOptions));
    }
  }, [form, settings, transcriptionOptions]);

  useEffect(() => {
    if (!transcriptionOptions) return;
    setForm((current) => {
      if (!current) return current;
      const defaultDiarizationSource = coerceDiarizationSource(
        transcriptionOptions,
        current.defaultDiarizationSource,
        current.defaultEngine,
        current.defaultModel,
      );
      return current.defaultDiarizationSource === defaultDiarizationSource
        ? current
        : { ...current, defaultDiarizationSource };
    });
  }, [transcriptionOptions]);

  useLayoutEffect(() => {
    scrollTabIntoTabScroller(tabsRef.current, activeTab);
  }, [activeTab]);

  const handleTabsKeyDownCapture = (event: KeyboardEvent<HTMLDivElement>) => {
    const targetTab = keyboardTargetTab(activeTab, event.key);
    if (targetTab !== null) scrollTabIntoTabScroller(tabsRef.current, targetTab);
  };

  const handleSave = async () => {
    if (!form) return;
    setSaving(true);
    try {
      await settingsApi.update(form);
      await mutate('settings');
      notify('Settings saved');
    } catch (error) {
      const message = error instanceof ApiError ? error.message : 'Failed to save settings';
      notify(message, 'error');
    } finally {
      setSaving(false);
    }
  };

  const handleReset = () => {
    if (settings) setForm(toSettingsForm(settings, transcriptionOptions));
  };

  const handleTabChange = (_event: SyntheticEvent, tab: SettingsTab) => {
    setActiveTab(tab);
    setVisitedTabs((current) => (current.includes(tab) ? current : [...current, tab]));
  };

  if (isLoading || !form) {
    return (
      <>
        <TopBar title="Settings" />
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          <Skeleton variant="rounded" height={48} />
          <Skeleton variant="rounded" height={360} />
        </Box>
      </>
    );
  }

  return (
    <>
      <TopBar
        title="Settings"
        actions={activeTab === 0 ? (
          <>
            <Button variant="outlined" startIcon={<RestoreIcon />} onClick={handleReset} disabled={saving}>Reset</Button>
            <Button variant="contained" startIcon={<SaveIcon />} onClick={handleSave} disabled={saving}>Save</Button>
          </>
        ) : undefined}
      />

      <Tabs
        ref={tabsRef}
        value={activeTab}
        onChange={handleTabChange}
        onKeyDownCapture={handleTabsKeyDownCapture}
        aria-label="Settings sections"
        selectionFollowsFocus
        variant="scrollable"
        scrollButtons="auto"
        allowScrollButtonsMobile
        sx={{ mb: 3 }}
      >
        <Tab label="Settings" {...tabProps(0)} />
        <Tab label="Local Model Manager" {...tabProps(1)} />
        <Tab label="System Capabilities" {...tabProps(2)} />
      </Tabs>

      <Box role="tabpanel" id="settings-tabpanel" aria-labelledby="settings-tab" hidden={activeTab !== 0}>
        <SettingsFormPanel form={form} setForm={setForm} transcriptionOptions={transcriptionOptions} />
      </Box>

      {visitedTabs.includes(1) && (
        <Box role="tabpanel" id="local-model-manager-tabpanel" aria-labelledby="local-model-manager-tab" hidden={activeTab !== 1}>
          <LocalModelManagerPanel />
        </Box>
      )}

      {visitedTabs.includes(2) && (
        <Box role="tabpanel" id="system-capabilities-tabpanel" aria-labelledby="system-capabilities-tab" hidden={activeTab !== 2}>
          <SystemCapabilitiesPanel />
        </Box>
      )}
    </>
  );
}
