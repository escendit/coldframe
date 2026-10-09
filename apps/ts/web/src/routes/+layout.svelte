<script lang="ts">
  import '@coldframe/design-tokens/fonts.css';
  import '@coldframe/design-tokens/tokens.css';
  import '../app.css';
  import type { Snippet } from 'svelte';
  import LiveRegions from '$lib/components/LiveRegions.svelte';
  import { serializeBrowserZoneCookie } from '$lib/notifications';

  let { children }: { children: Snippet } = $props();

  $effect(() => {
    // Tells the web app's server this browser's zone, which no request header carries, so it can hand
    // it to the Server as the detected zone (DW-23). Sign in sets it too, before the first app page loads.
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (zone !== '') {
      document.cookie = serializeBrowserZoneCookie(zone, window.location.protocol === 'https:');
    }
  });
</script>

{@render children()}
<LiveRegions />
