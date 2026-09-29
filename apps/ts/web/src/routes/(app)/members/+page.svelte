<script lang="ts">
  import { t } from '$lib/i18n';
</script>

<svelte:head>
  <title>{t('app.titleSuffix', { page: t('members.title') })}</title>
</svelte:head>

<h1 class="cf-page-title">{t('members.title')}</h1>
