<script lang="ts">
  import { announcements } from '$lib/announcer.svelte';
</script>

<div class="cf-live-region" role="status" aria-live="polite" aria-atomic="true">{announcements.polite}</div>
<div class="cf-live-region" role="alert" aria-live="assertive" aria-atomic="true">{announcements.assertive}</div>

<style>
  .cf-live-region {
    position: absolute;
    width: 1px;
    height: 1px;
    margin: -1px;
    padding: 0;
    border: 0;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }
</style>
