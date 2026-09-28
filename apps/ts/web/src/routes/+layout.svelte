<script lang="ts">
  import '@coldframe/design-tokens/fonts.css';
  import '@coldframe/design-tokens/tokens.css';
  import '../app.css';
  import type { Snippet } from 'svelte';
  import LiveRegions from '$lib/components/LiveRegions.svelte';

  let { children }: { children: Snippet } = $props();
</script>

{@render children()}
<LiveRegions />
