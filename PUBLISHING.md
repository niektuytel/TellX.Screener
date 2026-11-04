# Publishing Screener

Build on Windows with the .NET 8 SDK or a newer compatible SDK. End users receive a self-contained executable and do not need a separately installed .NET runtime.

## Verify the source

```powershell
.\scripts\verify.ps1
```

The harness uses fake screenshots, HTTP and input timestamps. Cleanup tests operate only in a temporary sandbox. It does not capture the real desktop, send requests to Google/the webhook, or write startup registry entries.

## Build the distribution

Close the running app from its context menu, then run:

```powershell
.\scripts\publish.ps1
```

The script checks for a running canonical executable before building, then publishes x64, x86 and ARM64 sequentially. It does not stop processes, change user settings, or remove legacy application copies.

To build all release packages while the local app keeps running, use `-SkipCanonicalCopy`. All ZIPs and checksums are updated, but `publish/WorkflowTracker.exe` remains the installed local copy. The newly built x64 executable is inside its ZIP and at `obj/release-packages/win-x64/WorkflowTracker.exe`. After closing the app, run the default publish command to also update the canonical local copy.

| Output | Purpose |
| --- | --- |
| `publish/WorkflowTracker.exe` | Current standalone x64 executable |
| `publish/packages/WorkflowTracker-win-x64.zip` | One x64 executable |
| `publish/packages/WorkflowTracker-win-x86.zip` | One x86 executable |
| `publish/packages/WorkflowTracker-win-arm64.zip` | One ARM64 executable |
| `publish/packages/WorkflowTracker-source.zip` | Source, tests, documentation and repository-local skill |
| `publish/packages/SHA256SUMS.txt` | SHA-256 hashes for the packages built in this invocation |

For one architecture, use `-RuntimeIdentifiers win-x86` (or `win-x64`/`win-arm64`). This updates only that architecture's ZIP; use the default command when preparing a complete release so all platform packages match the source version.

Source-only packaging, without stopping the app or requiring a build:

```powershell
.\scripts\package-source.ps1
```

This source-only command does not update the full-release checksum file; run `publish.ps1` last for the final matching release set. The source archive uses `scripts/source-files.txt` rather than recursively zipping the workspace. Update that manifest when adding a source directory or root file. Hidden repository files and `.agents/skills/screener-workflow-tracker` are included explicitly. Build outputs, local settings, credentials, logs and signing keys are excluded. Embedded API-key and webhook-token defaults must be empty.

## GitHub Actions downloads

The workflow is [Build Windows downloads](https://github.com/niektuytel/TellX.Screener/actions/workflows/build.yml), configured in `.github/workflows/build.yml`. It runs on every branch push, pull request, version-tag push and manual **Run workflow**. No Gemini API key or custom repository secret is required: all tests use fakes.

After a successful run, open **Actions > Build Windows downloads > the run > Artifacts**. Choose `WorkflowTracker-win-x64.zip` for most PCs, or the matching x86/ARM64 ZIP. The run summary also has download links. The existing ZIP is uploaded directly without another ZIP wrapper. Artifacts require GitHub sign-in/read access and are retained for 30 days; see [GitHub's artifact download instructions](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).

Extract the ZIP and double-click `WorkflowTracker.exe`. There is no separate runtime installation. This does not install automatic app updates.

## Automatic releases and public downloads

Every push to `main` automatically publishes a new stable GitHub Release after the Windows tests, builds, package verification and downloaded checksum verification pass. A push containing several commits releases the final commit of that push. Pull requests, other branches and manual workflow dispatches produce Actions artifacts only.

The app version uses the project's major/minor and adds `github.run_number` to its base patch: base `1.3.2` with run 6 becomes `1.3.8`, then run 7 becomes `1.3.9`. The CI checkout is stamped before testing/packaging, so binaries and the source ZIP agree. No version-bump commit or manual tag is needed. Reruns keep the same version and `build-N` tag. Keep the base version stable between automatic releases; increase major/minor when starting a new version series.

The release job creates its `build-N` tag at the exact source commit. The release title is always `Workflow Tracker`; asset names and the contained `WorkflowTracker.exe` have no version in their names. App version and source commit appear in release notes. Main pushes do not cancel one another. A slower old build can publish its own release, but only a build still at the head of `main` explicitly updates the latest download.

New releases start as drafts, attach all verified assets, then become public. Reruns resume interrupted drafts and preserve already-published assets. Only the release job has contents-write permission; the built-in `GITHUB_TOKEN` is sufficient. Actions must be enabled; no PAT or real Vision credentials are required. Tags created by this token do not start another workflow run.

Public downloads retain the same URLs across releases:

- [Latest release](https://github.com/niektuytel/TellX.Screener/releases/latest)
- [Latest Windows x64 ZIP](https://github.com/niektuytel/TellX.Screener/releases/latest/download/WorkflowTracker-win-x64.zip)
- [Latest Windows x86 ZIP](https://github.com/niektuytel/TellX.Screener/releases/latest/download/WorkflowTracker-win-x86.zip)
- [Latest Windows ARM64 ZIP](https://github.com/niektuytel/TellX.Screener/releases/latest/download/WorkflowTracker-win-arm64.zip)

Releases remain available independently of the 30-day Actions artifact retention. This does not add automatic updates inside the installed app.

### Optional manual version tags

Push a tag matching the Version in `WorkflowTracker.csproj`, for example:

```powershell
git tag v1.3.2
git push origin v1.3.2
```

Manual `v*` tags remain supported and must match the checked-in project version exactly, otherwise the build fails. Prerelease version tags are marked as prereleases. Use a version above the latest automatic app version when publishing a manual stable release.

## Source checkout

Use the contents of `WorkflowTracker-source.zip` for a source checkout. The root of the checkout must contain `WorkflowTracker.csproj`, `.gitignore`, and `.agents`; do not omit dotfiles. The `.gitignore` excludes generated outputs and local credentials. Upload the architecture ZIPs and `SHA256SUMS.txt` as release assets. The source ZIP is also available as a standalone source distribution.

The repository is `niektuytel/TellX.Screener`. Keep `.github`, `.agents` and the root dotfiles when importing the source ZIP so the workflow and skill are available. No license is assigned by the build workflow.

## Signing and platform checks

These builds are unsigned. Code signing requires a verified publisher identity and a trusted signing service/certificate. Signing does not guarantee the absence of SmartScreen warnings; see [Microsoft's explanation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). If signing is added, sign the final executables before creating ZIPs and generating checksums. Do not change an executable after signing it.

The README records actual testing separately from build availability. x64/x86 tests on Windows 11 do not establish native ARM64 or Windows 10 validation. The application remains on the requested .NET 8 target; review supported OS/runtime lifecycle before broad public deployment.
