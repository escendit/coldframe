<script lang="ts">
  import type { Snippet } from 'svelte';
  import { hasRole, type Role } from '$lib/roles';

  interface Props {
    /** The user's Role on the current Site, from the Server; null when unknown. */
    role: Role | null;
    minimum: Role;
    children: Snippet;
  }

  let { role, minimum, children }: Props = $props();
</script>

<!-- Controls a Role cannot use are hidden, not disabled (UX-DR76). -->
{#if hasRole(role, minimum)}
  {@render children()}
{/if}
