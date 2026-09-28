<script lang="ts">
  import { t } from '$lib/i18n';
  import { roleLabel, type Site } from '$lib/sites';

  interface Props {
    /** The user's Sites in the Server's order; never re-sorted here. */
    sites: readonly Site[];
    currentSite: Site | null;
    currentPath: string;
  }

  let { sites, currentSite, currentPath }: Props = $props();

  const creating = $derived(currentPath === '/sites/new');
  /**
   * Switching keeps the page, except from Create Site, which opens the Garden. It is a full page
   * load: the Server redirects back to the same path without `?site=`, which the client router
   * would otherwise treat as no change and keep the old Site's data.
   */
  const switchPath = $derived(creating ? '/garden' : currentPath);

  function switchHref(site: Site): string {
    return `${switchPath}?site=${encodeURIComponent(site.id)}`;
  }
</script>

<!-- Site switcher on the web (UX-DR23): one tab per Site with its Role, then "New Site". -->
<div class="cf-site-tabs" role="tablist" aria-label={t('sites.tabs')}>
  {#each sites as site (site.id)}
    {@const selected = !creating && site.id === currentSite?.id}
    <a class="cf-site-tabs__tab" class:cf-site-tabs__tab--current={selected} role="tab" aria-selected={selected ? 'true' : 'false'} href={switchHref(site)} data-sveltekit-reload>
      <span class="cf-site-tabs__name">{site.name}</span>
      <span class="cf-site-tabs__role">{t('sites.tabRole', { role: t(roleLabel[site.role]) })}</span>
    </a>
  {/each}
  <a class="cf-site-tabs__tab" class:cf-site-tabs__tab--current={creating} role="tab" aria-selected={creating ? 'true' : 'false'} href="/sites/new">
    <span class="cf-site-tabs__name">{t('sites.new')}</span>
  </a>
</div>

<style>
  .cf-site-tabs {
    display: flex;
    flex-wrap: wrap;
    align-items: stretch;
    min-width: 0;
  }

  .cf-site-tabs__tab {
    display: inline-flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--cf-spacing-2);
    min-width: 44px;
    min-height: var(--cf-spacing-header-height);
    padding: var(--cf-spacing-2) var(--cf-spacing-5);
    border-bottom: 3px solid transparent;
    color: var(--cf-color-text-on-color);
    font-family: var(--cf-type-body-font-family);
    font-size: var(--cf-type-body-font-size);
    line-height: var(--cf-type-body-line-height);
    text-decoration: none;
    overflow-wrap: anywhere;
  }

  .cf-site-tabs__tab--current {
    border-bottom-color: var(--cf-color-primary);
  }

  .cf-site-tabs__role {
    font-family: var(--cf-type-status-label-font-family);
    font-size: var(--cf-type-status-label-font-size);
    line-height: var(--cf-type-status-label-line-height);
    letter-spacing: var(--cf-type-status-label-letter-spacing);
    text-transform: uppercase;
  }
</style>
