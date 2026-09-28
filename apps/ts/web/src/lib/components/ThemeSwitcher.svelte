<script lang="ts">
  import { untrack } from 'svelte';
  import { t } from '$lib/i18n';
  import { applyTheme, serializeThemeCookie, type Theme } from '$lib/theme';
  import SegmentedChoice from './SegmentedChoice.svelte';

  interface Props {
    value: Theme;
  }

  let { value }: Props = $props();

  // The prop seeds the choice; afterwards the switcher owns it.
  let current = $state<Theme>(untrack(() => value));

  const options = [
    { value: 'system', label: t('theme.system') },
    { value: 'light', label: t('theme.light') },
    { value: 'dark', label: t('theme.dark') },
  ] as const;

  /** Applies at once, with no Save, and remembers the choice on this browser only. */
  function choose(theme: Theme): void {
    current = theme;
    applyTheme(document.documentElement, theme);
    document.cookie = serializeThemeCookie(theme, window.location.protocol === 'https:');
  }
</script>

<SegmentedChoice id="cf-theme" label={t('appearance.theme')} helper={t('appearance.themeHelper')} {options} value={current} onchange={choose} />
