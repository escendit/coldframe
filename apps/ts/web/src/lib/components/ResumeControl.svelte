<script lang="ts">
  import type { SubmitFunction } from '@sveltejs/kit';
  import { applyAction, enhance } from '$app/forms';
  import { invalidateAll } from '$app/navigation';
  import { announce } from '$lib/announcer.svelte';
  import { t } from '$lib/i18n';
  import { pauseNoticeOf, type PauseFailure, type PauseScope } from '$lib/pause';
  import Button from './Button.svelte';
  import RetryNotice from './RetryNotice.svelte';

  interface Props {
    /** The ID of the form; the notice of a Resume that did not happen is `<id>-notice`. */
    id: string;
    /** The form action that resumes: `?/resume` of the page, or `/garden?/resume` from the shell. */
    action: string;
    /** `siteId`, `scope`, and `deviceId` for a Device. */
    fields: Readonly<Record<string, string>>;
    /** A verb naming the result: "Resume 7C19". */
    label: string;
    /** Who was resumed, for the announcement: the Site's name or the Device's short ID. */
    name: string;
    siteName: string;
    variant?: 'secondary' | 'ghost';
    /** No button of its own: the Site menu item sends the form through `submit()`. */
    hidden?: boolean;
    /** Why the last Resume did not happen. */
    failure?: PauseFailure | null;
    /** False where the caller shows the notice of `failure` itself, in another place than the form. */
    showNotice?: boolean;
  }

  let { id, action, fields, label, name, siteName, variant = 'ghost', hidden = false, failure = $bindable(null), showNotice = true }: Props = $props();

  let form: HTMLFormElement | undefined = $state();
  let working = $state(false);

  const notice = $derived(failure === null ? null : pauseNoticeOf(failure, siteName));
  const scope = $derived<PauseScope>(fields.scope === 'device' ? 'device' : 'site');

  /** Sends the Resume, as its button would. One at a time: a second tap while the first is on its way sends nothing. */
  export function submit(): void {
    if (working) {
      return;
    }
    form?.requestSubmit();
  }

  /** One tap, no modal (UX-DR70). A success reads the page again; a refusal leaves its notice, with Try again where that can help. */
  const submitting: SubmitFunction = () => {
    working = true;
    return async ({ result }) => {
      working = false;
      if (result.type === 'redirect') {
        await applyAction(result);
      } else if (result.type === 'success') {
        failure = null;
        announce(t('pause.resumed', { name }), 'polite');
        await invalidateAll();
      } else if (result.type === 'failure' && typeof result.data?.notice === 'string') {
        failure = result.data as unknown as PauseFailure;
      } else {
        failure = { action: 'resume', scope, notice: 'unexpected' };
      }
    };
  };
</script>

<form {id} bind:this={form} method="POST" {action} class="cf-resume" class:cf-resume--hidden={hidden} use:enhance={submitting}>
  {#each Object.entries(fields) as [field, value] (field)}
    <input type="hidden" name={field} {value} />
  {/each}
  {#if !hidden}
    <Button type="submit" {variant} {label} {working} />
  {/if}
</form>
{#if showNotice && notice !== null}
  <RetryNotice id="{id}-notice" message={notice.message} action={notice.tryAgain ? action : null} {fields} {submitting} />
{/if}

<style>
  .cf-resume {
    margin: 0;
  }

  .cf-resume--hidden {
    display: none;
  }
</style>
