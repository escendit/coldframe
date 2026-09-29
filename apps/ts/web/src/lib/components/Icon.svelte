<script lang="ts" module>
  import add from '@coldframe/design-tokens/icons/add.svg?raw';
  import checkmark from '@coldframe/design-tokens/icons/checkmark.svg?raw';
  import chevronDown from '@coldframe/design-tokens/icons/chevron--down.svg?raw';
  import errorFilled from '@coldframe/design-tokens/icons/error--filled.svg?raw';
  import grid from '@coldframe/design-tokens/icons/grid.svg?raw';
  import notification from '@coldframe/design-tokens/icons/notification.svg?raw';
  import overflowMenuVertical from '@coldframe/design-tokens/icons/overflow-menu--vertical.svg?raw';
  import box from '@coldframe/design-tokens/icons/box.svg?raw';
  import settings from '@coldframe/design-tokens/icons/settings.svg?raw';
  import view from '@coldframe/design-tokens/icons/view.svg?raw';

  const sources = {
    add,
    checkmark,
    'chevron--down': chevronDown,
    'error--filled': errorFilled,
    grid,
    notification,
    'overflow-menu--vertical': overflowMenuVertical,
    box,
    settings,
    view,
  } as const;

  export type IconName = keyof typeof sources;

  /** Drops the generator comment and hides the SVG from assistive technology. */
  function prepare(svg: string): string {
    return svg
      .replace(/<!--[\s\S]*?-->\s*/gu, '')
      .replace('<svg ', '<svg aria-hidden="true" focusable="false" class="cf-icon__svg" ')
      .trim();
  }

  const prepared = Object.fromEntries(Object.entries(sources).map(([name, svg]) => [name, prepare(svg)])) as Record<IconName, string>;
</script>

<script lang="ts">
  interface Props {
    name: IconName;
    size?: 16 | 20 | 24;
  }

  let { name, size = 16 }: Props = $props();
</script>

<span class="cf-icon" data-icon={name} style:--cf-icon-size="{size}px">
  <!-- eslint-disable-next-line svelte/no-at-html-tags -- vendored Carbon SVG from @coldframe/design-tokens, not user input -->
  {@html prepared[name]}
</span>

<style>
  .cf-icon {
    display: inline-flex;
    flex: none;
    inline-size: var(--cf-icon-size);
    block-size: var(--cf-icon-size);
    color: currentColor;
  }

  .cf-icon :global(.cf-icon__svg) {
    inline-size: 100%;
    block-size: 100%;
  }
</style>
