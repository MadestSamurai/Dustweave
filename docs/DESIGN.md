# Dustweave desktop interface

Updated: 2026-10-06. The interface is a WPF desktop workspace for players watching multi-account tasks beside the game. Preserve the timeline, restrained green accent and light/dark/system themes. Source: `src/Desktop/App.xaml`, `DailyTheme.cs`, `Timeline.xaml` and the page views.

## Shared rules

- Use the existing Segoe UI family with system CJK fallback. Body text is 14 DIP, secondary descriptions 12–13, section titles 20, and the current task heading 24. Primary actions and titles use semibold; secondary buttons use normal weight.
- Use theme resources for all surfaces, text and states. Keep content opaque, avoid decorative motion, and preserve the existing system high-contrast fallback.
- Fields and buttons normally have a 36 DIP minimum height and 8 DIP radius. Focus must not change border thickness or move content. Text editing, dropdown scrolling, selected items and keyboard focus are part of a control's behavior, not optional decoration.
- Main content and its header share a 24 DIP outer inset. Pages normally have 20 DIP padding. The dense account table uses 12 DIP vertical padding to retain useful rows at the 920 × 650 DIP minimum window size.
- Leave room for translated labels. Wrap toolbars when needed; trim long table values with a full-value tooltip. Scrolling belongs to the content region, leaving primary actions reachable.

## Pages

- **Timeline:** distinguish the selected plan/history view from unavailable actions. Planned rows show the task and checkbox without repeating generic pending explanations. Use the shared 16-unit, rounded-stroke vector icons in `Icons.xaml`: task symbols before execution, explicit check/play/skip/pause/warning symbols during execution. Keep state text and accessible names; meaning must not depend on color alone. Actual run/history rows retain meaningful details. More detailed records are a secondary disclosure.
- **Settings:** keep the account selector and save action visible. Use the shared disclosure arrow separately from a task's enable checkbox, so expanding options never changes whether the task runs. Rows without options have no empty disclosure. Align summaries with their title text.
- **Accounts:** preserve all management actions. Show the stop action while an operation can be stopped. Keep the account list useful at compact widths; source checks verify room for three rows. The data-location note is available from the account count tooltip instead of repeating a full footer paragraph.
- **Appearance:** system/light/dark are three directly selectable segments with screen/sun/moon symbols, visible labels, selected borders and keyboard focus. Keep selection indices and preference persistence stable. The language selector remains a dropdown.
- **Timeline rail:** draw its full height in the item container, behind the node and content. The keyboard focus border is a separate overlay; its inset must never shorten the rail. Negative margins inside a data template do not reliably bridge container boundaries at fractional DPI.
- **Diagnostics:** lead with current connection status and a concrete next step, then the latest operation, timeline shortcut and feedback actions. Reuse the existing observation; opening, refreshing or expanding this page must not connect, switch accounts or issue game commands. Status summaries exclude account identifiers, names and login data. The folder action opens only the observation journal directory. Guild-specific actions remain in a collapsed advanced section, with a visible stop action while running. The page scrolls without moving its title.
- **Tools:** use the same page padding, control geometry and theme resources. Do not reserve a second footer-sized gap inside sidebar navigation. Scrollbar tracks retain their native available hit area, with a narrow visual thumb that highlights on hover/drag.

## Validation

`build.ps1 -Mode Smoke` exercises the isolated WPF demo without connecting to the game. It captures the five main pages in all three languages and both explicit themes at the minimum window size, plus normal-width plans, running tasks, long errors and dropdown content. Presentation checks cover selection semantics, visible controls, text editing, focus and layout stability. Existing smoke checks also verify that language/theme changes preserve account, plan and preference state.

The second polish pass adds vector-icon coverage, accessible theme-segment selection, six connection states in three languages, summary privacy checks and confirmation that diagnostic refresh/expansion sends no game command (86 presentation checks in total). See the [light](images/diagnostics-light.png) and [dark](images/diagnostics-dark.png) diagnostic views.

The 2026-10-06 pass used the local 150% display scale. It does not establish physical keyboard/screen-reader or a full 100/125/150/200% DPI matrix; those remain separate accessibility checks. Rendered screenshots must be inspected as well as running assertions. Package verification and game validation remain separate from UI smoke testing.
