<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { DisplayUser } from '$lib/user';
  import AppHeader from './AppHeader.svelte';
  import SideNav from './SideNav.svelte';

  interface Props {
    user: DisplayUser;
    currentPath: string;
    children: Snippet;
  }

  let { user, currentPath, children }: Props = $props();

  let menuOpen = $state(false);
</script>

<div class="cf-app-shell">
  <AppHeader {user} {menuOpen} onmenu={() => (menuOpen = !menuOpen)} />
  <div class="cf-app-shell__body">
    <SideNav {currentPath} open={menuOpen} onnavigate={() => (menuOpen = false)} />
    <main id="cf-main" class="cf-app-shell__main">
      {@render children()}
    </main>
  </div>
</div>

<style>
  .cf-app-shell {
    display: flex;
    flex-direction: column;
    min-height: 100vh;
    min-height: 100dvh;
  }

  .cf-app-shell__body {
    display: grid;
    flex: 1;
    grid-template-columns: minmax(12rem, 16rem) minmax(0, 1fr);
  }

  .cf-app-shell__main {
    min-width: 0;
    padding: var(--cf-spacing-gutter-web);
  }

  @media (max-width: 671.98px) {
    .cf-app-shell__body {
      grid-template-columns: minmax(0, 1fr);
      align-content: start;
    }

    .cf-app-shell__main {
      padding: var(--cf-spacing-gutter-mobile);
    }
  }
</style>
