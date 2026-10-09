<script lang="ts">
  import type { SubmitFunction } from '@sveltejs/kit';
  import { enhance } from '$app/forms';
  import { t } from '$lib/i18n';
  import InlineNotice from './InlineNotice.svelte';

  interface Props {
    /** The ID of the notice; with Try again its form is `<id>-retry`. */
    id: string;
    message: string;
    /** The form action that repeats the change, e.g. `?/setMute`; null where repeating cannot help. */
    action: string | null;
    /** The fields of the attempt, sent again as they were. */
    fields?: Readonly<Record<string, string>>;
    submitting: SubmitFunction;
  }

  let { id, message, action, fields = {}, submitting }: Props = $props();
</script>

<!-- The notice of a change that did not happen. Try again sends the same change once more. -->
{#if action === null}
  <InlineNotice {id} {message} />
{:else}
  <form id="{id}-retry" method="POST" {action} use:enhance={submitting}>
    {#each Object.entries(fields) as [name, value] (name)}
      <input type="hidden" {name} {value} />
    {/each}
    <InlineNotice {id} {message} action={{ label: t('notice.tryAgain') }} />
  </form>
{/if}
