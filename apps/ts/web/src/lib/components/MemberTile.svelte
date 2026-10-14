<script lang="ts">
  import { t } from '$lib/i18n';
  import { displayNameOf, initialsOf, type MemberEntry } from '$lib/members';
  import { roleLabel } from '$lib/sites';

  interface Props {
    member: MemberEntry;
  }

  let { member }: Props = $props();
</script>

<!-- Member tile (UX-DR51): one per Membership. Display-only in Story 9.1; Role change and removal come with Story 9.2. -->
<div class="cf-member-tile">
  <span class={member.self ? 'cf-member-tile__initials cf-member-tile__initials--self' : 'cf-member-tile__initials'} aria-hidden="true">{initialsOf(member)}</span>
  <span class="cf-member-tile__text">
    <span class="cf-member-tile__name">{displayNameOf(member)}</span>
    {#if member.self}
      <span class="cf-member-tile__you">{t('members.you')}</span>
    {/if}
    <span class="cf-member-tile__role">{t(roleLabel[member.role])}</span>
  </span>
</div>

<style>
  .cf-member-tile {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    align-items: center;
    gap: var(--cf-spacing-5);
    min-height: 44px;
    padding: var(--cf-spacing-4) var(--cf-spacing-5);
    border: 1px solid var(--cf-color-border-subtle);
    background: var(--cf-color-background);
  }

  .cf-member-tile__initials {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 3rem;
    min-height: 3rem;
    background: var(--cf-color-layer-01);
    color: var(--cf-color-text-primary);
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
  }

  .cf-member-tile__initials--self {
    background: var(--cf-color-primary);
    color: var(--cf-color-ink-on-bright);
  }

  .cf-member-tile__text {
    display: grid;
    gap: var(--cf-spacing-1);
    min-width: 0;
  }

  .cf-member-tile__name {
    font-family: var(--cf-type-body-lg-font-family);
    font-size: var(--cf-type-body-lg-font-size);
    line-height: var(--cf-type-body-lg-line-height);
    overflow-wrap: anywhere;
  }

  .cf-member-tile__you {
    font-family: var(--cf-type-helper-font-family);
    font-size: var(--cf-type-helper-font-size);
    line-height: var(--cf-type-helper-line-height);
    color: var(--cf-color-text-helper);
  }

  .cf-member-tile__role {
    font-family: var(--cf-type-status-label-font-family);
    font-size: var(--cf-type-status-label-font-size);
    line-height: var(--cf-type-status-label-line-height);
    letter-spacing: var(--cf-type-status-label-letter-spacing);
    text-transform: uppercase;
    color: var(--cf-color-text-secondary);
  }
</style>
