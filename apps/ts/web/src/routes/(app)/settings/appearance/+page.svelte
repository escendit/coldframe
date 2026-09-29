<script lang="ts">
  import ThemeSwitcher from '$lib/components/ThemeSwitcher.svelte';
  import { t } from '$lib/i18n';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('appearance.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('appearance.title')}</h1>

<ThemeSwitcher value={data.theme} />
