---
name: screener-workflow-tracker
description: "Maintain, configure, troubleshoot, test, and publish the existing Screener / WorkflowTracker .NET 8 WPF desktop app, including Gemini screenshot analysis and its webhook contract. Use for work on this app, not unrelated screen-capture projects."
---

# Screener Workflow Tracker

Maintain the existing app; current source and the user's instructions take precedence over this baseline. Invoke as `$screener-workflow-tracker`.

## Locate and inspect

This skill lives in `.agents/skills/screener-workflow-tracker`. Walk upward to the root containing `WorkflowTracker.csproj`; do not hardcode the checkout path.

Read applicable `AGENTS.md`, [README](../../../README.md) and the relevant implementation. Read [PUBLISHING](../../../PUBLISHING.md) for source/release preparation; [CHANGELOG](../../../CHANGELOG.md) records version history.

- `MainWindow.xaml.cs`: Play/Pause, startup, manual override, settings restart and shutdown.
- `SettingsWindow.xaml.cs`: compact form with initially collapsed Geavanceerd controls.
- `Models/TrackerSettings.cs`, `workflowtracker.defaults.json`: matching defaults and validation.
- `Services/CaptureWorker.cs`, `RecordingSchedule.cs`, `UserActivity.cs`: serial capture, schedule and activity gates.
- `VisionClient.cs`, `WebhookClient.cs`, `WebhookResponse.cs`: request snapshots and upload/response contracts.
- `RemoteSettingsPatch.cs`, `RemoteSettingsUpdater.cs`, `SettingsStore.cs`: allowlist, merged validation and persistence.
- `TextHistory.cs`, `Models/TextObservation.cs`: bounded textual context; `ScreenshotFingerprint.cs`: image deduplication.
- `StartupRegistration.cs`, `UninstallCoordinator.cs`, `UninstallCleanup.ps1`: HKCU startup and owned-file cleanup.
- `Tests/Program.cs` and partial test files: fake/sandbox harness; `ManualTimeProvider`: accelerated multi-timer clock.

## Defaults and ownership

Current release: **1.3.2**, one canonical `publish/WorkflowTracker.exe`.

| Setting | Default |
| --- | --- |
| VisionEndpoint | `https://generativelanguage.googleapis.com/v1beta/openai/chat/completions` |
| VisionModel | `gemini-3.1-flash-lite` |
| WebhookUrl | `https://tellx.nl/tuytelmachinery-assistent/screener` |
| CaptureIntervalSeconds / RequestTimeoutSeconds | 20 / 90 |
| DailyScheduleEnabled / recording hours | true / 06:00 inclusive to 18:00 exclusive, local Windows time |
| SkipWhenIdle / SendScreenshots | true / false |
| Previous AI context | Two complete text analyses, including running reports; no earlier PNGs |

Keys/tokens remain blank in embedded defaults. User settings are DPAPI CurrentUser at `%LOCALAPPDATA%/WorkflowTracker/settings.dat`; never ship this file or log credentials/response bodies. Preserve custom values during migrations. Missing new settings inherit current defaults. Keep the private saved-schema marker so explicitly chosen old prompt text is not rewritten. Verify official Google documentation when changing model selection or discussing prices/deprecations.

Autostart uses the current executable path in HKCU Run, respects the saved opt-out, and shows the overlay after Windows sign-in. A named mutex permits one app per session. Preserve transparent borderless/topmost UI, dragging, ShowActivated=false, ShowInTaskbar=false and native toolwindow style. Native capture uses GetSystemMetrics and GDI, not Forms.Screen or subprocesses; keep GDI/streams disposed and capture off the UI thread.

## Scheduling and explicit Play

Normal startup and daytime Play use the configured daily window. RecordingSchedule uses TimeProvider/local Windows timezone, supports overnight windows and Dutch daylight-saving changes. Times are strict HH:mm and must differ. The worker checks before capture, wakes at schedule boundaries and uses a linked deadline to cancel slow AI/webhook calls at the end time. Outside hours it waits without captures/network, checking at most once a minute. It cannot wake or power on the PC; recheck after resume.

Automatic pause clears text context, fingerprints and input baseline. Track the previous active window end so sleeping through the whole night still starts a fresh morning report. Manual Pause stops the worker and prevents scheduled restart for that stopped session. Amber/play indicates automatic wait, green/pause active, dark/play stopped.

**One click on amber Play, or Play from a stopped state outside hours, starts immediately and runs until Pause/app exit.** MainWindow must await the previous worker before starting the new one. `_manualScheduleOverride` is a session-only bool passed to CaptureWorker and RecordingSchedule.Evaluate; it is neither stored nor remotely configurable. Overridden windows are active with no next boundary. Use the same evaluation for normal ticks and remote-update scheduling, so later mornings/end times do not cancel or clear this session. Idle/image filters still apply. Preserve this flag across local settings dialogs, including Cancel; reset on explicit Pause and app restart. Remote uninstall remains effective.

UserActivity reads only GetLastInputInfo's uint timestamp for this Windows session. Do not record keys or coordinates. Compare the timestamp sampled before the last actual screenshot; input during processing must trigger a later capture. Without new input after AI success, skip capture itself, AI and webhook. First capture, AI-error retry and one reassessment after a changed remote configuration are exceptions. Input-read failure is fail-open. Idle skips retain context; background-only changes are intentionally skipped unless SkipWhenIdle is disabled.

After capture, compare full PNG SHA-256 fingerprints. Identical consecutive images after AI success (including null) skip AI and webhook, retaining only the 32-byte hash. Retry after AI errors; A-B-A must be analyzed again. Never retain earlier PNG buffers or introduce overlapping iterations/unbounded queues.

## Analysis and webhook

Vision receives only the current image and up to two full earlier textual results in chronological order. Explicitly label each follow-up and build `rapport` on the earlier report. Keep context entries flat, never recursively nest `vorige_opnamen`. Distinguish task switches and uncertain intent; report incomplete work too. Null means the screen cannot be assessed.

Editable AnalysisPrompt is required (max 10,000 characters), PreviousContextPrefix may be empty (max 2,000). Prefix/context appear only when earlier results exist. Keep the fixed JSON schema instructions separate; prompt-reset changes only prompt and prefix. Output bounds: screenshot_tekst 20,000; activity/goal 4,000 each; rapport 8,000. Preserve two successful analyses even when their webhook upload fails. Pause/exit releases history; there is no durable upload queue.

Every validated non-null analysis, including voltooid=false, sends multipart `workflow` as application/json. It contains screenshot_tekst, activiteit, doel, applicaties, stappenplan, voltooid, rapport, plus app-owned vastgelegd_op_utc and vorige_opnamen. The latter holds at most two text observations. Optional `screenshot` image/png is present only when SendScreenshots=true. No image/base64 in JSON. AI still needs the image when webhook screenshot sending is off. Vision and webhook use separate Bearer credentials; HTTPS only, redirects disabled.

HTTP 200 application/json may return `{"settings":{"VisionModel":"gemini-3.1-flash-lite"}}`. Match exact names; allow only the fields explicitly in RemoteSettingsPatch. Never allow WebhookUrl/WebhookBearerToken or arbitrary reflection/deserialization into settings. Reject duplicate/unknown/null/wrong-type fields; validate the merged configuration atomically, persist before live swap, and leave old settings on failure. Empty/identical patches do not save or trigger repeated analysis. A real change refreshes client request snapshots/timer without discarding report context, except a normal-session schedule change causing automatic pause. An overridden session continues. Bound response reads to 128 KiB even with chunked bodies. Normal ack metadata may accompany settings; action/confirm are reserved.

There is no command poll or server acknowledgement: pause, idle/deduplicated frames, null results and AI errors produce no webhook request. Bad provider credentials/model may require local recovery. See README for the exact settings table, types and payload example.

Uninstall requires HTTP 200 application/json with exactly two distinct keys: `{"action":"uninstall","confirm":"WorkflowTracker"}`, case-sensitive values, at most 4096 bytes. Do not broaden this contract or combine it with settings. Stop the worker and let the existing coordinator/helper validate process identity, hash, owned paths and ready/proceed handshake. Preserve nonrecursive cleanup, reparse-point checks and unrelated files/source/other exe copies. Test real deletion only in the verified sandbox; routine testing does not authorize uninstalling the user's app or uploading real screenshots.

## Verify and prepare publication

```powershell
.\scripts\verify.ps1
```

Run the harness for configuration, scheduling, capture, webhook or cleanup changes. It uses fake HTTP/images/input and sandbox-only deletion, with no live registry writes. Verify UI interactions when changed without enabling real recording just to inspect settings. Report actual checks separately from native OS/architecture coverage.

```powershell
.\scripts\publish.ps1
.\scripts\package-source.ps1
```

The first command builds all three self-contained single-file architectures sequentially, preventing shared obj/RID restore races. For the default command, close the canonical app first: preflight must fail before replacing outputs if it is running. Use -SkipCanonicalCopy when preparing packages without interrupting a local recording; this leaves the installed publish/WorkflowTracker.exe untouched and builds the new x64 exe in staging and its ZIP. The script does not stop processes or delete legacy copies. It produces the fixed x64 exe, one current ZIP per architecture, the source ZIP and SHA256SUMS.txt. Packages must contain exactly one correctly targeted exe; x64 ZIP must match the canonical exe. Preserve WPF trimming=false and native extraction. Do not promise Windows 10 support based only on a Windows 11/x64 run, or native ARM64 testing based on PE inspection.

The second command creates source only from `scripts/source-files.txt`. Keep dotfiles and the repo-local skill in that allowlist; update it when adding root files/directories. Exclude bin/obj/publish, runtime settings, credentials, keys and logs. Validate that an extracted source archive builds/tests, not just that ZIP creation succeeds. Source-only packaging does not refresh full-release checksums; run full publish last before delivery. Do not zip the whole workspace or silently assign a license, configure a GitHub repository or upload assets during local preparation.

When launching the fixed Windows PowerShell 5.1 cleanup helper from a PowerShell 7 parent (including CI), set the child PSModulePath to that selected host's built-in Modules directory so commands such as Get-FileHash resolve correctly. Keep preflight diagnostics on stderr. Sandbox cleanup must not mask the original test failure; clear copied read-only file attributes only inside the prevalidated temporary sandbox.

## GitHub build artifacts and releases

The repository is `niektuytel/TellX.Screener`. `.github/workflows/build.yml` runs on all branch pushes, pull requests, v* tag pushes and manual dispatch. It uses a Windows 2022 runner and .NET 8, runs the existing x64 harness and self-contained x86 tests, then the sequential publish script and verify-packages.ps1. ARM64 is built, not natively tested. Never supply real Vision credentials to CI.

Official actions are pinned to commit SHAs. upload-artifact v7 uses archive=false for each existing ZIP/checksum file; do not wrap downloads in another ZIP or upload the whole workspace. The source manifest must include `.github/workflows/*.yml` and the repo-local skill. Uploads fail if files are missing, replace same-run artifacts on reruns and retain them 30 days. The job summary contains explicit platform download links. Actions downloads require sign-in/read access; public downloads come from Releases.

Only a push of a version tag matching WorkflowTracker.csproj Version (for example v1.3.2) enables the separate contents-write release job. Other jobs have contents-read permission and no persisted checkout credentials; do not use pull_request_target. download-artifact v8 must use skip-decompress=true to preserve the uploaded ZIPs. SHA256SUMS.txt uses LF so Linux sha256sum --check works. New releases are drafts until assets finish uploading; an interrupted draft can resume, and published release assets stay untouched on rerun. Prerelease version tags get prerelease status. Latest stable release links become available only after the first stable tagged release succeeds. This workflow does not sign binaries or add an app updater.

Lint the YAML and validate action inputs against the pinned versions before publishing workflow edits. Run the underlying scripts locally where possible, then distinguish local validation from a successful GitHub-hosted run. Source preparation alone does not imply a remote upload; an explicit GitHub deployment request with a specified repository authorizes applying the prepared source/workflow there. Do not create version tags or public releases merely to test routine artifact changes unless the requested publishing scope includes them.

Update README/PUBLISHING/CHANGELOG and this skill when contracts or release procedures change. Repo instructions must stay portable and free of a user's absolute checkout path. Keep the personal installed skill aligned when modifying the app's skill.
