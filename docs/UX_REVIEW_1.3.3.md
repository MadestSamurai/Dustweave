# 1.3.3 usability review

## Outcome and scope

The interface should help a player choose an account, configure a task, run it, and recover when something fails. Developer test commands are not product navigation. Moving existing controls into another card is not a complete redesign.

This review covers the shell, daily timeline, daily settings, tools, accounts, schedules, plugins, diagnostics, software settings, update page and onboarding. Keep automation behavior and account identity guards intact. Validate with isolated fixtures; do not use a live game for visual work.

## Problems and intended changes

| Area | Problem | Intended experience |
| --- | --- | --- |
| Shell | Appearance, language, onboarding and connection are scattered | Onboarding beside connection; infrequent application preferences in one settings page; short navigation labels |
| Accounts | Login, queue actions, software configuration and developer validation compete | Current login actions together; a separate manageable account list; one queue action |
| Accounts | Restore is detached from save and sign-in | Save, new sign-in and restore on one row |
| Accounts | Batch login check and standalone isolated launch have unclear value | Remove those entries; sandbox parallelism belongs to queue mode, with a plain-language explanation |
| Sandbox repair | Users must interpret a list before the tool has tried matching | Show the unique verified match first; “Not this one” reveals manual selection; ambiguous matches are never silently bound |
| Software settings | Paths and closing behavior have no consistent home | Appearance/language, startup/closing, installation paths, updates/log retention grouped by purpose |
| Startup | Windows startup is missing; opening software and executing tasks are conflated | Two independent opt-in settings; Windows startup never enables a queue by itself |
| Daily settings | Max-width grids float in the center; many unrelated sections open together | Stable left alignment, consistent field widths, deliberate categories and expandable task parameters |
| Controls | Dropdowns resemble a large split button; focus, arrow and popup treatment differ | One calm field surface, restrained chevron, predictable keyboard and focus behavior in both themes |
| Schedule | One plan is fragmented over several cards | A continuous plan editor: enable/time, account order, execution behavior, save; next run and saved state remain legible |
| Diagnostics | Advanced guild experiments and an arbitrary last operation obscure the purpose | Current connection state, an actionable explanation and export/contact workflow only |
| Diagnostics | Timeline navigation and disk cleanup are unrelated to reporting a problem | Remove the timeline shortcut; move retention/cleanup to software settings |
| All pages | Boilerplate, ambiguous disabled controls, excessive borders and nested panels | Keep essential labels; explain only meaningful restrictions; remove empty decoration |
| All pages | Compact and translated layouts have not driven design choices | Review actual renders at 920×650 and default size, in three languages and light/dark themes |

## References and interpretation

- [Microsoft app settings guidance](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings): group related settings, keep infrequent app preferences separate from task commands, disclose dependent options progressively.
- [PowerToys general settings](https://learn.microsoft.com/en-us/windows/powertoys/general): application startup, appearance and updates belong together; issue reporting has an explicit purpose.
- [Windows forms guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/forms): labels, spacing and layout must support readable input rather than decoration.

These are references, not a requirement to copy their components or put every setting into a card. Dustweave retains its existing WPF theme and timeline identity.

## Acceptance

- Every primary action has one obvious home; no silent account selection or binding.
- No advanced guild test UI, recent-action dump, timeline link or disk cleanup on Diagnostics.
- Software preferences persist; Windows startup is off by default and can be disabled without administrator rights.
- Account list still shows at least three complete rows at minimum window size.
- Dropdown keyboard navigation, focus, disabled states and long translated values remain usable.
- All new text is localized; inspect actual screenshots, including empty/error/manual-selection states.
- Record targeted regression results, package integrity and real updater acceptance before publishing.

## Implemented validation

- The `ux-final` isolated desktop run passed. All main pages were rendered at a minimum window of 920×650, plus default-size timeline and schedule views; Simplified Chinese, Traditional Chinese and English were inspected in light and dark themes.
- Empty account lists route to account setup. Account rows, preset selectors and schedule account cards remain usable. Hidden invalid settings are revealed and retain the entered value; search finds nested options across categories.
- Sandbox repair covers a unique verified match and manual correction, with foreign-account binding still rejected. These are fixtures, not a claim of live sandbox repair.
- Startup registration was checked with an in-memory registry: enable/disable, quoted paths, moved installation, failure handling and independence from automatic task execution. Closing choices persist; invalid saved preferences preserve the file and prevent automatic execution.
- Targeted TaskPresets and SandboxInstallation regressions passed (37 checks). Package and updater acceptance are recorded separately in the release deployment receipt.
- Review includes rendered focus/disabled/empty/error states. It does not claim physical screen-reader testing, an actual Windows sign-in test or live game validation.
