<script lang="ts">
  import { t } from '$lib/i18n';
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('garden.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('garden.title')}</h1>
