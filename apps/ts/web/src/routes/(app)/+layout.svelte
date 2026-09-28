<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import AppShell from '$lib/components/AppShell.svelte';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps & { children: Snippet } = $props();
</script>

<AppShell user={data.user} currentPath={page.url.pathname}>
  {@render children()}
</AppShell>
